using System;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Model.Repositories;

namespace Resgrid.Repositories.DataRepository
{
	public class HealthRepository : IHealthRepository
	{
		public async Task<string> GetDatabaseCurrentTime()
		{
			var query = $@"SELECT GETDATE()";

			try
			{
				using (IDbConnection db = new SqlConnection(Config.DataConfig.ConnectionString))
				{
					var results = await db.QueryAsync<string>(query);

					return results.FirstOrDefault();
				}
			}
			catch (Exception)
			{
				return null;
			}
		}
	}
}
