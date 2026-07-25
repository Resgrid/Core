using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository.Queries.UnitTracking
{
	public class SelectUnitTrackingDeviceByProtocolIdentifierQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;

		public SelectUnitTrackingDeviceByProtocolIdentifierQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			if (DataConfig.DatabaseType == DatabaseTypes.Postgres)
			{
				return $@"SELECT * FROM {_sqlConfiguration.SchemaName}.unittrackingdevices
					WHERE protocolkey = {_sqlConfiguration.ParameterNotation}ProtocolKey
					  AND deviceidentifier = {_sqlConfiguration.ParameterNotation}DeviceIdentifier
					  AND isenabled = true
					  AND isdeleted = false";
			}

			return $@"SELECT * FROM {_sqlConfiguration.SchemaName}.[UnitTrackingDevices]
				WHERE [ProtocolKey] = {_sqlConfiguration.ParameterNotation}ProtocolKey
				  AND [DeviceIdentifier] = {_sqlConfiguration.ParameterNotation}DeviceIdentifier
				  AND [IsEnabled] = 1
				  AND [IsDeleted] = 0";
		}

		public string GetQuery<TEntity>() where TEntity : class, IEntity => GetQuery();
	}
}
