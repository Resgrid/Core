using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface IAddressRepository : IRepository<Address>
	{
		Task<Address> GetAddressByIdAsync(int addressId);
	}
}
