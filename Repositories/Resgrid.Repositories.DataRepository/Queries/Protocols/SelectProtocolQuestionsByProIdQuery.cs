using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.Protocols
{
	public class SelectProtocolQuestionsByProIdQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public SelectProtocolQuestionsByProIdQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			var query = _sqlConfiguration.SelectProtocolQuestionsByProIdQuery
										 .ReplaceQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
																 string.Empty,
																 _sqlConfiguration.ParameterNotation,
																new string[] {
																				"%PROTOCOLID%"
																			  },
																 new string[] {
																				"ProtocolId",
																			  },
																 new string[] {
																				"%PROTOCOLQUESTIONSTABLE%",
																				"%PROTOCOLQUESTIONANSWERSTABLE%"
																 },
																 new string[] {
																				_sqlConfiguration.DispatchProtocolQuestionsTable,
																				_sqlConfiguration.DispatchProtocolQuestionAnswersTable
																 }
																 );

			return query;
		}

		public string GetQuery<TEntity>() where TEntity : class, IEntity
		{
			throw new System.NotImplementedException();
		}
	}
}
