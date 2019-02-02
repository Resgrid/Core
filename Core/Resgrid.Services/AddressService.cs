using System.Linq;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class AddressService : IAddressService
	{
		private readonly IAddressRepository _addressRepository;
		private readonly IAddressVerificationProvider _addressVerificationProvider;

		public AddressService(IAddressRepository addressRepository, IAddressVerificationProvider addressVerificationProvider)
		{
			_addressRepository = addressRepository;
			_addressVerificationProvider = addressVerificationProvider;
		}

		public Address GetAddressById(int addressId)
		{
			return _addressRepository.GetAll().FirstOrDefault(x => x.AddressId == addressId);
		}

		public async Task<Address> GetAddressByIdAsync(int addressId)
		{
			return await _addressRepository.GetAddressByIdAsync(addressId);
		}

		public Address SaveAddress(Address address)
		{
			_addressRepository.SaveOrUpdate(address);

			return address;
		}

		public AddressVerificationResult IsAddressValid(Address address)
		{
			return _addressVerificationProvider.VerifyAddress(address);
		}
	}
}
