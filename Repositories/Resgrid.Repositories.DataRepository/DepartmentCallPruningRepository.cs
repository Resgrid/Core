using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using Dapper;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Repositories.DataRepository.Contexts;
using Resgrid.Repositories.DataRepository.Transactions;

namespace Resgrid.Repositories.DataRepository
{
	public class DepartmentCallPruningRepository : RepositoryBase<DepartmentCallPruning>, IDepartmentCallPruningRepository
	{
		public string connectionString =
			ConfigurationManager.ConnectionStrings.Cast<ConnectionStringSettings>()
				.FirstOrDefault(x => x.Name == "ResgridContext")
				.ConnectionString;

		public DepartmentCallPruningRepository(DataContext context, IISolationLevel isolationLevel)
			: base(context, isolationLevel) { }

		public List<DepartmentCallPruning> GetAllDepartmentCallPrunings()
		{
			using (IDbConnection db = new SqlConnection(connectionString))
			{
				return db.Query<DepartmentCallPruning>(@"SELECT * FROM DepartmentCallPruning").ToList();
			}
		}

		public DepartmentCallPruning GetDepartmentCallPruningSettings(int departmentId)
		{
			using (IDbConnection db = new SqlConnection(connectionString))
			{
				var result = db.Query<DepartmentCallPruning>(@"SELECT * FROM DepartmentCallPruning
															WHERE DepartmentId = @departmentId",
						new { departmentId = departmentId });

				return result.FirstOrDefault();
			}
		}
	}
}
