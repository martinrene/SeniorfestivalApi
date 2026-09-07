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

    private const string FrontpageTextSetting = "frontpageText";
    private const string UpdateTextSetting = "updateAppText";
    private const string LegacyUpdateTextSetting = "updateAppTextNoStore";

    /// <summary>Optional marker in updateAppText saying where to put the store link.</summary>
    private const string StoreUrlPlaceholder = "{storeUrl}";

    /// <summary>Used only if updateAppText has not been added to the Settings table.</summary>
    private const string DefaultUpdateText =
        "<h2>Opdatér appen</h2><p>Der er kommet en ny version af appen. "
        + "Hent den for at få det nyeste program og de nyeste funktioner.</p>";

    /// <summary>Used when the platform is unknown, so there is no store to link to.</summary>
    private const string DefaultLegacyUpdateText =
        "<h2>Opdatér appen</h2><p>Der er kommet en ny version af appen. "
        + "Hent den i App Store eller Google Play for at få det nyeste program "
        + "og de nyeste funktioner.</p>";

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

        //if (cacheValue == null || cacheExpire == null || new DateTime() > cacheExpire)
        //{
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
            cacheExpire = new DateTime().AddMinutes(2);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.Message);
            return new BadRequestResult();
        }
        //}
        // Snapshot the field: another request can replace it at any point, and the
        // update prompt must not be written into an object other requests share.
        DataObject? snapshot = cacheValue;

        if (snapshot == null)
        {
            return new BadRequestResult();
        }

        return new OkObjectResult(new DataObject
        {
            ScheduleEvents = snapshot.ScheduleEvents,
            ActivityEvents = snapshot.ActivityEvents,
            Shops = snapshot.Shops,
            Settings = ApplyUpdatePrompt(snapshot.Settings, req.Query["platform"], req.Query["build"]),
            Texts = snapshot.Texts
        });
    }

    /// <summary>
    /// Swaps frontpageText for an update prompt when the caller is not running the
    /// build that Settings says is current for its platform.
    /// <para>
    /// Every path that cannot answer the question confidently returns the settings
    /// untouched: a caller that did not identify itself, a platform with no version
    /// configured, or a blank setting. A missing or mistyped setting therefore
    /// cannot lock the whole user base out of the front page.
    /// </para>
    /// </summary>
    private Setting[] ApplyUpdatePrompt(Setting[] settings, string? platform, string? build)
    {
        bool hasPlatform = !string.IsNullOrWhiteSpace(platform);
        bool hasBuild = !string.IsNullOrWhiteSpace(build);

        // Apps released before these parameters existed send neither, which makes
        // them old builds by definition -- and the ones most in need of the notice.
        // Without a platform there is no right store to point them at, so they get
        // a link-free message. Gated on its own switch because it fires for every
        // such caller at once: turning it on is what says "the new build is live in
        // both stores", which the version numbers alone cannot tell us.
        if (!hasPlatform && !hasBuild)
        {
            if (!IsLegacyNoticeEnabled())
            {
                return settings;
            }

            _logger.LogInformation("Serving link-free update notice to an app that sent no platform or build.");

            string legacy = SettingValue(settings, LegacyUpdateTextSetting);

            return WithFrontpageText(
                settings,
                string.IsNullOrWhiteSpace(legacy) ? DefaultLegacyUpdateText : legacy);
        }

        // Knowing the platform but not the build says nothing about whether the
        // caller is current, so leave it alone rather than guess.
        if (!hasPlatform || !hasBuild)
        {
            return settings;
        }

        bool isIos = platform!.Equals("ios", StringComparison.OrdinalIgnoreCase);
        bool isAndroid = platform.Equals("android", StringComparison.OrdinalIgnoreCase);

        if (!isIos && !isAndroid)
        {
            return settings;
        }

        // App settings rather than the Settings table: which build is current is
        // deployment configuration, not app content, and the Settings table is
        // handed to every client, so there is no reason to broadcast it.
        string? currentVersion = Environment.GetEnvironmentVariable(
            isIos ? "currentIosVersion" : "currentAndroidVersion");

        if (string.IsNullOrWhiteSpace(currentVersion) || currentVersion.Trim() == build!.Trim())
        {
            return settings;
        }

        _logger.LogInformation(
            "Serving update prompt: {Platform} build {Build} is not the current {Current}.",
            platform, build, currentVersion);

        string storeUrl = (Environment.GetEnvironmentVariable(
            isIos ? "appStoreUrl" : "playStoreUrl") ?? "").Trim();

        // The message stays in the Settings table: it is Danish copy shown to
        // users, and editing it there takes effect without restarting the API.
        string message = SettingValue(settings, UpdateTextSetting);

        if (string.IsNullOrWhiteSpace(message))
        {
            message = DefaultUpdateText;
        }

        if (message.Contains(StoreUrlPlaceholder))
        {
            // An empty substitution would leave a dead href, so fall back to the
            // link-free wording instead of emitting one.
            message = storeUrl.Length > 0
                ? message.Replace(StoreUrlPlaceholder, storeUrl)
                : DefaultLegacyUpdateText;
        }
        else if (storeUrl.Length > 0)
        {
            message += $"<p><a href=\"{storeUrl}\">Hent opdateringen</a></p>";
        }

        return WithFrontpageText(settings, message);
    }

    private static bool IsLegacyNoticeEnabled()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable("legacyUpdateNotice")?.Trim(),
            "true",
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns the settings with frontpageText carrying <paramref name="message"/>.
    /// Replaces rather than mutates: these Setting instances belong to the cached
    /// object and are handed to every other request at the same time.
    /// </summary>
    private static Setting[] WithFrontpageText(Setting[] settings, string message)
    {
        bool replaced = false;

        var updated = settings.Select(setting =>
        {
            if (!string.Equals(setting.Name, FrontpageTextSetting, StringComparison.OrdinalIgnoreCase))
            {
                return setting;
            }

            replaced = true;
            return new Setting
            {
                PartitionKey = setting.PartitionKey,
                RowKey = setting.RowKey,
                Name = setting.Name,
                Value = message
            };
        }).ToList();

        // The app renders nothing when frontpageText is absent, so add it rather
        // than let the notice go unseen.
        if (!replaced)
        {
            updated.Add(new Setting
            {
                PartitionKey = "Setting",
                RowKey = FrontpageTextSetting,
                Name = FrontpageTextSetting,
                Value = message
            });
        }

        return updated.ToArray();
    }

    private static string SettingValue(Setting[] settings, string name)
    {
        return settings
            .FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))
            ?.Value ?? "";
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