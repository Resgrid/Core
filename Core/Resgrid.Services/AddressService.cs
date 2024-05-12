using System;
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
		private static string CacheKey = "Address_{0}";
		private static TimeSpan CacheLength = TimeSpan.FromDays(90);

		private readonly IAddressRepository _addressRepository;
		private readonly IAddressVerificationProvider _addressVerificationProvider;
		private readonly ICacheProvider _cacheProvider;

		public AddressService(IAddressRepository addressRepository, IAddressVerificationProvider addressVerificationProvider, ICacheProvider cacheProvider)
		{
			_addressRepository = addressRepository;
			_addressVerificationProvider = addressVerificationProvider;
			_cacheProvider = cacheProvider;
		}

		public async Task<Address> GetAddressByIdAsync(int addressId)
		{
			async Task<Address> getDepartmentGroup()
			{
				var address = await _addressRepository.GetByIdAsync(addressId);

				return address;
			}

			if (Config.SystemBehaviorConfig.CacheEnabled)
			{
				return await _cacheProvider.RetrieveAsync(string.Format(CacheKey, addressId), getDepartmentGroup, CacheLength);
			}

			return await getDepartmentGroup();
		}

		public async Task<Address> SaveAddressAsync(Address address, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (address.AddressId > 0)
				_cacheProvider.Remove(string.Format(CacheKey, address.AddressId));

			return await _addressRepository.SaveOrUpdateAsync(address, cancellationToken);
		}

		public async Task<AddressVerificationResult> IsAddressValidAsync(Address address)
		{
			return await _addressVerificationProvider.VerifyAddressAsync(address);
		}

		public async Task<bool> DeleteAddress(int addressId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var address = await _addressRepository.GetByIdAsync(addressId);

			if (address != null)
				return await _addressRepository.DeleteAsync(address, cancellationToken);

			return false;
		}
	}
}
