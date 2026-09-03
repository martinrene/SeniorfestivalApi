

using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using Azure;
using Seniorfestival.Data.Models;
using Seniorfestival.Data.Persistence;

namespace Seniorfestival.Data.Repositories
{
    public class VotingRespository : IVotingRepository
    {
        // Every voting lives in a single partition, so the table can be listed as one page.
        public const string PartitionName = "Voting";

        private readonly ITableRepository<Voting> repository;

        public VotingRespository(ITableRepository<Voting> tableRepository)
        {
            this.repository = tableRepository;
        }

        public async Task<Voting[]> ReadActiveVotings()
        {
            return (await repository.GetFromQueryAsync("Active eq true")).ToArray();
        }

        public async Task<Voting[]> ReadAllVotings()
        {
            return (await repository.GetFromQueryAsync($"PartitionKey eq '{PartitionName}'")).ToArray();
        }

        public async Task<Voting> FetchVoting(string votingId)
        {
            return (await repository.GetAsync(PartitionName, votingId));
        }

        public async Task<Voting?> FindVoting(string votingId)
        {
            try
            {
                return await repository.GetAsync(PartitionName, votingId);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return null;
            }
        }

        public async Task CreateVoting(Voting voting)
        {
            voting.PartitionKey = PartitionName;
            await repository.AddAsync(voting);
        }

        public async Task SaveVoting(Voting voting)
        {
            voting.PartitionKey = PartitionName;
            await repository.UpsertAsync(voting);
        }
    }
}
