using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Seniorfestival.Data.Models;
using Seniorfestival.Data.Repositories;

namespace Seniorfestival.Api;

/// <summary>
/// Backend for the admin site's activity list. Read-only for everything the Google
/// Sheet owns (title, time, location, ...) and writable only for the three fields
/// the sheet has no column for: QrCode, MinutesPerPerson and OpeningHours.
/// Named EventsAdmin rather than AdminEvents because the Functions host reserves
/// every route starting with "admin" and refuses to load the function.
/// </summary>
public class EventsAdmin
{
    private const string ActivityPartitionKey = "Aktivitet";

    // Same fallback the queue estimate uses when an activity has no history and no manual value.
    private const int DefaultMinutesPerPerson = 5;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // The QR code goes into an OData filter (FindByQrCode) and is scanned off a printed
    // sign, so it is kept to characters that are safe in both.
    private static readonly Regex AllowedQrCode = new("^[A-Za-z0-9_-]+$", RegexOptions.Compiled);

    private readonly ILogger<EventsAdmin> _logger;
    private readonly IEventRepository eventRepository;
    private readonly IQueueNumberRepository queueNumberRepository;

    public EventsAdmin(ILogger<EventsAdmin> logger, IEventRepository eventRepository, IQueueNumberRepository queueNumberRepository)
    {
        _logger = logger;
        this.eventRepository = eventRepository;
        this.queueNumberRepository = queueNumberRepository;
    }

