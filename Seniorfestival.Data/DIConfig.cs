using Azure.Data.Tables;
using Seniorfestival.Data.Persistence;
using Seniorfestival.Data.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace Seniorfestival.Data
{
    public static class DIConfig
    {
        public static IServiceCollection AddData(this IServiceCollection services)
        {
            var tabelClientConnectionString = Environment.GetEnvironmentVariable("tableStorageConnectionString");

            services.AddSingleton(_ => new TableServiceClient(tabelClientConnectionString));
            services.AddSingleton(typeof(ITableRepository<>), typeof(TableRepository<>));
            services.AddSingleton(typeof(IEventRepository), typeof(EventRepository));
            services.AddSingleton(typeof(IShopRepository), typeof(ShopRepository));
            services.AddSingleton(typeof(ITextRepository), typeof(TextRepository));

            return services;
        }
    }
}
