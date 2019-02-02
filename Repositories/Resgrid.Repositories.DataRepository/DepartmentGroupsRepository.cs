using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Data.SqlServerCe;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Model;
using Resgrid.Model.Custom;
using Resgrid.Model.Repositories;
using Resgrid.Repositories.DataRepository.Contexts;
using Resgrid.Repositories.DataRepository.Transactions;

namespace Resgrid.Repositories.DataRepository
{
	public class DepartmentGroupsRepository : RepositoryBase<DepartmentGroup>, IDepartmentGroupsRepository
	{
		public string connectionString =
			ConfigurationManager.ConnectionStrings.Cast<ConnectionStringSettings>()
				.FirstOrDefault(x => x.Name == "ResgridContext")
				.ConnectionString;

		public DepartmentGroupsRepository(DataContext context, IISolationLevel isolationLevel)
			: base(context, isolationLevel) { }


		public async Task<List<DepartmentGroup>> GetAllGroupsForDepartmentUnlimitedAsync(int departmentId)
		{
			using (IDbConnection db = new SqlConnection(connectionString))
			{
				var result = await db.QueryAsync<DepartmentGroup>(@"SELECT * FROM DepartmentGroups WHERE DepartmentId = @departmentId",
						new { departmentId = departmentId });

				return result.ToList();
			}
		}

		public List<DepartmentGroup> GetAllStationGroupsForDepartment(int departmentId)
		{
			Dictionary<int, DepartmentGroup> lookup = new Dictionary<int, DepartmentGroup>();

			using (IDbConnection db = new SqlConnection(connectionString))
			{
				var query = @"SELECT * FROM DepartmentGroups dg
							LEFT OUTER JOIN Addresses a ON dg.AddressId = a.AddressId
							WHERE dg.DepartmentId = @departmentId AND dg.Type = 2";

				db.Query<DepartmentGroup, Address, DepartmentGroup>(query, (dg, a) =>
				{
					DepartmentGroup group;

					if (!lookup.TryGetValue(dg.DepartmentGroupId, out group))
					{
						lookup.Add(dg.DepartmentGroupId, dg);
						group = dg;
					}

					if (a != null && dg.Address == null)
					{
						group.Address = a;
					}

					return dg;

				}, new { departmentId = departmentId }, splitOn: "AddressId").ToList();
			}

			return lookup.Values.ToList();
		}
	}
}
