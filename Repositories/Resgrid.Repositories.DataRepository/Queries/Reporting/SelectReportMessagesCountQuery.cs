using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.Reporting
{
	public class SelectReportMessagesCountQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public SelectReportMessagesCountQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			return _sqlConfiguration.SelectReportMessagesCountQuery
				.ReplaceQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
					_sqlConfiguration.MessagesTable,
					_sqlConfiguration.ParameterNotation,
					new string[] { "%ALLDEPTS%", "%DID%", "%STARTDATE%", "%ENDDATE%" },
					new string[] { "AllDepts", "DepartmentId", "StartDate", "EndDate" },
					new string[] { "%MEMBERSTABLE%" },
					new string[] { _sqlConfiguration.DepartmentMembersTable });
		}

		public string GetQuery<TEntity>() where TEntity : class, IEntity
		{
			throw new System.NotImplementedException();
		}
	}
}
