using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.Calls
{
	public class SelectCallProtocolsByCallIdQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public SelectCallProtocolsByCallIdQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			var query = _sqlConfiguration.SelectCallProtocolsByCallIdQuery
					.ReplaceQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
						string.Empty,
						_sqlConfiguration.ParameterNotation,
						new string[] {
							"%CALLID%"
						},
						new string[] {
							"CallId",
						},
						new string[] {
							"%CALLPROTOCOLSTABLE%",
							"%DISPATCHPROTOCOLSTABLE%"
						},
						new string[] {
							_sqlConfiguration.CallProtocolsTable,
							_sqlConfiguration.DispatchProtocolsTable
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
