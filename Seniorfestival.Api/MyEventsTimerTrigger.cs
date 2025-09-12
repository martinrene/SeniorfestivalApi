using System;
using System.Linq.Expressions;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Seniorfestival.Data.Repositories;

namespace Seniorfestival.System;

public class MyEventsTimerTrigger
{
    private readonly ILogger _logger;
    private readonly IEventRepository eventRepository;
    private readonly IMyEventRepository myEventRepository;
    private readonly string url = "https://api.onesignal.com/notifications";
    private readonly string key = "os_v2_app_m4rhcerxongu3fxtgvam7fzpi7lgya4fmbku5oemy3mppo42ajozjdbfj2l7676hx5gnl54xxbhbzwing7gynuvgqpd2p4zdne42s2i";

    public MyEventsTimerTrigger(ILoggerFactory loggerFactory, IEventRepository eventRepository, IMyEventRepository myEventRepository)
    {
        _logger = loggerFactory.CreateLogger<MyEventsTimerTrigger>();
        this.eventRepository = eventRepository;
        this.myEventRepository = myEventRepository;
    }

    [Function("MyEventsTimerTrigger")]
    public async Task Run([TimerTrigger("0 1,11,21,31,41,51 * * * *")] TimerInfo myTimer)
    {
        try
        {
            var events = await eventRepository.ReadAllEvents();
            var nowForDaySelect = DateTime.UtcNow.AddHours(-4);

            TimeZoneInfo tst = TimeZoneInfo.FindSystemTimeZoneById("Central Europe Standard Time");
            DateTime nowt = DateTime.UtcNow;

            DateTime.SpecifyKind(nowt, DateTimeKind.Utc);

            DateTime now = TimeZoneInfo.ConvertTime(nowt, TimeZoneInfo.Utc, tst);
            DateTime next = TimeZoneInfo.ConvertTime(nowt.AddMinutes(10), TimeZoneInfo.Utc, tst);

            var danishDay = "";
            var dayNo = 0;

            switch (nowForDaySelect.DayOfWeek)
            {
                case DayOfWeek.Friday:
                    danishDay = "fredag";
                    dayNo = 12;
                    break;

                case DayOfWeek.Saturday:
                    danishDay = "lordag";
                    dayNo = 13;
                    break;

                case DayOfWeek.Sunday:
                    danishDay = "sondag";
                    dayNo = 14;
                    break;

                default:
                    return;
            }

            var eventsNow = events.Where(e => e.Day == danishDay).Where(e =>
            {
                var splt = e.Start?.Split(":");
                var hour = int.Parse(splt[0]);
                var eventDate = new DateTime(now.Year, now.Month, hour < 4 ? dayNo + 1 : dayNo, hour, int.Parse(splt[1]), 0);
                return now < eventDate && eventDate < next;
            }).Select(e => e);

            foreach (var evt in eventsNow)
            {
                var myEventsForEvent = await myEventRepository.MyEventsForEventId(evt.RowKey);

                var oneSignalRequest = new OneSignalRequest();

                oneSignalRequest.headings = new Headings()
                {
                    en = $"{evt.Title} om få minutter"
                };

                oneSignalRequest.contents = new Contents()
                {
                    en = $"Skynd dig til {evt.Location} - {evt.Title} er klar kl. {evt.Start}. Vi zez"
                };

                oneSignalRequest.data = new Data()
                {
                    eventDay = danishDay,
                    eventId = evt.RowKey
                };

                oneSignalRequest.include_aliases = new IncludeAliases()
                {
                    external_id = myEventsForEvent.Select(e => e.PartitionKey).ToArray()
                };

                if (oneSignalRequest.include_aliases.external_id.Count() > 0)
                {

                    var json = Newtonsoft.Json.JsonConvert.SerializeObject(oneSignalRequest);
                    var data = new StringContent(json, Encoding.UTF8, "application/json");

                    using var client = new HttpClient();
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Key", key);

                    var response = await client.PostAsync(url, data);
                }

            }
        }
        catch (Exception ex)
        {
            {
                _logger.LogError(ex, ex.Message, []);
            }
        }
    }


    private class OneSignalRequest
    {
        public string target_channel { get; set; } = "push";
        public string app_id { get; set; } = "67227112-3773-4d4d-96f3-3540cf972f47";

        public IncludeAliases? include_aliases { get; set; }

        public Contents? contents { get; set; }

        public Headings? headings { get; set; }

        public Data? data { get; set; }

    }

    private class IncludeAliases
    {
        public string[] external_id { get; set; } = [];
    }

    private class Contents
    {
        public string en { get; set; } = "";
    }

    private class Headings
    {
        public string en { get; set; } = "";
    }

    private class Data
    {
        public string eventId { get; set; } = "";
        public string eventDay { get; set; } = "";
    }

}