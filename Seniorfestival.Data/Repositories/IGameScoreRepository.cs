using Seniorfestival.Data.Models;

namespace Seniorfestival.Data.Repositories
{
    public interface IGameScoreRepository
    {
        Task<GameScore[]> ReadAllScores();

        Task<GameScore?> FindScore(string day, string phoneId);

        Task SaveScore(GameScore score);
    }
}
