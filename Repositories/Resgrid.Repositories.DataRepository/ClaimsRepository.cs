using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Npgsql;
using Resgrid.Config;
using Resgrid.Model.Identity;
using Resgrid.Model.Repositories;
using static Resgrid.Framework.Testing.TestData;

namespace Resgrid.Repositories.DataRepository
{
	public class ClaimsRepository : IClaimsRepository
	{
		public async Task<List<string>> GetRolesAsync(IdentityUser user)
		{
			if (Config.DataConfig.DatabaseType == DatabaseTypes.Postgres)
			{
				var query = @"SELECT anr.name FROM aspnetuserroles anur
							INNER JOIN aspnetroles anr ON anur.roleid = anr.id
							WHERE anur.userid = @userId";


				using (IDbConnection db = new NpgsqlConnection(DataConfig.CoreConnectionString))
				{
					return (await db.QueryAsync<string>(query, new { userId = user.Id })).ToList();
				}
			}
			else
			{
				var query = @"SELECT anr.Name FROM AspNetUserRoles anur
							INNER JOIN AspNetRoles anr ON anur.RoleId = anr.Id
							WHERE anur.UserId = @userId";

				using (IDbConnection db = new SqlConnection(DataConfig.CoreConnectionString))
				{
					var results = await db.QueryAsync<string>(query, new { userId = user.Id });

					return results.ToList();
				}
			}

		}

		public async Task<IdentityUser> FindByIdAsync(string userId)
		{
			if (Config.DataConfig.DatabaseType == DatabaseTypes.Postgres)
			{
				var query = @"SELECT * FROM aspnetusers WHERE id = @userId";

				using (IDbConnection db = new NpgsqlConnection(DataConfig.CoreConnectionString))
				{
					return (await db.QueryAsync<IdentityUser>(query, new { userId = userId })).FirstOrDefault();
				}
			}
			else
			{
				var query = @"SELECT * FROM AspNetUsers WHERE Id = @userId";

				using (IDbConnection db = new SqlConnection(DataConfig.CoreConnectionString))
				{
					var results = await db.QueryAsync<IdentityUser>(query, new { id = userId });

					return results.FirstOrDefault();
				}
			}
		}
	}
}
