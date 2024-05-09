using Resgrid.Model.Repositories.Connection;
using System.Configuration;
using System.Data.Common;
using System.Data.SqlClient;
using System.Linq;

namespace Resgrid.Repositories.DataRepository.Servers.SqlServer
{
	public class SqlServerConnectionProvider : IConnectionProvider
	{
		//public string connectionString =
		//	ConfigurationManager.ConnectionStrings.Cast<ConnectionStringSettings>()
		//		.FirstOrDefault(x => x.Name == "ResgridContext")
		//		.ConnectionString;

		public DbConnection Create()
		{
			return new SqlConnection(Config.DataConfig.ConnectionString);
		}
	}
}
