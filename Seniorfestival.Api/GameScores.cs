using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Seniorfestival.Data.Models;
using Seniorfestival.Data.Repositories;

namespace Seniorfestival.Api;

/// <summary>
/// Leaderboard for the food-throwing game: the day's best, the festival's best,
/// and where the calling device sits.
/// </summary>
public class GameScores
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>How many places each leaderboard shows.</summary>
    private const int TopCount = 2;

    /// <summary>Comfortably above anything reachable in a minute; rejects nonsense.</summary>
    private const int MaxCredibleScore = 500;

    private const int MaxNameLength = 30;

    private readonly ILogger<GameScores> _logger;
    private readonly IGameScoreRepository gameScoreRepository;

    public GameScores(ILogger<GameScores> logger, IGameScoreRepository gameScoreRepository)
    {
        _logger = logger;
        this.gameScoreRepository = gameScoreRepository;
    }

    [Function("GameScores")]
    public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequest req)
    {
        if (req.Method == "POST")
        {
            return await Save(req);
        }

        return new OkObjectResult(await BuildLeaderboard(req.Query["phoneId"]));
    }

    private async Task<IActionResult> Save(HttpRequest req)
    {
        ScoreRequest? request;

        try
        {
            string body = await new StreamReader(req.Body).ReadToEndAsync();
            request = JsonSerializer.Deserialize<ScoreRequest>(body, JsonOptions);
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult(new { error = "Kunne ikke læse resultatet." });
        }

        if (request == null || string.IsNullOrWhiteSpace(request.PhoneId))
        {
            return new BadRequestObjectResult(new { error = "phoneId mangler." });
        }

        if (request.Score <= 0 || request.Score > MaxCredibleScore)
        {
            return new BadRequestObjectResult(new { error = "Resultatet ser ikke rigtigt ud." });
        }

        string day = FestivalToday();
        string phoneId = request.PhoneId.Trim();
        string name = (request.Name ?? "").Trim();

        if (name.Length == 0)
        {
            name = "Anonym";
        }
        else if (name.Length > MaxNameLength)
        {
            name = name[..MaxNameLength];
        }

        var existing = await gameScoreRepository.FindScore(day, phoneId);

        // A device keeps its best of the day. The app only submits on a personal
        // best, but never let a later, worse round overwrite a good one.
        if (existing == null || request.Score > existing.Score)
        {
            await gameScoreRepository.SaveScore(new GameScore
            {
                Day = day,
                PhoneId = phoneId,
                Name = name,
                Score = request.Score
            });

            _logger.LogInformation("Saved game score {Score} for {Day}.", request.Score, day);
        }

        return new OkObjectResult(await BuildLeaderboard(phoneId));
    }

    private async Task<object> BuildLeaderboard(string? phoneId)
    {
        var all = await gameScoreRepository.ReadAllScores();
        string today = FestivalToday();

        var todayTop = all
            .Where(s => s.Day == today)
            .OrderByDescending(s => s.Score)
            // Earlier wins a tie, so a leader cannot be displaced by an equal score.
            .ThenBy(s => s.Timestamp ?? DateTimeOffset.MaxValue)
            .Take(TopCount)
            .Select(Entry)
            .ToArray();

        // One entry per device across the whole festival, keeping its best day.
        var festivalBests = all
            .GroupBy(s => s.PhoneId)
            .Select(g => g
                .OrderByDescending(s => s.Score)
                .ThenBy(s => s.Timestamp ?? DateTimeOffset.MaxValue)
                .First())
            .ToArray();

        var festivalTop = festivalBests
            .OrderByDescending(s => s.Score)
            .ThenBy(s => s.Timestamp ?? DateTimeOffset.MaxValue)
            .Take(TopCount)
            .Select(Entry)
            .ToArray();

        object? me = null;

        if (!string.IsNullOrWhiteSpace(phoneId))
        {
            var mine = festivalBests.FirstOrDefault(s => s.PhoneId == phoneId.Trim());

            if (mine != null)
            {
                me = new
                {
                    name = mine.Name,
                    score = mine.Score,
                    // Ties share a position, so two equal bests are both 1st.
                    position = festivalBests.Count(s => s.Score > mine.Score) + 1,
                    total = festivalBests.Length
                };
            }
        }

        return new { today = todayTop, festival = festivalTop, me };
    }

    private static object Entry(GameScore score) => new { name = score.Name, score = score.Score };

    /// <summary>
    /// Today in festival-local time, so a round played at half past midnight counts
    /// against the right day. Matches the time zone <see cref="Queues"/> uses.
    /// </summary>
    private static string FestivalToday()
    {
        TimeZoneInfo festivalTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Central Europe Standard Time");

        return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, festivalTimeZone).ToString("yyyy-MM-dd");
    }

    private class ScoreRequest
    {
        public string? PhoneId { get; set; }
        public string? Name { get; set; }
        public int Score { get; set; }
    }
}
