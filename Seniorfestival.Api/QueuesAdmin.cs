using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Seniorfestival.Data.Models;
using Seniorfestival.Data.Repositories;

namespace Seniorfestival.Api;

/// <summary>
/// Backend for the admin site's queue view: the waiting tickets for one activity, and
/// marking one served. <see cref="QueueDone"/> does the same marking for the staff
/// screens; this endpoint exists separately so the admin site can hold its own function
/// key and be revoked without touching anything else.
/// Named QueuesAdmin rather than AdminQueues because the Functions host reserves every
/// route starting with "admin" and refuses to load the function.
/// </summary>
public class QueuesAdmin
{
    private const int DefaultMinutesPerPerson = 5;

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
            return await MarkDone(evt, req);
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
                position = index + 1
            })
        });
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

    private static async Task<QueueDoneRequest?> ReadRequest(HttpRequest req)
    {
        try
        {
            string body = await new StreamReader(req.Body).ReadToEndAsync();
            return JsonSerializer.Deserialize<QueueDoneRequest>(body, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IActionResult Error(string message) => new BadRequestObjectResult(new { error = message });

    private class QueueDoneRequest
    {
        public string? Number { get; set; }
    }
}
