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

        public async Task<Setting?> FindSetting(string name)
        {
            // On Name rather than RowKey: the rows the app reads are keyed by name today,
            // but nothing has ever enforced it, so the property is the reliable one.
            var matches = await repository.GetFromQueryAsync($"Name eq '{name}'");

            return matches.FirstOrDefault();
        }

        public async Task SaveSetting(Setting setting)
        {
            await repository.UpsertAsync(setting);
        }
    }
}