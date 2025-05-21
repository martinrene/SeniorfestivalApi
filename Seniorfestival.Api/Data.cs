using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Seniorfestival.Data.Models;
using Seniorfestival.Data.Repositories;

namespace Seniorfestival.Api;

public class Data
{
    private readonly ILogger<Data> _logger;
    private readonly IEventRepository eventRepository;

    public Data(ILogger<Data> logger, IEventRepository eventRepository)
    {
        _logger = logger;
        this.eventRepository = eventRepository;
    }

    [Function("Data")]
    public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequest req)
    {
        _logger.LogInformation("C# HTTP trigger function processed a request.");
        try
        {
            var events = await eventRepository.ReadAllEvents();

            DataObject data = new DataObject()
            {
                Programs = events.Where(e => e.PartitionKey == "Program").ToArray(),
                Activities = events.Where(e => e.PartitionKey == "Aktivitet").ToArray()
            };

            return new OkObjectResult(data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.Message);
            return new BadRequestResult();
        }
    }

    private class DataObject
    {
        public Evento[] Programs { get; set; } = [];
        public Evento[] Activities { get; set; } = [];

    }
}