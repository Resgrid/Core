using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.ResourceOrders
{
	public class UpdateOrderFillStatusQuery : IUpdateQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public UpdateOrderFillStatusQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery<TEntity>(TEntity entity)
		{
			var updateValues = new string[] {"Accepted", "AcceptedOn", "AcceptedUserId"};
			var setFragment = updateValues.UpdateQuerySetFragment(_sqlConfiguration.ParameterNotation);

			var query = _sqlConfiguration.UpdateRoleQuery
				.ReplaceUpdateQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
					_sqlConfiguration.RoleTable,
					setFragment,
					"ResourceOrderFillId",
					$"{_sqlConfiguration.ParameterNotation}Id");

			return query;
		}
	}
}
