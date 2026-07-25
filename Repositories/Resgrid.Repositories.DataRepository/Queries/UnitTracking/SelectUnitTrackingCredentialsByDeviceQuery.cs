using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository.Queries.UnitTracking
{
	public class SelectUnitTrackingCredentialsByDeviceQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;

		public SelectUnitTrackingCredentialsByDeviceQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			if (DataConfig.DatabaseType == DatabaseTypes.Postgres)
			{
				return $@"SELECT * FROM {_sqlConfiguration.SchemaName}.unittrackingcredentials
					WHERE unittrackingdeviceid = {_sqlConfiguration.ParameterNotation}UnitTrackingDeviceId
					ORDER BY createdon DESC";
			}

			return $@"SELECT * FROM {_sqlConfiguration.SchemaName}.[UnitTrackingCredentials]
				WHERE [UnitTrackingDeviceId] = {_sqlConfiguration.ParameterNotation}UnitTrackingDeviceId
				ORDER BY [CreatedOn] DESC";
		}

		public string GetQuery<TEntity>() where TEntity : class, IEntity => GetQuery();
	}
}
