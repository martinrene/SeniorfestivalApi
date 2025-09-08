using Seniorfestival.Data.Models;

namespace Seniorfestival.Data.Repositories
{
    public interface IVoteRepository
    {
        public Task AddMyVote(Vote vote);
        Task<Vote[]> ReadAllMyVotes(string phoneId);
    }
}