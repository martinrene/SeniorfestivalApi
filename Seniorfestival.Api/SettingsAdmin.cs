using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Seniorfestival.Data.Models;
using Seniorfestival.Data.Repositories;

namespace Seniorfestival.Api;

/// <summary>
/// Backend for the admin site's settings page: the rows of the Settings table, which the
/// app reads through <see cref="Data"/>. Values take effect on the app's next fetch -
/// Data rebuilds its answer per request, so there is no cache to wait out.
/// Named SettingsAdmin rather than AdminSettings because the Functions host reserves
/// every route starting with "admin" and refuses to load the function.
/// </summary>
public class SettingsAdmin
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // RowKey characters Table Storage rejects, plus the quote that would break the OData
    // filter the name is looked up with.
    private static readonly char[] IllegalNameCharacters = ['/', '\\', '#', '?', '\''];

    // Table Storage caps a string property at 64KB; this leaves room to spare for the
    // HTML blobs (frontpageText and friends) without letting a paste run away.
    private const int MaxValueLength = 32000;

    private readonly ILogger<SettingsAdmin> _logger;
    private readonly ISettingRepository settingRepository;

    public SettingsAdmin(ILogger<SettingsAdmin> logger, ISettingRepository settingRepository)
    {
        _logger = logger;
        this.settingRepository = settingRepository;
    }

    [Function("SettingsAdmin")]
    public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Function, "get", "post", "put")] HttpRequest req)
    {
        var unauthorized = AdminAuth.Check(req, _logger, AdminArea.Settings);
        if (unauthorized != null)
        {
            return unauthorized;
        }

        string? name = req.Query["name"];

        switch (req.Method)
        {
            case "POST":
                return await Create(req);

            case "PUT":
                if (string.IsNullOrEmpty(name))
                {
                    return Error("name mangler.");
                }
                return await Update(name, req);

            default:
                return await List();
        }
    }

    private async Task<IActionResult> List()
    {
        var settings = await settingRepository.ReadAllSettings();

        return new OkObjectResult(settings
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .Select(ToDto));
    }

    private async Task<IActionResult> Update(string name, HttpRequest req)
    {
        if (name.IndexOfAny(IllegalNameCharacters) >= 0)
        {
            return Error("Navnet indeholder tegn der ikke kan slås op.");
        }

        var setting = await settingRepository.FindSetting(name);

        if (setting == null)
        {
            return new NotFoundResult();
        }

        var request = await ReadRequest(req);

        if (request == null)
        {
            return Error("Kunne ikke læse indstillingen.");
        }

        // An empty value is meaningful - blanking frontpageText is how the app is told to
        // show nothing - so only the length is checked, not whether anything was typed.
        string value = request.Value ?? "";

        if (value.Length > MaxValueLength)
        {
            return Error($"Værdien må højst være {MaxValueLength} tegn.");
        }

        setting.Value = value;

        await settingRepository.SaveSetting(setting);
        _logger.LogInformation("Updated setting {Name}", setting.Name);

        return new OkObjectResult(ToDto(setting));
    }

    private async Task<IActionResult> Create(HttpRequest req)
    {
        var request = await ReadRequest(req);

        if (request == null)
        {
            return Error("Kunne ikke læse indstillingen.");
        }

        string name = (request.Name ?? "").Trim();
        string value = request.Value ?? "";

        if (name.Length == 0)
        {
            return Error("Skriv et navn på indstillingen.");
        }

        if (name.IndexOfAny(IllegalNameCharacters) >= 0)
        {
            return Error("Navnet må ikke indeholde / \\ # ? eller '");
        }

        if (value.Length > MaxValueLength)
        {
            return Error($"Værdien må højst være {MaxValueLength} tegn.");
        }

        if (await settingRepository.FindSetting(name) != null)
        {
            return Error($"Der findes allerede en indstilling der hedder '{name}'.");
        }

        // Same shape Data writes when it has to add frontpageText itself, so a row created
        // here is indistinguishable from the ones already in the table.
        var setting = new Setting
        {
            PartitionKey = "Setting",
            RowKey = name,
            Name = name,
            Value = value
        };

        await settingRepository.SaveSetting(setting);
        _logger.LogInformation("Created setting {Name}", name);

        return new OkObjectResult(ToDto(setting));
    }

    private static async Task<SettingRequest?> ReadRequest(HttpRequest req)
    {
        try
        {
            string body = await new StreamReader(req.Body).ReadToEndAsync();
            return JsonSerializer.Deserialize<SettingRequest>(body, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static object ToDto(Setting setting) => new
    {
        name = setting.Name,
        value = setting.Value,
        updated = setting.Timestamp
    };

    private static IActionResult Error(string message) => new BadRequestObjectResult(new { error = message });

    private class SettingRequest
    {
        public string? Name { get; set; }
        public string? Value { get; set; }
    }
}
