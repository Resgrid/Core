using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.RunCards
{
	public class SelectRunCardRoleRequirementsByRunCardIdQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public SelectRunCardRoleRequirementsByRunCardIdQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			var query = _sqlConfiguration.SelectRunCardRoleRequirementsByRunCardIdQuery
				.ReplaceQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
					_sqlConfiguration.RunCardRoleRequirementsTableName,
					_sqlConfiguration.ParameterNotation,
					new string[] { "%RCID%" },
					new string[] { "RunCardId" });

			return query;
		}

		public string GetQuery<TEntity>() where TEntity : class, IEntity
		{
			return GetQuery();
		}
	}
}
