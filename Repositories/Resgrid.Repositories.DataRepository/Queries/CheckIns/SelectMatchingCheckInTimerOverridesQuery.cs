using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.CheckIns
{
	public class SelectMatchingCheckInTimerOverridesQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public SelectMatchingCheckInTimerOverridesQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			var query = _sqlConfiguration.SelectMatchingCheckInTimerOverridesQuery
				.ReplaceQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
					_sqlConfiguration.CheckInTimerOverridesTableName,
					_sqlConfiguration.ParameterNotation,
					new string[] { "%DID%", "%CTID%", "%CPRI%" },
					new string[] { "DepartmentId", "CallTypeId", "CallPriority" });

			return query;
		}

		public string GetQuery<TEntity>() where TEntity : class, IEntity
		{
			throw new System.NotImplementedException();
		}
	}
}
