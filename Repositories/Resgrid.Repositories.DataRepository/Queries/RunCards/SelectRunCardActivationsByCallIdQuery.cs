using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.RunCards
{
	public class SelectRunCardActivationsByCallIdQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public SelectRunCardActivationsByCallIdQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			var query = _sqlConfiguration.SelectRunCardActivationsByCallIdQuery
				.ReplaceQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
					_sqlConfiguration.RunCardActivationsTableName,
					_sqlConfiguration.ParameterNotation,
					new string[] { "%CALLID%" },
					new string[] { "CallId" });

			return query;
		}

		public string GetQuery<TEntity>() where TEntity : class, IEntity
		{
			return GetQuery();
		}
	}
}
