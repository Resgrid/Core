using Npgsql;
using Resgrid.Model.Repositories.Connection;
using System.Data.Common;

namespace Resgrid.Repositories.DataRepository.Servers.PostgreSql
{
	public class PostgreSqlConnectionProvider : IConnectionProvider
	{
		public DbConnection Create()
		{
			return new NpgsqlConnection(Config.DataConfig.CoreConnectionString); //+ ";Client Encoding=windows-1252;Encoding=windows-1252");
		}
	}
}
