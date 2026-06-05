using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.Reporting
{
	public class SelectReportRollupsQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public SelectReportRollupsQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			return _sqlConfiguration.SelectReportRollupsQuery
				.ReplaceQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
					_sqlConfiguration.ReportingDailyRollupTable,
					_sqlConfiguration.ParameterNotation,
					new string[] { "%STARTDATE%", "%ENDDATE%", "%METRIC%", "%HASDEPT%", "%DID%" },
					new string[] { "StartDate", "EndDate", "Metric", "HasDept", "DepartmentId" });
		}

		public string GetQuery<TEntity>() where TEntity : class, IEntity
		{
			throw new System.NotImplementedException();
		}
	}
}
