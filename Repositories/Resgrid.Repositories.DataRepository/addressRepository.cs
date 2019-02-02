using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Repositories.DataRepository.Contexts;
using Resgrid.Repositories.DataRepository.Transactions;

namespace Resgrid.Repositories.DataRepository
{
	public class AddressRepository : RepositoryBase<Address>, IAddressRepository
	{
		public string connectionString =
			ConfigurationManager.ConnectionStrings.Cast<ConnectionStringSettings>()
				.FirstOrDefault(x => x.Name == "ResgridContext")
				.ConnectionString;

		public AddressRepository(DataContext context, IISolationLevel isolationLevel)
			: base(context, isolationLevel)
		{ }

		public async Task<Address> GetAddressByIdAsync(int addressId)
		{
			var query = $@"SELECT * FROM Addresses WHERE AddressId = @addressId";

			using (IDbConnection db = new SqlConnection(connectionString))
			{
				var results = await db.QueryAsync<Address>(query, new { addressId = addressId });

				return results.FirstOrDefault();
			}
		}
	}
}
