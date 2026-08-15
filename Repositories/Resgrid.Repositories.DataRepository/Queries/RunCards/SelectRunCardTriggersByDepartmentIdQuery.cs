using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.RunCards
{
	public class SelectRunCardTriggersByDepartmentIdQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public SelectRunCardTriggersByDepartmentIdQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			var query = _sqlConfiguration.SelectRunCardTriggersByDepartmentIdQuery
				.ReplaceQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
					_sqlConfiguration.RunCardTriggersTableName,
					_sqlConfiguration.ParameterNotation,
					new string[] { "%DID%" },
					new string[] { "DepartmentId" });

			return query;
		}

		public string GetQuery<TEntity>() where TEntity : class, IEntity
		{
			return GetQuery();
		}
	}
}
