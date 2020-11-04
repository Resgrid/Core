using System.Threading;
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

		public async Task<Address> GetAddressByIdAsync(int addressId)
		{
			return await _addressRepository.GetByIdAsync(addressId);
		}

		public async Task<Address> SaveAddressAsync(Address address, CancellationToken cancellationToken = default(CancellationToken))
		{
			return await _addressRepository.SaveOrUpdateAsync(address, cancellationToken);
		}

		public async Task<AddressVerificationResult> IsAddressValidAsync(Address address)
		{
			return await _addressVerificationProvider.VerifyAddressAsync(address);
		}
	}
}
