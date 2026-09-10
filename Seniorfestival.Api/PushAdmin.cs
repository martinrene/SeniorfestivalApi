using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Seniorfestival.Api;

/// <summary>
/// Sends a push notification to every subscribed app user, from the admin site.
/// <para>
/// Not to be confused with <see cref="Seniorfestival.System.MyEventsTimerTrigger"/>,
/// which pushes event reminders to the handful of people who saved that event. This one
/// is a broadcast: there is no undo and no "only these people".
/// </para>
/// Named PushAdmin rather than AdminPush because the Functions host reserves every route
/// starting with "admin" and refuses to load the function.
/// </summary>
public class PushAdmin
{
    private const string OneSignalUrl = "https://api.onesignal.com/notifications";

    private const int MaxHeadingLength = 80;
    private const int MaxMessageLength = 240;

    /// <summary>OneSignal's default segment of everyone who accepted notifications.</summary>
    private const string DefaultSegment = "Subscribed Users";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // Static so repeated sends reuse the connection instead of leaving sockets behind.
    private static readonly HttpClient HttpClient = new();

    /// <summary>
    /// Where tapping the notification takes the user. The values are the additionalData
    /// keys the app's own click handler looks for (main.js), and the admin site may only
    /// pick from this list - so a typo cannot produce a notification that opens nothing
    /// or, worse, half a route.
    /// </summary>
    private static readonly Dictionary<string, Dictionary<string, object>?> Targets = new()
    {
        ["none"] = null,
        ["home"] = new() { ["notificationText"] = true },
        ["voting"] = new() { ["vote"] = true },
        ["game"] = new() { ["startGame"] = true },
        ["radio"] = new() { ["startRadio"] = true },
        ["queues"] = new() { ["eventQueueNumber"] = true },
    };

    private readonly ILogger<PushAdmin> _logger;

    public PushAdmin(ILogger<PushAdmin> logger)
    {
        _logger = logger;
    }

    [Function("PushAdmin")]
    public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequest req)
    {
        var unauthorized = AdminAuth.Check(req, _logger, AdminArea.Settings);
        if (unauthorized != null)
        {
            return unauthorized;
        }

        string? appId = Environment.GetEnvironmentVariable("oneSignalAppId");
        string? apiKey = Environment.GetEnvironmentVariable("oneSignalApiKey");
        string segment = Environment.GetEnvironmentVariable("oneSignalSegment")?.Trim() is { Length: > 0 } configured
            ? configured
            : DefaultSegment;

        bool configuredForSending = !string.IsNullOrWhiteSpace(appId) && !string.IsNullOrWhiteSpace(apiKey);

        // The page asks first, so it can say the send button will not work before someone
        // has typed out a message.
        if (req.Method == "GET")
        {
            return new OkObjectResult(new
            {
                configured = configuredForSending,
                segment,
                targets = Targets.Keys
            });
        }

        if (!configuredForSending)
        {
            _logger.LogError("oneSignalAppId or oneSignalApiKey is not configured - cannot send.");

            return new ObjectResult(new { error = "Push er ikke sat op på serveren (oneSignalAppId / oneSignalApiKey)." })
            {
                StatusCode = StatusCodes.Status503ServiceUnavailable
            };
        }

        var request = await ReadRequest(req);

        if (request == null)
        {
            return Error("Kunne ikke læse beskeden.");
        }

        string heading = (request.Heading ?? "").Trim();
        string message = (request.Message ?? "").Trim();
        string target = (request.Target ?? "none").Trim();

        if (heading.Length == 0)
        {
            return Error("Skriv en overskrift.");
        }

        if (heading.Length > MaxHeadingLength)
        {
            return Error($"Overskriften må højst være {MaxHeadingLength} tegn.");
        }

        if (message.Length == 0)
        {
            return Error("Skriv en besked.");
        }

        if (message.Length > MaxMessageLength)
        {
            return Error($"Beskeden må højst være {MaxMessageLength} tegn.");
        }

        if (!Targets.TryGetValue(target, out var data))
        {
            return Error("Ukendt destination for beskeden.");
        }

        var payload = new Dictionary<string, object>
        {
            ["app_id"] = appId!,
            ["target_channel"] = "push",
            ["included_segments"] = new[] { segment },
            ["headings"] = new Dictionary<string, string> { ["en"] = heading },
            ["contents"] = new Dictionary<string, string> { ["en"] = message },
        };

        // Left out entirely rather than sent as null: with no additionalData the app just
        // opens where it was, which is what "none" means.
        if (data != null)
        {
            payload["data"] = data;
        }

        return await Send(payload, apiKey!, heading, target, segment);
    }

    private async Task<IActionResult> Send(
        Dictionary<string, object> payload, string apiKey, string heading, string target, string segment)
    {
        using var content = new StringContent(
            JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");

        using var oneSignalRequest = new HttpRequestMessage(HttpMethod.Post, OneSignalUrl) { Content = content };
        oneSignalRequest.Headers.Authorization = new AuthenticationHeaderValue("Key", apiKey);

        HttpResponseMessage response;

        try
        {
            response = await HttpClient.SendAsync(oneSignalRequest);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Could not reach OneSignal");
            return Error("Kunne ikke få forbindelse til OneSignal. Prøv igen.");
        }

        string body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "OneSignal refused the notification: {Status} {Body}", (int)response.StatusCode, body);

            return Error($"OneSignal afviste beskeden: {ReadOneSignalError(body) ?? $"HTTP {(int)response.StatusCode}"}");
        }

        var (id, recipients) = ReadOneSignalResult(body);

        _logger.LogInformation(
            "Sent push \"{Heading}\" to segment {Segment} (target {Target}), id {Id}, {Recipients} recipients",
            heading, segment, target, id, recipients);

        // OneSignal reports 0 recipients when the segment matched nobody, which is worth
        // seeing: the message was accepted but nobody got it.
        return new OkObjectResult(new { id, recipients, segment });
    }

    /// <summary>OneSignal reports problems as {"errors":["..."]} - or as an object.</summary>
    private static string? ReadOneSignalError(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);

            if (document.RootElement.TryGetProperty("errors", out var errors))
            {
                if (errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
                {
                    return errors[0].ToString();
                }

                return errors.ToString();
            }
        }
        catch (JsonException)
        {
            // Not JSON - fall through to the status code.
        }

        return null;
    }

    private static (string? Id, int? Recipients) ReadOneSignalResult(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);

            string? id = document.RootElement.TryGetProperty("id", out var idValue)
                ? idValue.GetString()
                : null;

            int? recipients = document.RootElement.TryGetProperty("recipients", out var count)
                && count.TryGetInt32(out int parsed)
                ? parsed
                : null;

            return (id, recipients);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private static async Task<PushRequest?> ReadRequest(HttpRequest req)
    {
        try
        {
            string body = await new StreamReader(req.Body).ReadToEndAsync();
            return JsonSerializer.Deserialize<PushRequest>(body, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IActionResult Error(string message) => new BadRequestObjectResult(new { error = message });

    private class PushRequest
    {
        public string? Heading { get; set; }
        public string? Message { get; set; }

        /// <summary>A key from <see cref="Targets"/>; defaults to "none".</summary>
        public string? Target { get; set; }
    }
}
