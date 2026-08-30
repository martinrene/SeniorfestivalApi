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
        TimeOnly nowLocal = TimeOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, festivalTimeZone));

        switch (req.Method)
        {
            case "POST":
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                QueueJoinRequest? data = JsonSerializer.Deserialize<QueueJoinRequest>(requestBody, new JsonSerializerOptions(JsonSerializerDefaults.Web));

                if (data == null || string.IsNullOrEmpty(data.QrCode))
                {
                    return new BadRequestResult();
                }

                Event? evt = await eventRepository.FindByQrCode(data.QrCode);
                if (evt == null)
                {
                    return new NotFoundResult();
                }

                QueueNumber? existing = await queueNumberRepository.FindActiveQueueNumber(evt.RowKey, phoneId);
                if (existing == null)
                {
                    var activeQueueForEvent = await queueNumberRepository.ReadActiveQueueForEvent(evt.RowKey);
                    int minutesPerPersonForJoin = evt.EstimateMinutesPerPerson(DefaultMinutesPerPerson);
                    int rawWaitMinutesForJoin = activeQueueForEvent.Length * minutesPerPersonForJoin;

                    if (evt.WouldExceedOpeningHours(rawWaitMinutesForJoin, nowLocal))
                    {
                        return new ConflictObjectResult(new { message = "Der er ikke flere ledige pladser i køen i dag" });
                    }

                    await queueNumberRepository.AddToQueue(evt.RowKey, phoneId, data.Name ?? "");
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

                    var queueEvent = await eventRepository.FindById(queueNumber.EventId);
                    int position = Array.FindIndex(activeQueue, q => q.RowKey == queueNumber.RowKey) + 1;
                    int minutesPerPerson = queueEvent?.EstimateMinutesPerPerson(DefaultMinutesPerPerson) ?? DefaultMinutesPerPerson;
                    int rawWaitMinutes = (position - 1) * minutesPerPerson;
                    int estimatedWaitMinutes = queueEvent?.EstimateWaitMinutes(rawWaitMinutes, nowLocal) ?? rawWaitMinutes;

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
