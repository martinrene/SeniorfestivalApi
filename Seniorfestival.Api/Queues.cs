using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Seniorfestival.Data.Models;
using Seniorfestival.Data.Repositories;

namespace Seniorfestival.Api;

public class Queues
{
    // Last-resort fallback for activities with no recorded service history and no manual MinutesPerPerson.
    private const int DefaultMinutesPerPerson = 5;

    private readonly ILogger<Queues> _logger;
    private readonly IEventRepository eventRepository;
    private readonly IQueueNumberRepository queueNumberRepository;

    public Queues(ILogger<Queues> logger, IEventRepository eventRepository, IQueueNumberRepository queueNumberRepository)
    {
        _logger = logger;
        this.eventRepository = eventRepository;
        this.queueNumberRepository = queueNumberRepository;
    }

    [Function("Queues")]
    public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequest req)
    {
        _logger.LogInformation("C# HTTP trigger function processed a request.");

        string? phoneId = req.Query["phoneId"];
        if (string.IsNullOrEmpty(phoneId))
        {
            return new OkResult();
        }

        TimeZoneInfo festivalTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Central Europe Standard Time");
        DateTime nowFestival = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, festivalTimeZone);
        TimeOnly nowLocal = TimeOnly.FromDateTime(nowFestival);

        switch (req.Method)
        {
            case "POST":
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                QueueJoinRequest? data = JsonSerializer.Deserialize<QueueJoinRequest>(requestBody, new JsonSerializerOptions(JsonSerializerDefaults.Web));

                if (data == null || string.IsNullOrEmpty(data.QrCode))
                {
                    return new BadRequestResult();
                }

                // One sign covers every row of the activity: the days it runs, and the times it
                // runs on each of them. The scan resolves to the day the guest is standing there
                // on, and that day's sessions share one queue on the first of them.
                var day = ActivityDay.From(await eventRepository.FindSessionsByQrCode(data.QrCode, nowFestival));
                if (day == null)
                {
                    return new NotFoundResult();
                }

                Event host = day.Host;

                QueueNumber? existing = await queueNumberRepository.FindActiveQueueNumber(host.RowKey, phoneId);
                if (existing == null)
                {
                    var activeQueueForEvent = await queueNumberRepository.ReadActiveQueueForEvent(host.RowKey);
                    int minutesPerPersonForJoin = host.EstimateMinutesPerPerson(DefaultMinutesPerPerson);
                    int rawWaitMinutesForJoin = activeQueueForEvent.Length * minutesPerPersonForJoin;

                    if (day.WouldRunPastLastSession(rawWaitMinutesForJoin, nowLocal))
                    {
                        return new ConflictObjectResult(new { message = "Der er ikke flere ledige pladser i køen i dag" });
                    }

                    await queueNumberRepository.AddToQueue(host.RowKey, phoneId, data.Name ?? "");
                }

                return new AcceptedResult();

            default:
                var myQueueNumbers = await queueNumberRepository.ReadActiveQueueForPhone(phoneId);
                var response = new List<QueueStatus>();

                foreach (var queueNumber in myQueueNumbers)
                {
                    var activeQueue = (await queueNumberRepository.ReadActiveQueueForEvent(queueNumber.EventId))
                        .OrderBy(q => q.Timestamp)
                        .ToArray();

                    // Tickets live on the day's first session, so the rest of that day's sessions
                    // are what the wait still has to be served within.
                    var queueEvent = await eventRepository.FindById(queueNumber.EventId);
                    var queueDay = queueEvent == null
                        ? null
                        : ActivityDay.From(await eventRepository.ReadSessionsForEvent(queueEvent));

                    int position = Array.FindIndex(activeQueue, q => q.RowKey == queueNumber.RowKey) + 1;
                    int minutesPerPerson = queueEvent?.EstimateMinutesPerPerson(DefaultMinutesPerPerson) ?? DefaultMinutesPerPerson;
                    int rawWaitMinutes = (position - 1) * minutesPerPerson;
                    int estimatedWaitMinutes = queueDay?.EstimateWaitMinutes(rawWaitMinutes, nowLocal) ?? rawWaitMinutes;

                    response.Add(new QueueStatus
                    {
                        Id = queueNumber.RowKey,
                        Code = queueEvent?.QrCode ?? "",
                        ActivityName = queueEvent?.Title ?? "",
                        Position = position,
                        TotalInQueue = activeQueue.Length,
                        EstimatedWaitMinutes = estimatedWaitMinutes,
                    });
                }

                return new OkObjectResult(response);
        }
    }

    private class QueueJoinRequest
    {
        public string QrCode { get; set; } = "";
        public string Name { get; set; } = "";
    }

    private class QueueStatus
    {
        public string Id { get; set; } = "";
        public string Code { get; set; } = "";
        public string ActivityName { get; set; } = "";
        public int Position { get; set; }
        public int TotalInQueue { get; set; }
        public int EstimatedWaitMinutes { get; set; }
    }
}
