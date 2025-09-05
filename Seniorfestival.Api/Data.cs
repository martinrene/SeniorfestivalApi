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
    private readonly IShopRepository shopRepository;
    private readonly ISettingRepository settingRepository;
    private readonly ITextRepository textRepository;

    private static DataObject? cacheValue;
    private static DateTime? cacheExpire;

    public Data(ILogger<Data> logger, IEventRepository eventRepository, IShopRepository shopRepository, ISettingRepository settingRepository, ITextRepository textRepository)
    {
        _logger = logger;
        this.eventRepository = eventRepository;
        this.shopRepository = shopRepository;
        this.settingRepository = settingRepository;
        this.textRepository = textRepository;
    }

    [Function("Data")]
    public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequest req)
    {
        _logger.LogInformation("C# HTTP trigger function processed a request.");

        if (!string.IsNullOrEmpty(req.Query["clearcache"]))
        {
            cacheValue = null;
            cacheExpire = null;
        }
        ;

        if (cacheValue == null || cacheExpire == null || new DateTime() > cacheExpire)
        {
            try
            {
                var eventsReq = eventRepository.ReadAllEvents();
                var shopsReq = shopRepository.ReadAllShops();
                var settingsReq = settingRepository.ReadAllSettings();
                var textReq = textRepository.ReadAllTexts();

                await Task.WhenAll(eventsReq, shopsReq, settingsReq, textReq);

                var schedules = eventsReq.Result.Where(e => e.PartitionKey == "Program").OrderBy(e =>
                {
                    if (String.IsNullOrEmpty(e.Start))
                    {
                        return DateTime.MinValue;
                    }

                    var splt = e.Start.Split(":");
                    return new DateTime(2025, 1, 1, Int32.Parse(splt[0]), Int32.Parse(splt[1]), 0);
                });

                var avtivities = eventsReq.Result.Where(e => e.PartitionKey == "Aktivitet").OrderBy(e =>
                {
                    if (String.IsNullOrEmpty(e.Start))
                    {
                        return DateTime.MinValue;
                    }

                    var splt = e.Start.Split(":");
                    return new DateTime(2025, 1, 1, Int32.Parse(splt[0]), Int32.Parse(splt[1]), 0);
                });



                DataObject data = new DataObject()
                {
                    ScheduleEvents = schedules.ToArray(),
                    ActivityEvents = avtivities.ToArray(),
                    Shops = shopsReq.Result,
                    Settings = settingsReq.Result,
                    Texts = textReq.Result
                };

                cacheValue = data;
                cacheExpire = new DateTime().AddMinutes(10);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
                return new BadRequestResult();
            }
        }
        return new OkObjectResult(cacheValue);
    }

    private class DataObject
    {
        public Event[] ScheduleEvents { get; set; } = [];
        public Event[] ActivityEvents { get; set; } = [];
        public Shop[] Shops { get; set; } = [];
        public Setting[] Settings { get; set; } = [];
        public Text[] Texts { get; set; } = [];

    }
}