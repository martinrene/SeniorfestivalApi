using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Seniorfestival.Data.Models;
using Seniorfestival.Data.Repositories;

namespace Seniorfestival.Api;

public class Votings
{
    private readonly ILogger<Votings> _logger;
    private readonly IVotingRepository votingRepository;
    private readonly IVoteRepository voteRepository;

    public Votings(ILogger<Votings> logger, IVotingRepository votingRepository, IVoteRepository voteRepository)
    {
        _logger = logger;
        this.votingRepository = votingRepository;
        this.voteRepository = voteRepository;
    }

    [Function("Voting")]
    public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequest req)
    {
        switch (req.Method)
        {
            case "POST":
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                Vote? vote = JsonSerializer.Deserialize<Vote>(requestBody, new JsonSerializerOptions(JsonSerializerDefaults.Web));

                if (vote != null)
                {
                    await voteRepository.AddMyVote(vote);
                }
                return new AcceptedResult();


            default:
                var allVotings = await votingRepository.ReadActiveVotings();

                if (allVotings.Length == 0)
                {
                    return new OkObjectResult(new string[0]);
                }

                string? phoneId = req.Query["phoneId"];

                if (string.IsNullOrEmpty(phoneId))
                {
                    return new BadRequestResult();
                }
                var myVotes = await voteRepository.ReadAllMyVotes(phoneId);

                var result = (from v in allVotings
                              select new { Voting = v, Vote = myVotes.FirstOrDefault(vo => vo.VotingId == v.VotingId) }).ToArray();

                if (result.Length > 0)
                {
                    return new OkObjectResult(result);
                }
                return new OkObjectResult(new string[0]);
        }
    }
}