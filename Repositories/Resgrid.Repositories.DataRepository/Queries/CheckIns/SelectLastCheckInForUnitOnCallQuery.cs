using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.CheckIns
{
	public class SelectLastCheckInForUnitOnCallQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public SelectLastCheckInForUnitOnCallQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			var query = _sqlConfiguration.SelectLastCheckInForUnitOnCallQuery
				.ReplaceQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
					_sqlConfiguration.CheckInRecordsTableName,
					_sqlConfiguration.ParameterNotation,
					new string[] { "%CALLID%", "%UNITID%" },
					new string[] { "CallId", "UnitId" });

			return query;
		}

		public string GetQuery<TEntity>() where TEntity : class, IEntity
		{
			throw new System.NotImplementedException();
		}
	}
}
