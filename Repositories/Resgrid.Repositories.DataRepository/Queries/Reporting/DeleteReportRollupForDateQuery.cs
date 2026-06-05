using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.Reporting
{
	public class DeleteReportRollupForDateQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public DeleteReportRollupForDateQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			return _sqlConfiguration.DeleteReportRollupForDateQuery
				.ReplaceQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
					_sqlConfiguration.ReportingDailyRollupTable,
					_sqlConfiguration.ParameterNotation,
					new string[] { "%BUCKETDATE%", "%HASDEPT%", "%DID%" },
					new string[] { "BucketDate", "HasDept", "DepartmentId" });
		}

		public string GetQuery<TEntity>() where TEntity : class, IEntity
		{
			throw new System.NotImplementedException();
		}
	}
}
