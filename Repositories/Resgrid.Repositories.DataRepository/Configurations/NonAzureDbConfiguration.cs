using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using Resgrid.Repositories.DataRepository.Contexts;
using Resgrid.Repositories.DataRepository.Migrations;

namespace Resgrid.Repositories.DataRepository.Configurations
{
    public class NonAzureDbConfiguration : DbConfiguration
    {
        public NonAzureDbConfiguration()
        {
            SetDefaultConnectionFactory(new SqlConnectionFactory());
            SetDatabaseInitializer(new MigrateDatabaseToLatestVersion<DataContext, Configuration>());
        }
    } 
}