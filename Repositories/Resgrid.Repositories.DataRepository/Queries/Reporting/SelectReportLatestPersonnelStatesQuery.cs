using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.Reporting
{
	public class SelectReportLatestPersonnelStatesQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public SelectReportLatestPersonnelStatesQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			return _sqlConfiguration.SelectReportLatestPersonnelStatesQuery
				.ReplaceQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
					_sqlConfiguration.ActionLogsTable,
					_sqlConfiguration.ParameterNotation,
					new string[] { "%ALLDEPTS%", "%DID%" },
					new string[] { "AllDepts", "DepartmentId" });
		}

		public string GetQuery<TEntity>() where TEntity : class, IEntity
		{
			throw new System.NotImplementedException();
		}
	}
}
