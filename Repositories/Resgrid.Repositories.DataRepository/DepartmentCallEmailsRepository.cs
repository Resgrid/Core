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
	public class DepartmentCallEmailsRepository : RepositoryBase<DepartmentCallEmail>, IDepartmentCallEmailsRepository
	{
		public string connectionString =
			ConfigurationManager.ConnectionStrings.Cast<ConnectionStringSettings>()
				.FirstOrDefault(x => x.Name == "ResgridContext")
				.ConnectionString;

		public DepartmentCallEmailsRepository(DataContext context, IISolationLevel isolationLevel)
			: base(context, isolationLevel) { }

		public List<DepartmentCallEmail> GetAllDepartmentEmailSettings()
		{
			using (IDbConnection db = new SqlConnection(connectionString))
			{
				return db.Query<DepartmentCallEmail>(@"SELECT * FROM DepartmentCallEmails").ToList();
			}
		}
	}
}