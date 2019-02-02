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
	public class DepartmentNotificationRepository : RepositoryBase<DepartmentNotification>, IDepartmentNotificationRepository
	{
		public string connectionString =
			ConfigurationManager.ConnectionStrings.Cast<ConnectionStringSettings>()
				.FirstOrDefault(x => x.Name == "ResgridContext")
				.ConnectionString;

		public DepartmentNotificationRepository(DataContext context, IISolationLevel isolationLevel)
			: base(context, isolationLevel) { }

		public List<DepartmentNotification> GetNotificationsByDepartment(int departmentId)
		{
			using (IDbConnection db = new SqlConnection(connectionString))
			{
				var result = db.Query<DepartmentNotification>($"SELECT * FROM DepartmentNotifications WHERE DepartmentId = @departmentId", new { departmentId = departmentId });
				return result.ToList();
			}
		}
	}
}