using System.Threading.Tasks;
using Resgrid.Model.Providers;

namespace Resgrid.Model.Services
{
	public interface IAddressService
	{
		Address GetAddressById(int addressId);
		Address SaveAddress(Address address);
		AddressVerificationResult IsAddressValid(Address address);
		Task<Address> GetAddressByIdAsync(int addressId);
	}
}
