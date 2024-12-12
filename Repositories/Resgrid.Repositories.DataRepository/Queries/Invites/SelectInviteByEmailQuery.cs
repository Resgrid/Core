using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.Invites
{
	public class SelectInviteByEmailQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public SelectInviteByEmailQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			var query = _sqlConfiguration.SelectInviteByEmailQuery
				.ReplaceQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
					_sqlConfiguration.InvitesTable,
					_sqlConfiguration.ParameterNotation,
					new string[] { "%EMAIL%" },
					new string[] { "EmailAddress" });

			return query;
		}

		public string GetQuery<TEntity>() where TEntity : class, IEntity
		{
			throw new System.NotImplementedException();
		}
	}
}
