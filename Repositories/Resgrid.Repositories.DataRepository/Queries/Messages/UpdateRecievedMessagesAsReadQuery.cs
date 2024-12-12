using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.Messages
{
	public class UpdateRecievedMessagesAsReadQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public UpdateRecievedMessagesAsReadQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			var query = _sqlConfiguration.UpdateRecievedMessagesAsReadQuery
				.ReplaceQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
					_sqlConfiguration.MessageRecipientsTable,
					_sqlConfiguration.ParameterNotation,
					new string[] { "%USERID%", "%READON%" },
					new string[] { "UserId", "ReadOn" });

			return query;
		}

		public string GetQuery<TEntity>() where TEntity : class, IEntity
		{
			throw new System.NotImplementedException();
		}
	}
}
