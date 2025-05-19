using Seniorfestival.Data.Models;
using Seniorfestival.Data.Persistence;

namespace Seniorfestival.Data.Repositories
{
    public class TextRepository : ITextRepository
    {
        private readonly ITableRepository<Text> repository;

        public TextRepository(ITableRepository<Text> repository)
        {
            this.repository = repository;
        }

        public async Task<Text[]> ReadAllTexts()
        {
            return (await repository.GetFromQueryAsync("")).ToArray();
        }
    }
}