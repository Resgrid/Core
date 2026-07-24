using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository.Queries.UnitTracking
{
	public class SelectUnitTrackingDevicesByUnitQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;

		public SelectUnitTrackingDevicesByUnitQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			if (DataConfig.DatabaseType == DatabaseTypes.Postgres)
			{
				return $@"SELECT * FROM {_sqlConfiguration.SchemaName}.unittrackingdevices
					WHERE departmentid = {_sqlConfiguration.ParameterNotation}DepartmentId
					  AND unitid = {_sqlConfiguration.ParameterNotation}UnitId
					ORDER BY createdon DESC";
			}

			return $@"SELECT * FROM {_sqlConfiguration.SchemaName}.[UnitTrackingDevices]
				WHERE [DepartmentId] = {_sqlConfiguration.ParameterNotation}DepartmentId
				  AND [UnitId] = {_sqlConfiguration.ParameterNotation}UnitId
				ORDER BY [CreatedOn] DESC";
		}

		public string GetQuery<TEntity>() where TEntity : class, IEntity => GetQuery();
	}
}
