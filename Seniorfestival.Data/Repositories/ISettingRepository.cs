using Seniorfestival.Data.Models;

namespace Seniorfestival.Data.Repositories
{
    public interface ISettingRepository
    {
        Task<Setting[]> ReadAllSettings();
    }
}