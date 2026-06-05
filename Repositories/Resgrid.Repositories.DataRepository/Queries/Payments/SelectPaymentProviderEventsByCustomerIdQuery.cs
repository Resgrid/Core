using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository.Queries.Payments
{
	public class SelectPaymentProviderEventsByCustomerIdQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public SelectPaymentProviderEventsByCustomerIdQuery(SqlConfiguration sqlConfiguration) => _sqlConfiguration = sqlConfiguration;

		public string GetQuery()
		{
			if (DataConfig.DatabaseType == DatabaseTypes.Postgres)
				return $"SELECT * FROM {_sqlConfiguration.SchemaName}.paymentproviderevents WHERE customerid = {_sqlConfiguration.ParameterNotation}CustomerId ORDER BY recievedon DESC";

			return $"SELECT * FROM {_sqlConfiguration.SchemaName}.[PaymentProviderEvents] WHERE [CustomerId] = {_sqlConfiguration.ParameterNotation}CustomerId ORDER BY [RecievedOn] DESC";
		}

		public string GetQuery<TEntity>() where TEntity : class, IEntity => GetQuery();
	}
}
