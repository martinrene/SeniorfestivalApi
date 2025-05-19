using Seniorfestival.Data.Models;

namespace Seniorfestival.Data.Repositories
{
    public interface ITextRepository
    {
        Task<Text[]> ReadAllTexts();
    }
}