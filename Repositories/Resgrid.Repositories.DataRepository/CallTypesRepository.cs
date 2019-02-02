using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Repositories.DataRepository.Contexts;
using Resgrid.Repositories.DataRepository.Transactions;

namespace Resgrid.Repositories.DataRepository
{
	public class CallTypesRepository : RepositoryBase<CallType>, ICallTypesRepository
	{
		public string connectionString =
			ConfigurationManager.ConnectionStrings.Cast<ConnectionStringSettings>()
				.FirstOrDefault(x => x.Name == "ResgridContext")
				.ConnectionString;

		public CallTypesRepository(DataContext context, IISolationLevel isolationLevel)
			: base(context, isolationLevel)
		{ }

		public async Task<List<CallType>> GetCallTypesForDepartmentAsync(int departmentId)
		{
			var query = $@"SELECT * FROM CallTypes 
							WHERE DepartmentId = @departmentId";

			using (IDbConnection db = new SqlConnection(connectionString))
			{
				var results = await db.QueryAsync<CallType>(query, new { departmentId = departmentId });

				return results.ToList();
			}
		}
	}
}
