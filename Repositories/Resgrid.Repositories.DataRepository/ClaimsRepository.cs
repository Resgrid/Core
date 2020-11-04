using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Model.Identity;
using Resgrid.Model.Repositories;

namespace Resgrid.Repositories.DataRepository
{
	public class ClaimsRepository : IClaimsRepository
	{
		public async Task<List<string>> GetRolesAsync(IdentityUser user)
		{
			var query = $@"SELECT anr.Name FROM AspNetUserRoles anur
							INNER JOIN AspNetRoles anr ON anur.RoleId = anr.Id
							WHERE anur.UserId = @userId";

			using (IDbConnection db = new SqlConnection(ConfigurationManager.ConnectionStrings["ResgridContext"].ConnectionString))
			{
				var results = await db.QueryAsync<string>(query, new { userId = user.Id });

				return results.ToList();
			}
		}

		public async Task<IdentityUser> FindByIdAsync(string userId)
		{
			var query = $@"SELECT * FROM AspNetUsers WHERE Id = @userId";

			using (IDbConnection db = new SqlConnection(ConfigurationManager.ConnectionStrings["ResgridContext"].ConnectionString))
			{
				var results = await db.QueryAsync<IdentityUser>(query, new { id = userId });

				return results.FirstOrDefault();
			}
		}
	}
}
