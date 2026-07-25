using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository.Queries.UnitTracking
{
	public class RevokeUnitTrackingCredentialsByDeviceQuery : IUpdateQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;

		public RevokeUnitTrackingCredentialsByDeviceQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery<TEntity>(TEntity entity)
		{
			if (DataConfig.DatabaseType == DatabaseTypes.Postgres)
			{
				return $@"UPDATE {_sqlConfiguration.SchemaName}.unittrackingcredentials
					SET revokedon = {_sqlConfiguration.ParameterNotation}RevokedOn
					WHERE unittrackingdeviceid = {_sqlConfiguration.ParameterNotation}UnitTrackingDeviceId
					AND revokedon IS NULL";
			}

			return $@"UPDATE {_sqlConfiguration.SchemaName}.[UnitTrackingCredentials]
				SET [RevokedOn] = {_sqlConfiguration.ParameterNotation}RevokedOn
				WHERE [UnitTrackingDeviceId] = {_sqlConfiguration.ParameterNotation}UnitTrackingDeviceId
				AND [RevokedOn] IS NULL";
		}
	}
}
