using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using Resgrid.Repositories.DataRepository.Strategies;

namespace Resgrid.Repositories.DataRepository.Configurations
{
	public class WebDbConfiguration : DbConfiguration
	{
		public WebDbConfiguration()
		{
			// SetExecutionStrategy("System.Data.SqlClient", () => new SqlAzureExecutionStrategy());
			//SetExecutionStrategy("System.Data.SqlClient", () => new CustomSqlAzureExecutionStrategy());
			SetDefaultConnectionFactory(new SqlConnectionFactory());
			//SetDatabaseInitializer(new MigrateDatabaseToLatestVersion<DataContext, Configuration>());
		}
	}
}