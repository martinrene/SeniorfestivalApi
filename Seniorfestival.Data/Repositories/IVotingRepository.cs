using Seniorfestival.Data.Models;

namespace Seniorfestival.Data.Repositories
{
    public interface IVotingRepository
    {
        Task<Voting[]> ReadActiveVotings();

        Task<Voting> FetchVoting(string votingId);
    }
}