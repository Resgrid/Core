using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.Reporting
{
	public class InsertReportRollupQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public InsertReportRollupQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			// Values are bound from the ReportingDailyRollup object by Dapper (literal @params);
			// only schema/table tokens need substitution here.
			return _sqlConfiguration.InsertReportRollupQuery
				.ReplaceQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
					_sqlConfiguration.ReportingDailyRollupTable,
					_sqlConfiguration.ParameterNotation,
					new string[] { },
					new string[] { });
		}

		public string GetQuery<TEntity>() where TEntity : class, IEntity
		{
			throw new System.NotImplementedException();
		}
	}
}
