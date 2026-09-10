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

        if (req.Method == "POST")
        {
            // Assigned first: req.Query returns StringValues, which cannot be matched
            // against string constants in a switch.
            string? action = req.Query["action"];

            return action switch
            {
                "join" => await Join(evt, req),
                "done" => await MarkDone(evt, req),
                _ => Error("Ukendt handling. Brug action=join eller action=done."),
            };
        }

        return await Queue(evt);
    }

    private async Task<IActionResult> Queue(Event evt)
    {
        var queue = (await queueNumberRepository.ReadActiveQueueForEvent(evt.RowKey))
            .OrderBy(q => q.Timestamp)
            .ToArray();

        int minutesPerPerson = evt.EstimateMinutesPerPerson(DefaultMinutesPerPerson);

        return new OkObjectResult(new
        {
            eventId = evt.RowKey,
            title = evt.Title,
            qrCode = evt.QrCode,
            openingHours = evt.OpeningHours,
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
    /// Puts a guest without the app at the back of the queue. The opening-hours check the
    /// app does before letting someone join is deliberately skipped: that one stops people
    /// taking a slot that no longer exists, while this is the staff standing at the
    /// activity deciding to let one more in.
    /// </summary>
    private async Task<IActionResult> Join(Event evt, HttpRequest req)
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

        var ticket = await queueNumberRepository.AddToQueue(evt.RowKey, phoneId, name);
        _logger.LogInformation(
            "Added walk-in queue number {Number} to activity {EventId}", ticket.Number, evt.RowKey);

        return await Queue(evt);
    }

    private async Task<IActionResult> MarkDone(Event evt, HttpRequest req)
    {
        var request = await ReadRequest(req);

        if (request == null || string.IsNullOrWhiteSpace(request.Number))
        {
            return Error("Kønummer mangler.");
        }

        var ticket = await queueNumberRepository.MarkDone(evt.RowKey, request.Number.Trim());

        if (ticket == null)
        {
            // Already served, or served by a staff screen between the page load and the click.
            return new ConflictObjectResult(new { error = "Kønummeret er ikke længere i køen." });
        }

        // Feeds the measured minutes-per-person the wait estimate prefers over the manual value.
        await eventRepository.RecordServiceCompletion(evt.RowKey);
        _logger.LogInformation("Marked queue number {Number} done for activity {EventId}", ticket.Number, evt.RowKey);

        // The event was just rewritten by RecordServiceCompletion, so re-read it rather than
        // reporting a minutes-per-person estimate from before this completion.
        var updated = await eventRepository.FindById(evt.RowKey) ?? evt;

        return await Queue(updated);
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
