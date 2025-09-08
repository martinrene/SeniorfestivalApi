using Seniorfestival.Data.Models;
using Seniorfestival.Data.Persistence;

namespace Seniorfestival.Data.Repositories
{
    public class VoteRepository : IVoteRepository
    {
        private readonly ITableRepository<Vote> repository;

        public VoteRepository(ITableRepository<Vote> repository)
        {
            this.repository = repository;
        }

        public async Task AddMyVote(Vote vote)
        {
            await repository.AddAsync(vote);
        }

        public async Task<Vote[]> ReadAllMyVotes(string phoneId)
        {
            return (await repository.GetFromQueryAsync($"RowKey eq '{phoneId}'")).ToArray();
        }
    }
}