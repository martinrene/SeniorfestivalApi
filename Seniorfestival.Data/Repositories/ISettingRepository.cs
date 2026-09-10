using Seniorfestival.Data.Models;

namespace Seniorfestival.Data.Repositories
{
    public interface ISettingRepository
    {
        Task<Setting[]> ReadAllSettings();
        Task<Setting?> FindSetting(string name);
        Task SaveSetting(Setting setting);
    }
}