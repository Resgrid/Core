using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Providers;

namespace Resgrid.Model.Services
{
	public interface IAddressService
	{
		Task<Address> GetAddressByIdAsync(int addressId);
		Task<Address> SaveAddressAsync(Address address, CancellationToken cancellationToken = default(CancellationToken));
		Task<AddressVerificationResult> IsAddressValidAsync(Address address);
		Task<bool> DeleteAddress(int addressId, CancellationToken cancellationToken = default(CancellationToken));
	}
}
