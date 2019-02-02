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
	public class DepartmentGroupMembersRepository : RepositoryBase<DepartmentGroupMember>, IDepartmentGroupMembersRepository
	{
		public string connectionString =
			ConfigurationManager.ConnectionStrings.Cast<ConnectionStringSettings>()
				.FirstOrDefault(x => x.Name == "ResgridContext")
				.ConnectionString;

		public DepartmentGroupMembersRepository(DataContext context, IISolationLevel isolationLevel)
			: base(context, isolationLevel) { }


		public List<DepartmentGroupMember> GetAllMembersForGroup(int groupId)
		{
			using (IDbConnection db = new SqlConnection(connectionString))
			{
				var result = db.Query<DepartmentGroupMember>(@"SELECT dgm.* FROM DepartmentGroupMembers dgm
															INNER JOIN DepartmentMembers dm ON dgm.UserId = dm.UserId
															WHERE dgm.DepartmentGroupId = @departmentGroupId AND dm.IsDeleted = 0 AND dm.IsDisabled = 0",
						new { departmentGroupId = groupId });

				return result.ToList();
			}
		}
	}
}
