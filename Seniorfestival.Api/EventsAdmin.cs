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
/// Sheet owns (title, time, location, ...) and writable only for the two fields
/// the sheet has no column for: QrCode and MinutesPerPerson.
/// Named EventsAdmin rather than AdminEvents because the Functions host reserves
/// every route starting with "admin" and refuses to load the function.
/// </summary>
public class EventsAdmin
{
    private const string ActivityPartitionKey = "Aktivitet";

    // Same fallback the queue estimate uses when an activity has no history and no manual value.
    private const int DefaultMinutesPerPerson = 5;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // The QR code goes into an OData filter (FindSessionsByQrCode) and is scanned off a printed
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

        // One code on one day is one queue, however many sessions the activity runs that day.
        var days = activities
            .Where(e => !string.IsNullOrWhiteSpace(e.QrCode))
            .GroupBy(GroupKey)
            .ToDictionary(g => g.Key, g => ActivityDay.From(g)!);

        var queueLengths = await ReadQueueLengths(days);

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
                queueLengths.TryGetValue(GroupKey(e), out var length) ? length : null,
                OtherDays(sharingCode.GetValueOrDefault(e.QrCode ?? "", []), e),
                OtherSessions(days.GetValueOrDefault(GroupKey(e)), e))));
    }

    /// <summary>The queue a row's tickets go in: its QR code on its festival day.</summary>
    private static (string QrCode, string Day) GroupKey(Event evt) =>
        (evt.QrCode ?? "", FestivalDay.Normalize(evt.Day));

    /// <summary>
    /// The days, in festival order, that other rows sharing this row's QR code run on. The
    /// row's own day is left out - other sessions on it are the same queue, not another one.
    /// </summary>
    private static string[] OtherDays(Event[] sharing, Event evt) => sharing
        .Select(e => FestivalDay.Normalize(e.Day))
        .Where(day => day != FestivalDay.Normalize(evt.Day))
        .Distinct()
        .OrderBy(day => FestivalDay.Rank(day))
        .ToArray();

    /// <summary>
    /// The times of the other sessions this row shares its queue with today, so the UI can
    /// show that the same list is handed out across all of them.
    /// </summary>
    private static string[] OtherSessions(ActivityDay? day, Event evt) => day == null
        ? []
        : day.Sessions
            .Where(e => e.RowKey != evt.RowKey)
            .Select(e => SessionWindow.FromEvent(e)?.ToString())
            .Where(times => times != null)
            .Select(times => times!)
            .ToArray();

    /// <summary>
    /// One count per queue - read off the day's first session, where the shared tickets live -
    /// so every session of that day reports the same length.
    /// </summary>
    private async Task<Dictionary<(string, string), int>> ReadQueueLengths(
        Dictionary<(string, string), ActivityDay> days)
    {
        var counts = await Task.WhenAll(days.Select(async entry =>
            (entry.Key, Count: (await queueNumberRepository.ReadActiveQueueForEvent(entry.Value.Host.RowKey)).Length)));

        return counts.ToDictionary(c => c.Key, c => c.Count);
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

        if (qrCode.Length > 0 && !AllowedQrCode.IsMatch(qrCode))
        {
            return Error("QR-koden må kun indeholde bogstaver, tal, bindestreg og underscore.");
        }

        if (request.MinutesPerPerson is int minutes && (minutes < 1 || minutes > 240))
        {
            return Error("Minutter pr. person skal være mellem 1 og 240.");
        }

        // Sharing a code is the point: an activity is one printed sign and one row per day
        // it runs - and per time of day it runs - each scan resolving to the rows for the day
        // the guest is standing there on. Several rows on the same day share one queue, so a
        // guest joining in the morning keeps their place into the evening session.
        Event[] sharing = qrCode.Length > 0 ? await eventRepository.ReadEventsByQrCode(qrCode) : [];

        evt.QrCode = qrCode.Length > 0 ? qrCode : null;
        evt.MinutesPerPerson = request.MinutesPerPerson;

        await eventRepository.UpsertEvent(evt);
        _logger.LogInformation(
            "Updated activity {EventId}: qrCode {QrCode}, minutesPerPerson {Minutes}",
            eventId, evt.QrCode, evt.MinutesPerPerson);

        // Built from the rows read before the save plus the row just written, so the group
        // reflects the code this save assigned rather than the one it replaced.
        var day = ActivityDay.From(sharing
            .Where(e => e.RowKey != evt.RowKey && FestivalDay.Normalize(e.Day) == FestivalDay.Normalize(evt.Day))
            .Append(evt));

        var queueLength = day == null
            ? (int?)null
            : (await queueNumberRepository.ReadActiveQueueForEvent(day.Host.RowKey)).Length;

        return new OkObjectResult(ToDto(
            evt,
            queueLength,
            OtherDays(sharing.Where(e => e.RowKey != evt.RowKey).ToArray(), evt),
            OtherSessions(day, evt)));
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

    private static object ToDto(Event evt, int? queueLength, string[] sharedDays, string[] sharedSessions) => new
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
        // Other sessions today running the same code - they hand out one shared queue.
        sharedSessions,
        minutesPerPerson = evt.MinutesPerPerson,
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
    }
}
