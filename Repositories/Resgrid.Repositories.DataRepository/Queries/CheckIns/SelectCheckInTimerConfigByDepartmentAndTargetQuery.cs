using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.CheckIns
{
	public class SelectCheckInTimerConfigByDepartmentAndTargetQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public SelectCheckInTimerConfigByDepartmentAndTargetQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			var query = _sqlConfiguration.SelectCheckInTimerConfigByDepartmentAndTargetQuery
				.ReplaceQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
					_sqlConfiguration.CheckInTimerConfigsTableName,
					_sqlConfiguration.ParameterNotation,
					new string[] { "%DID%", "%TTT%", "%UTID%" },
					new string[] { "DepartmentId", "TimerTargetType", "UnitTypeId" });

			return query;
		}

		public string GetQuery<TEntity>() where TEntity : class, IEntity
		{
			return GetQuery();
		}
	}
}
