

using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using Seniorfestival.Data.Models;
using Seniorfestival.Data.Persistence;

namespace Seniorfestival.Data.Repositories
{
    public class VotingRespository : IVotingRepository
    {
        private readonly ITableRepository<Voting> repository;

        public VotingRespository(ITableRepository<Voting> tableRepository)
        {
            this.repository = tableRepository;
        }

        public async Task<Voting[]> ReadActiveVotings()
        {
            return (await repository.GetFromQueryAsync("Active eq true")).ToArray();
        }

        public async Task<Voting> FetchVoting(string votingId)
        {
            return (await repository.GetAsync("Voting", votingId));
        }
    }
}