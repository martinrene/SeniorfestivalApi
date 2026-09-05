using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Seniorfestival.Data.Models;
using Seniorfestival.Data.Repositories;

namespace Seniorfestival.Api;

/// <summary>
/// Replaces the Guests table with the participants from the kreds feed. Every group's
/// "deltagere" become one row each, tagged with the group name.
/// </summary>
public class GuestsFeedSync
{
    // 1552 participants is a lot of single-entity round trips; without this the sync
    // runs for minutes and risks the 230s HTTP timeout.
    private const int MaxParallelWrites = 16;

    private static readonly HttpClient httpClient = new() { Timeout = TimeSpan.FromSeconds(60) };

    private static readonly JsonSerializerOptions FeedJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    private readonly ILogger<GuestsFeedSync> _logger;
    private readonly IGuestRepository guestRepository;

    public GuestsFeedSync(ILogger<GuestsFeedSync> logger, IGuestRepository guestRepository)
    {
        _logger = logger;
        this.guestRepository = guestRepository;
    }

    [Function("GuestsFeedSync")]
    public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequest req)
    {
        string? feedUrl = Environment.GetEnvironmentVariable("kredsFeedUrl");
        if (string.IsNullOrEmpty(feedUrl))
        {
            return new BadRequestObjectResult("App setting 'kredsFeedUrl' is not configured.");
        }

        bool dryRun = !string.IsNullOrEmpty(req.Query["dryRun"]);

        try
        {
            KredsFeed? feed;
            using (var response = await httpClient.GetAsync(feedUrl))
            {
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("Kreds feed returned {StatusCode}", (int)response.StatusCode);
                    return new ObjectResult($"Kreds feed returned {(int)response.StatusCode}.") { StatusCode = 502 };
                }

                feed = JsonSerializer.Deserialize<KredsFeed>(
                    await response.Content.ReadAsStringAsync(), FeedJsonOptions);
            }

            var guests = BuildGuests(feed);

            if (guests.Count == 0)
            {
                _logger.LogWarning("Kreds feed produced no usable participants. Skipping sync to avoid deleting existing data.");
                return new ObjectResult(new
                {
                    inserted = 0,
                    deleted = 0,
                    warning = "Feed contained no usable participants; existing table rows were left untouched."
                })
                { StatusCode = 422 };
            }

            if (feed!.TotalParticipants > 0 && feed.TotalParticipants != guests.Count)
            {
                _logger.LogWarning(
                    "Kreds feed declares {Declared} participants but {Actual} usable rows were built.",
                    feed.TotalParticipants, guests.Count);
            }

            // Snapshot before writing. New rows get fresh RowKeys and so never collide
            // with the old ones, which means adding before removing keeps the guest list
            // continuously populated instead of briefly empty.
            var existing = await guestRepository.ReadAllGuests();

            if (dryRun)
            {
                return new OkObjectResult(Summary(feed, guests, existing.Length, dryRun: true));
            }

            var options = new ParallelOptions { MaxDegreeOfParallelism = MaxParallelWrites };

            await Parallel.ForEachAsync(guests, options,
                async (guest, _) => await guestRepository.UpsertGuest(guest));

            await Parallel.ForEachAsync(existing, options,
                async (guest, _) => await guestRepository.DeleteGuest(guest));

            _logger.LogInformation("Synced {Inserted} guests, removed {Deleted} previous rows.",
                guests.Count, existing.Length);

            return new OkObjectResult(Summary(feed, guests, existing.Length, dryRun: false));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync guests from the kreds feed");
            return new ObjectResult(ex.Message) { StatusCode = 500 };
        }
    }

    private static object Summary(KredsFeed feed, List<Guest> guests, int deleted, bool dryRun) => new
    {
        dryRun,
        feedLastUpdated = feed.LastUpdated,
        groups = guests.Select(g => g.Group).Distinct().Count(),
        inserted = guests.Count,
        deleted
    };

    private static List<Guest> BuildGuests(KredsFeed? feed)
    {
        var guests = new List<Guest>();

        foreach (var group in feed?.Groups ?? [])
        {
            string kreds = (group.Kredsnavn ?? "").Trim();

            if (kreds.Length == 0)
            {
                continue;
            }

            foreach (var participant in group.Deltagere ?? [])
            {
                string name = (participant ?? "").Trim();

                if (name.Length == 0)
                {
                    continue;
                }

                // The same first name can appear twice in one group; every participant
                // gets their own row, so the key is just a fresh id.
                guests.Add(new Guest
                {
                    PartitionKey = SanitizeKey(kreds),
                    RowKey = Guid.NewGuid().ToString("N"),
                    Name = name,
                    Group = kreds
                });
            }
        }

        return guests;
    }

    /// <summary>Table Storage rejects these characters in a key; the feed is external, so guard for them.</summary>
    private static string SanitizeKey(string value)
    {
        return new string(value
            .Select(c => c is '/' or '\\' or '#' or '?' || char.IsControl(c) ? '-' : c)
            .ToArray());
    }

    private class KredsFeed
    {
        public DateTimeOffset? LastUpdated { get; set; }
        public int TotalParticipants { get; set; }
        public int TotalKredse { get; set; }
        public KredsGroup[] Groups { get; set; } = [];
    }

    private class KredsGroup
    {
        public string? Kredsnavn { get; set; }
        public string?[]? Deltagere { get; set; }
    }
}
