using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository.Queries.UnitTracking
{
	public class SelectUnitTrackingCredentialBySecretHashQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;

		public SelectUnitTrackingCredentialBySecretHashQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			if (DataConfig.DatabaseType == DatabaseTypes.Postgres)
			{
				return $@"SELECT * FROM {_sqlConfiguration.SchemaName}.unittrackingcredentials
					WHERE secrethash = {_sqlConfiguration.ParameterNotation}SecretHash";
			}

			return $@"SELECT * FROM {_sqlConfiguration.SchemaName}.[UnitTrackingCredentials]
				WHERE [SecretHash] = {_sqlConfiguration.ParameterNotation}SecretHash";
		}

		public string GetQuery<TEntity>() where TEntity : class, IEntity => GetQuery();
	}
}
