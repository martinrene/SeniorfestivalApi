using Seniorfestival.Data.Models;

namespace Seniorfestival.Data.Repositories
{
    public interface IVotingRepository
    {
        Task<Voting[]> ReadActiveVotings();

        Task<Voting[]> ReadAllVotings();

        Task<Voting> FetchVoting(string votingId);

        Task<Voting?> FindVoting(string votingId);

        Task CreateVoting(Voting voting);

        Task SaveVoting(Voting voting);
    }
}