    [Function("EventsAdmin")]
    public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Function, "get", "put")] HttpRequest req)
    {
        var unauthorized = AdminAuth.Check(req, _logger, AdminArea.Activities);
        if (unauthorized != null)
        {
            return unauthorized;
        }

        string? eventId = req.Query["eventId"];

        if (req.Method == "PUT")
        {
            if (string.IsNullOrEmpty(eventId))
            {
                return Error("eventId mangler.");
            }

            return await Update(eventId, req);
        }

        return await List();
    }

    /// <summary>
    /// The activities worth setting up a queue for. Untitled rows and rows the sheet has
    /// not marked public are left out: an untitled row is a half-finished sheet line, and
    /// a non-public one is not shown in the app, so nobody can reach its queue.
    /// The admin site groups and sorts the result, so no order is imposed here.
    /// </summary>
    private async Task<IActionResult> List()
    {
        var activities = (await eventRepository.ReadEventsByPartition(ActivityPartitionKey))
            .Where(e => e.Public && !string.IsNullOrWhiteSpace(e.Title))
            .ToArray();

        // Only an activity with a QR code can be queued for, so only those need a count.
        var queueLengths = await ReadQueueLengths(activities);

        // A shared code is deliberate, so the list has to be able to say so rather than
        // leaving it looking like the same code was pasted in twice by mistake.
        var sharingCode = activities
            .Where(e => !string.IsNullOrWhiteSpace(e.QrCode))
            .GroupBy(e => e.QrCode!)
            .ToDictionary(g => g.Key, g => g.ToArray());

        // An activity with no QR code reports a null length rather than 0: there is no
        // queue to be empty.
        return new OkObjectResult(activities
            .Select(e => ToDto(
                e,
                queueLengths.TryGetValue(e.RowKey, out var length) ? length : null,
                OtherDays(sharingCode.GetValueOrDefault(e.QrCode ?? "", []), e))));
    }

    /// <summary>
    /// The days, in festival order, that other rows sharing this row's QR code run on.
    /// </summary>
    private static string[] OtherDays(Event[] sharing, Event evt) => sharing
        .Where(e => e.RowKey != evt.RowKey)
        .Select(e => FestivalDay.Normalize(e.Day))
        .Distinct()
        .OrderBy(day => FestivalDay.Rank(day))
        .ToArray();

    private async Task<Dictionary<string, int>> ReadQueueLengths(Event[] activities)
    {
        var queued = activities.Where(e => !string.IsNullOrWhiteSpace(e.QrCode)).ToArray();

        var counts = await Task.WhenAll(queued.Select(async e =>
            (e.RowKey, Count: (await queueNumberRepository.ReadActiveQueueForEvent(e.RowKey)).Length)));

        return counts.ToDictionary(c => c.RowKey, c => c.Count);
    }

    private async Task<IActionResult> Update(string eventId, HttpRequest req)
    {
        var evt = await eventRepository.FindById(eventId);

        if (evt == null || evt.PartitionKey != ActivityPartitionKey)
        {
            return new NotFoundResult();
        }

        var request = await ReadRequest(req);

        if (request == null)
        {
            return Error("Kunne ikke læse aktiviteten.");
        }

        string qrCode = (request.QrCode ?? "").Trim();
        string openingHours = (request.OpeningHours ?? "").Trim();

        if (qrCode.Length > 0 && !AllowedQrCode.IsMatch(qrCode))
        {
            return Error("QR-koden må kun indeholde bogstaver, tal, bindestreg og underscore.");
        }

        if (request.MinutesPerPerson is int minutes && (minutes < 1 || minutes > 240))
        {
            return Error("Minutter pr. person skal være mellem 1 og 240.");
        }

        string? invalidOpeningHours = ValidateOpeningHours(openingHours);
        if (invalidOpeningHours != null)
        {
            return Error(invalidOpeningHours);
        }

        // Sharing a code across days is the point: an activity that runs Friday to Sunday
        // is one printed sign and one row per day, each with its own queue, and the guest's
        // scan resolves to the row for the day they are standing there on. Two rows on the
        // *same* day cannot be told apart that way, so that stays an error.
        Event[] sharing = [];

        if (qrCode.Length > 0)
        {
            sharing = await eventRepository.ReadEventsByQrCode(qrCode);
            string day = FestivalDay.Normalize(evt.Day);

            var clash = sharing.FirstOrDefault(e =>
                e.RowKey != evt.RowKey && FestivalDay.Normalize(e.Day) == day);

            if (clash != null)
            {
                return Error($"QR-koden bruges allerede af '{clash.Title}' samme dag.");
            }
        }

        evt.QrCode = qrCode.Length > 0 ? qrCode : null;
        evt.MinutesPerPerson = request.MinutesPerPerson;
        evt.OpeningHours = openingHours.Length > 0 ? openingHours : null;

        await eventRepository.UpsertEvent(evt);
        _logger.LogInformation(
            "Updated activity {EventId}: qrCode {QrCode}, minutesPerPerson {Minutes}, openingHours {OpeningHours}",
            eventId, evt.QrCode, evt.MinutesPerPerson, evt.OpeningHours);

        var queueLength = evt.QrCode == null
            ? (int?)null
            : (await queueNumberRepository.ReadActiveQueueForEvent(evt.RowKey)).Length;

        // Only this row was written, so the rows read before the save still describe the
        // other days correctly.
        return new OkObjectResult(ToDto(evt, queueLength, OtherDays(sharing, evt)));
    }

    /// <summary>
    /// Rejects anything the queue estimate would silently drop: Parse() throws away windows
    /// it cannot read, so a typo would leave the activity looking open around the clock.
    /// </summary>
    private static string? ValidateOpeningHours(string openingHours)
    {
        if (openingHours.Length == 0)
        {
            return null;
        }

        var parts = openingHours.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var windows = OpeningHoursCalculator.Parse(openingHours);

        if (windows.Length != parts.Length)
        {
            return "Åbningstider skal skrives som fx 09:00-12:00,13:00-17:00.";
        }

        foreach (var window in windows)
        {
            if (window.End <= window.Start)
            {
                return "Et åbningsinterval skal slutte efter det starter.";
            }
        }

        for (int i = 1; i < windows.Length; i++)
        {
            if (windows[i].Start < windows[i - 1].End)
            {
                return "Åbningsintervallerne må ikke overlappe hinanden.";
            }
        }

        return null;
    }

    private static async Task<EventRequest?> ReadRequest(HttpRequest req)
    {
        try
        {
            string body = await new StreamReader(req.Body).ReadToEndAsync();
            return JsonSerializer.Deserialize<EventRequest>(body, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static object ToDto(Event evt, int? queueLength, string[] sharedDays) => new
    {
        eventId = evt.RowKey,
        title = evt.Title,
        day = evt.Day,
        start = evt.Start,
        end = evt.End,
        location = evt.Location,
        qrCode = evt.QrCode,
        // Other days running the same printed code, so the UI can show it is on purpose.
        sharedDays,
        minutesPerPerson = evt.MinutesPerPerson,
        openingHours = evt.OpeningHours,
        // What the queue actually estimates with right now: measured service times win over
        // the manual value, so the admin can see when their number is not the one in use.
        estimatedMinutesPerPerson = evt.EstimateMinutesPerPerson(DefaultMinutesPerPerson),
        queueLength,
        updated = evt.Timestamp
    };

    private static IActionResult Error(string message) => new BadRequestObjectResult(new { error = message });

    private class EventRequest
    {
        public string? QrCode { get; set; }
        public int? MinutesPerPerson { get; set; }
        public string? OpeningHours { get; set; }
    }
}
