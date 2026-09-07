using Azure;
using Seniorfestival.Data.Models;
using Seniorfestival.Data.Persistence;

namespace Seniorfestival.Data.Repositories
{
    public class GameScoreRepository : IGameScoreRepository
    {
        private readonly ITableRepository<GameScore> repository;

        public GameScoreRepository(ITableRepository<GameScore> repository)
        {
            this.repository = repository;
        }

        /// <summary>
        /// The whole table, which is one festival's worth of scores and is emptied
        /// between festivals. Both leaderboards are built from this in memory rather
        /// than with two queries, since the day's rows are a subset of it.
        /// </summary>
        public async Task<GameScore[]> ReadAllScores()
        {
            return (await repository.GetFromQueryAsync("")).ToArray();
        }

        public async Task<GameScore?> FindScore(string day, string phoneId)
        {
            try
            {
                return await repository.GetAsync(day, phoneId);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return null;
            }
        }

        public async Task SaveScore(GameScore score)
        {
            await repository.UpsertAsync(score);
        }
    }
}
