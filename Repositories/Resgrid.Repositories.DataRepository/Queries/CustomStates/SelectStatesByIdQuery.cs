using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.CustomStates
{
	public class SelectStatesByIdQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public SelectStatesByIdQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			var query = _sqlConfiguration.SelectStatesByIdQuery
										 .ReplaceQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
																 string.Empty,
																 _sqlConfiguration.ParameterNotation,
																new string[] {
																				"%CUSTOMSTATEID%"
																			  },
																 new string[] {
																				"CustomStateId",
																			  },
																 new string[] {
																				"%CUSTOMSTATESTABLE%",
																				"%MCUSTOMSTATEDETAILSTABLE%"
																 },
																 new string[] {
																				_sqlConfiguration.CustomStatesTable,
																				_sqlConfiguration.CustomStateDetailsTable
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
