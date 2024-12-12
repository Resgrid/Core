using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.ResourceOrders
{
	public class SelectItemsByResourceOrderIdQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public SelectItemsByResourceOrderIdQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			var query = _sqlConfiguration.SelectItemsByResourceOrderIdQuery
				.ReplaceQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
					string.Empty,
					_sqlConfiguration.ParameterNotation,
					new string[] {
						"%RESOURCEORDERID%"
					},
					new string[] {
						"ResourceOrderId"
					},
					new string[] {
						"%RESOURCEORDERITEMSTABLE%",
						"%RESOURCEORDERFILLSTABLE%"
					},
					new string[] {
						_sqlConfiguration.ResourceOrderItemsTable,
						_sqlConfiguration.ResourceOrderFillsTable
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
