using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.CheckIns
{
	public class SelectLastCheckInForUserOnCallQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public SelectLastCheckInForUserOnCallQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			var query = _sqlConfiguration.SelectLastCheckInForUserOnCallQuery
				.ReplaceQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
					_sqlConfiguration.CheckInRecordsTableName,
					_sqlConfiguration.ParameterNotation,
					new string[] { "%CALLID%", "%UID%" },
					new string[] { "CallId", "UserId" });

			return query;
		}

		public string GetQuery<TEntity>() where TEntity : class, IEntity
		{
			throw new System.NotImplementedException();
		}
	}
}
