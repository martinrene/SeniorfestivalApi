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
    private readonly ISettingRepository settingRepository;
    private readonly ITextRepository textRepository;

    public Data(ILogger<Data> logger, IEventRepository eventRepository, ISettingRepository settingRepository, ITextRepository textRepository)
    {
        _logger = logger;
        this.eventRepository = eventRepository;
        this.settingRepository = settingRepository;
        this.textRepository = textRepository;
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
                ScheduleEvents = events.Where(e => e.PartitionKey == "Program").ToArray(),
                ActivityEvents = events.Where(e => e.PartitionKey == "Aktivitet").ToArray(),
                Settings = await settingRepository.ReadAllSettings(),
                Texts = await textRepository.ReadAllTexts()

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
        public Event[] ScheduleEvents { get; set; } = [];
        public Event[] ActivityEvents { get; set; } = [];
        public Setting[] Settings { get; set; } = [];
        public Text[] Texts { get; set; } = [];

    }
}