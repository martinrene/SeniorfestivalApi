using Seniorfestival.Data.Models;
using Seniorfestival.Data.Persistence;

namespace Seniorfestival.Data.Repositories
{
    public class SettingRepository : ISettingRepository
    {
        private readonly ITableRepository<Setting> repository;

        public SettingRepository(ITableRepository<Setting> repository)
        {
            this.repository = repository;
        }

        public async Task<Setting[]> ReadAllSettings()
        {
            return (await repository.GetFromQueryAsync("")).ToArray();
        }
    }
}