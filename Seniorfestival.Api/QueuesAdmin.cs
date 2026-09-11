using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Seniorfestival.Data.Models;
using Seniorfestival.Data.Repositories;

namespace Seniorfestival.Api;

/// <summary>
/// Backend for the admin site's queue view: the waiting tickets for one activity, adding
/// a guest who has no app, and marking a ticket served. <see cref="QueueDone"/> does the
/// same marking for the staff screens; this endpoint exists separately so the admin site
/// can hold its own function key and be revoked without touching anything else.
/// Named QueuesAdmin rather than AdminQueues because the Functions host reserves every
/// route starting with "admin" and refuses to load the function.
/// </summary>
public class QueuesAdmin
{
    private const int DefaultMinutesPerPerson = 5;

    // Marks a ticket the staff typed in rather than one joined from a phone. The app
    // never produces an id in this shape, so the two can't be confused.
    private const string WalkInPhoneIdPrefix = "walkin-";

    private const int MaxNameLength = 60;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ILogger<QueuesAdmin> _logger;
    private readonly IEventRepository eventRepository;
    private readonly IQueueNumberRepository queueNumberRepository;

    public QueuesAdmin(ILogger<QueuesAdmin> logger, IEventRepository eventRepository, IQueueNumberRepository queueNumberRepository)
    {
        _logger = logger;
        this.eventRepository = eventRepository;
        this.queueNumberRepository = queueNumberRepository;
    }

    [Function("QueuesAdmin")]
    public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequest req)
    {
        var unauthorized = AdminAuth.Check(req, _logger, AdminArea.Activities);
        if (unauthorized != null)
        {
            return unauthorized;
        }

        string? eventId = req.Query["eventId"];

        if (string.IsNullOrEmpty(eventId))
        {
            return Error("eventId mangler.");
        }

        var evt = await eventRepository.FindById(eventId);

        if (evt == null)
        {
            return new NotFoundResult();
        }

        // The day's sessions share one queue, so any of them opens the same list: work on the
        // group rather than on whichever row the admin site happened to link to.
        var day = ActivityDay.From(await eventRepository.ReadSessionsForEvent(evt));

        if (day == null)
        {
            return new NotFoundResult();
        }

        if (req.Method == "POST")
        {
            // Assigned first: req.Query returns StringValues, which cannot be matched
            // against string constants in a switch.
            string? action = req.Query["action"];

            return action switch
            {
                "join" => await Join(day, req),
                "done" => await MarkDone(day, req),
                _ => Error("Ukendt handling. Brug action=join eller action=done."),
            };
        }

        return await Queue(day);
    }

    private async Task<IActionResult> Queue(ActivityDay day)
    {
        Event host = day.Host;

        var queue = (await queueNumberRepository.ReadActiveQueueForEvent(host.RowKey))
            .OrderBy(q => q.Timestamp)
            .ToArray();

        int minutesPerPerson = host.EstimateMinutesPerPerson(DefaultMinutesPerPerson);

        return new OkObjectResult(new
        {
            eventId = host.RowKey,
            title = host.Title,
            qrCode = host.QrCode,
            // Every session the queue runs across today, so the staff can see the list is
            // shared and when it has to be emptied by.
            sessions = day.SessionTimes,
            minutesPerPerson,
            totalInQueue = queue.Length,
            tickets = queue.Select((q, index) => new
            {
                number = q.Number,
                name = q.Name,
                joinedAt = q.Timestamp,
                position = index + 1,
                // A walk-in has no phone showing their number, so the staff has to call
                // them out loud. The list says which ones those are.
                walkIn = IsWalkIn(q)
            })
        });
    }

    /// <summary>
    /// Puts a guest without the app at the back of the queue. The check the app does before
    /// letting someone join - whether their turn would fall after the day's last session -
    /// is deliberately skipped: that one stops people taking a slot that no longer exists,
    /// while this is the staff standing at the activity deciding to let one more in.
    /// </summary>
    private async Task<IActionResult> Join(ActivityDay day, HttpRequest req)
    {
        var request = await ReadRequest(req);
        string name = (request?.Name ?? "").Trim();

        if (name.Length == 0)
        {
            // The guest has no phone to read their number off, so the name is the only
            // thing the staff can call them up by.
            return Error("Skriv et navn, så personen kan kaldes op.");
        }

        if (name.Length > MaxNameLength)
        {
            return Error($"Navnet må højst være {MaxNameLength} tegn.");
        }

        // No app means no phone id. A unique synthetic one keeps walk-ins out of every
        // phone-keyed lookup: FindActiveQueueNumber can never match one walk-in against
        // another, and ReadActiveQueueForPhone is only called with an id the app supplied.
        string phoneId = WalkInPhoneIdPrefix + Guid.NewGuid().ToString("N");

        var ticket = await queueNumberRepository.AddToQueue(day.Host.RowKey, phoneId, name);
        _logger.LogInformation(
            "Added walk-in queue number {Number} to activity {EventId}", ticket.Number, day.Host.RowKey);

        return await Queue(day);
    }

    private async Task<IActionResult> MarkDone(ActivityDay day, HttpRequest req)
    {
        var request = await ReadRequest(req);

        if (request == null || string.IsNullOrWhiteSpace(request.Number))
        {
            return Error("Kønummer mangler.");
        }

        Event host = day.Host;
        var ticket = await queueNumberRepository.MarkDone(host.RowKey, request.Number.Trim());

        if (ticket == null)
        {
            // Already served, or served by a staff screen between the page load and the click.
            return new ConflictObjectResult(new { error = "Kønummeret er ikke længere i køen." });
        }

        // Feeds the measured minutes-per-person the wait estimate prefers over the manual value.
        await eventRepository.RecordServiceCompletion(host.RowKey);
        _logger.LogInformation("Marked queue number {Number} done for activity {EventId}", ticket.Number, host.RowKey);

        // The host was just rewritten by RecordServiceCompletion, so re-read it rather than
        // reporting a minutes-per-person estimate from before this completion.
        var updated = await eventRepository.FindById(host.RowKey);

        return await Queue(updated == null
            ? day
            : ActivityDay.From(day.Sessions.Select(e => e.RowKey == updated.RowKey ? updated : e))!);
    }

    private static bool IsWalkIn(QueueNumber ticket) =>
        ticket.PhoneId.StartsWith(WalkInPhoneIdPrefix, StringComparison.Ordinal);

    private static async Task<QueueRequest?> ReadRequest(HttpRequest req)
    {
        try
        {
            string body = await new StreamReader(req.Body).ReadToEndAsync();
            return JsonSerializer.Deserialize<QueueRequest>(body, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IActionResult Error(string message) => new BadRequestObjectResult(new { error = message });

    private class QueueRequest
    {
        /// <summary>The ticket to mark served (action=done).</summary>
        public string? Number { get; set; }

        /// <summary>The guest to put in the queue (action=join).</summary>
        public string? Name { get; set; }
    }
}
