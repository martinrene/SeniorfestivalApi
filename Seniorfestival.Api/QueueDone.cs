using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Seniorfestival.Data.Models;
using Seniorfestival.Data.Repositories;

namespace Seniorfestival.Api;

// Marks a queue ticket as served. Intended for staff use (e.g. calling the next number),
// not the mobile app - there is no phoneId involved.
public class QueueDone
{
    private readonly ILogger<QueueDone> _logger;
    private readonly IEventRepository eventRepository;
    private readonly IQueueNumberRepository queueNumberRepository;

    public QueueDone(ILogger<QueueDone> logger, IEventRepository eventRepository, IQueueNumberRepository queueNumberRepository)
    {
        _logger = logger;
        this.eventRepository = eventRepository;
        this.queueNumberRepository = queueNumberRepository;
    }

    [Function("QueueDone")]
    public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequest req)
    {
        _logger.LogInformation("C# HTTP trigger function processed a request.");

        string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
        QueueDoneRequest? data = JsonSerializer.Deserialize<QueueDoneRequest>(requestBody, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        if (data == null || string.IsNullOrEmpty(data.EventId) || string.IsNullOrEmpty(data.Number))
        {
            return new BadRequestResult();
        }

        QueueNumber? ticket = await queueNumberRepository.MarkDone(data.EventId, data.Number);
        if (ticket != null)
        {
            await eventRepository.RecordServiceCompletion(data.EventId);
        }

        return new AcceptedResult();
    }

    private class QueueDoneRequest
    {
        public string EventId { get; set; } = "";
        public string Number { get; set; } = "";
    }
}
