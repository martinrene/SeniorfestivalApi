using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Seniorfestival.Data.Models;
using Seniorfestival.Data.Repositories;

namespace Seniorfestival.Api;

/// <summary>
/// Backend for the admin site: list, create and edit votings, and read live results.
/// The public <see cref="Votings"/> endpoint stays read-only for the mobile app.
/// Named VotingsAdmin rather than AdminVotings because the Functions host reserves
/// every route starting with "admin" and refuses to load the function.
/// </summary>
public class VotingsAdmin
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // RowKey characters Table Storage rejects, plus the separator used inside Choices.
    private static readonly char[] IllegalIdCharacters = ['/', '\\', '#', '?', ';'];

    private readonly ILogger<VotingsAdmin> _logger;
    private readonly IVotingRepository votingRepository;
    private readonly IVoteRepository voteRepository;

    public VotingsAdmin(ILogger<VotingsAdmin> logger, IVotingRepository votingRepository, IVoteRepository voteRepository)
    {
        _logger = logger;
        this.votingRepository = votingRepository;
        this.voteRepository = voteRepository;
    }

    [Function("VotingsAdmin")]
    public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Function, "get", "post", "put")] HttpRequest req)
    {
        string? votingId = req.Query["votingId"];

        switch (req.Method)
        {
            case "POST":
                return await Create(req);

            case "PUT":
                if (string.IsNullOrEmpty(votingId))
                {
                    return Error("votingId mangler.");
                }
                return await Update(votingId, req);

            default:
                if (!string.IsNullOrEmpty(votingId))
                {
                    return await Results(votingId);
                }
                return await List();
        }
    }

    private async Task<IActionResult> List()
    {
        var votings = await votingRepository.ReadAllVotings();

        return new OkObjectResult(votings
            .OrderByDescending(v => v.Timestamp ?? DateTimeOffset.MinValue)
            .Select(ToDto));
    }

    /// <summary>
    /// Current tally for one voting. Choices nobody has picked are returned with a count of
    /// zero so the chart keeps every option visible from the moment the voting opens.
    /// </summary>
    private async Task<IActionResult> Results(string votingId)
    {
        var voting = await votingRepository.FindVoting(votingId);

        if (voting == null)
        {
            return new NotFoundResult();
        }

        var votes = await voteRepository.ReadAllVotesForVoting(votingId);
        var tally = votes.GroupBy(v => v.Choice).ToDictionary(g => g.Key, g => g.Count());
        var choices = SplitChoices(voting.Choices);

        var counts = choices
            .Select(choice => new { answer = choice, count = tally.GetValueOrDefault(choice, 0) })
            // Answers from a choice list that has since been edited would otherwise vanish.
            .Concat(tally
                .Where(entry => !choices.Contains(entry.Key))
                .Select(entry => new { answer = entry.Key, count = entry.Value }))
            .ToArray();

        return new OkObjectResult(new
        {
            votingId = voting.VotingId,
            description = voting.Description,
            choices,
            active = voting.Active,
            totalVotes = votes.Length,
            counts
        });
    }

    private async Task<IActionResult> Create(HttpRequest req)
    {
        var request = await ReadRequest(req);

        if (request == null)
        {
            return Error("Kunne ikke læse afstemningen.");
        }

        string description = (request.Description ?? "").Trim();
        string choices = JoinChoices(request.Choices);

        string? invalid = Validate(description, choices);
        if (invalid != null)
        {
            return Error(invalid);
        }

        string votingId = (request.VotingId ?? "").Trim();
        if (votingId.Length == 0)
        {
            votingId = Guid.NewGuid().ToString("N")[..8];
        }
        else if (votingId.IndexOfAny(IllegalIdCharacters) >= 0)
        {
            return Error("Id må ikke indeholde / \\ # ? eller ;");
        }
        else if (await votingRepository.FindVoting(votingId) != null)
        {
            return Error($"Der findes allerede en afstemning med id '{votingId}'.");
        }

        var voting = new Voting
        {
            VotingId = votingId,
            Description = description,
            Choices = choices,
            Active = request.Active
        };

        await votingRepository.CreateVoting(voting);
        _logger.LogInformation("Created voting {VotingId}", votingId);

        return new OkObjectResult(ToDto(voting));
    }

    private async Task<IActionResult> Update(string votingId, HttpRequest req)
    {
        var voting = await votingRepository.FindVoting(votingId);

        if (voting == null)
        {
            return new NotFoundResult();
        }

        var request = await ReadRequest(req);

        if (request == null)
        {
            return Error("Kunne ikke læse afstemningen.");
        }

        string description = (request.Description ?? "").Trim();
        string choices = JoinChoices(request.Choices);

        // Votes already cast are counted against the choice texts, so the wording is frozen
        // while a voting is open. Only the Active toggle stays live.
        if (voting.Active && (description != voting.Description || choices != voting.Choices))
        {
            return new ConflictObjectResult(new { error = "En aktiv afstemning kan ikke ændres. Deaktivér den først." });
        }

        string? invalid = Validate(description, choices);
        if (invalid != null)
        {
            return Error(invalid);
        }

        voting.Description = description;
        voting.Choices = choices;
        voting.Active = request.Active;

        await votingRepository.SaveVoting(voting);
        _logger.LogInformation("Updated voting {VotingId}, active {Active}", votingId, voting.Active);

        return new OkObjectResult(ToDto(voting));
    }

    private static async Task<VotingRequest?> ReadRequest(HttpRequest req)
    {
        try
        {
            string body = await new StreamReader(req.Body).ReadToEndAsync();
            return JsonSerializer.Deserialize<VotingRequest>(body, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Validate(string description, string choices)
    {
        if (description.Length == 0)
        {
            return "Skriv en kort beskrivelse af afstemningen.";
        }

        var parts = SplitChoices(choices);

        if (parts.Length < 2)
        {
            return "Der skal være mindst to svarmuligheder.";
        }

        if (parts.Distinct(StringComparer.OrdinalIgnoreCase).Count() != parts.Length)
        {
            return "Svarmulighederne skal være forskellige.";
        }

        return null;
    }

    private static object ToDto(Voting voting) => new
    {
        votingId = voting.VotingId,
        description = voting.Description,
        choices = SplitChoices(voting.Choices),
        active = voting.Active,
        updated = voting.Timestamp
    };

    private static string[] SplitChoices(string choices) =>
        choices.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string JoinChoices(string[]? choices) =>
        string.Join(";", (choices ?? []).Select(c => (c ?? "").Trim()).Where(c => c.Length > 0));

    private static IActionResult Error(string message) => new BadRequestObjectResult(new { error = message });

    private class VotingRequest
    {
        public string? VotingId { get; set; }
        public string? Description { get; set; }
        public string[]? Choices { get; set; }
        public bool Active { get; set; }
    }
}
