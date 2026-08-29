using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>See <see cref="IMemberProfileRelocationService"/>.</summary>
	public class MemberProfileRelocationService : IMemberProfileRelocationService
	{
		private readonly IDepartmentMemberSensitiveDataRepository _repository;
		private readonly IDepartmentMemberSensitiveDataService _sensitiveDataService;
		private readonly IUserProfileService _userProfileService;
		private readonly IAddressService _addressService;

		public MemberProfileRelocationService(IDepartmentMemberSensitiveDataRepository repository,
			IDepartmentMemberSensitiveDataService sensitiveDataService, IUserProfileService userProfileService,
			IAddressService addressService)
		{
			_repository = repository;
			_sensitiveDataService = sensitiveDataService;
			_userProfileService = userProfileService;
			_addressService = addressService;
		}

		public async Task<IReadOnlyList<int>> GetDepartmentIdsWithOutstandingDataAsync()
		{
			var ids = await _repository.GetDepartmentIdsWithOutstandingLegacyProfileDataAsync();
			return ids?.Distinct().OrderBy(x => x).ToList() ?? new List<int>();
		}

		public async Task<MemberProfileRelocationResult> RelocateDepartmentAsync(int departmentId,
			CancellationToken cancellationToken = default)
		{
			var result = new MemberProfileRelocationResult { DepartmentId = departmentId };
			if (departmentId <= 0)
				return result;

			// Includes disabled and deleted members on purpose: their identification number and
			// address are still their personal data and still have to end up under the department's
			// protection, and leaving them behind would keep the backlog non-empty forever.
			var profiles = await _userProfileService.GetAllProfilesForDepartmentIncDisabledDeletedAsync(departmentId);
			if (profiles == null || profiles.Count == 0)
				return result;

			var rows = (await _repository.GetAllByDepartmentIdAsync(departmentId))?.ToList()
				?? new List<DepartmentMemberSensitiveData>();

			// Read straight from the repository, NOT through the read pipeline: this pass must see
			// whether a target column already holds something, and an rgdp envelope counts as
			// "already has a value". Resolving first would hand back REDACTED sentinels for a
			// protected department and make populated fields look empty.
			var byUser = rows
				.Where(r => !string.IsNullOrWhiteSpace(r.UserId))
				.GroupBy(r => r.UserId, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

			var addressCache = new Dictionary<int, Address>();

			async Task<Address> GetAddressAsync(int addressId)
			{
				if (addressCache.TryGetValue(addressId, out var cached))
					return cached;

				Address address = null;
				try
				{
					address = await _addressService.GetAddressByIdAsync(addressId);
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"MemberProfileRelocation: address {addressId} for department {departmentId}");
				}

				addressCache[addressId] = address;
				return address;
			}

			foreach (var profile in profiles.Values.Where(p => p != null && !string.IsNullOrWhiteSpace(p.UserId)))
			{
				cancellationToken.ThrowIfCancellationRequested();

				byUser.TryGetValue(profile.UserId, out var row);
				if (row != null && row.LegacyProfileRelocatedOn.HasValue)
					continue;

				result.MembersExamined++;

				try
				{
					var created = row == null;
					row ??= new DepartmentMemberSensitiveData { DepartmentId = departmentId, UserId = profile.UserId };

					if (string.IsNullOrWhiteSpace(row.IdentificationNumber) &&
						!string.IsNullOrWhiteSpace(profile.IdentificationNumber))
					{
						row.IdentificationNumber = profile.IdentificationNumber;
						result.IdentificationNumbersMoved++;
					}

					if (string.IsNullOrWhiteSpace(row.HomeAddress1) && profile.HomeAddressId.HasValue)
					{
						var home = await GetAddressAsync(profile.HomeAddressId.Value);
						if (home != null && !string.IsNullOrWhiteSpace(home.Address1))
						{
							row.HomeAddress1 = home.Address1;
							row.HomeCity = home.City;
							row.HomeState = home.State;
							row.HomePostalCode = home.PostalCode;
							row.HomeCountry = home.Country;
							result.AddressesMoved++;
						}
					}

					if (string.IsNullOrWhiteSpace(row.MailingAddress1) && profile.MailingAddressId.HasValue)
					{
						var mailing = await GetAddressAsync(profile.MailingAddressId.Value);
						if (mailing != null && !string.IsNullOrWhiteSpace(mailing.Address1))
						{
							row.MailingAddress1 = mailing.Address1;
							row.MailingCity = mailing.City;
							row.MailingState = mailing.State;
							row.MailingPostalCode = mailing.PostalCode;
							row.MailingCountry = mailing.Country;
							result.AddressesMoved++;
						}
					}

					// Marked even when nothing moved. The marker means "this member has been through
					// relocation", not "this member had data"; without that, every member who never
					// filled in an address would be re-examined on every pass forever.
					row.LegacyProfileRelocatedOn = DateTime.UtcNow;

					// Through the service, so the ADP write safety net runs: for an enrolled
					// department the values are enveloped as they land, and a blocked write throws
					// rather than parking plaintext in a protected row.
					await _sensitiveDataService.SaveAsync(row, cancellationToken);

					if (created)
						result.RowsCreated++;
				}
				catch (Exception ex)
				{
					// The marker was never persisted, so this member is picked up again next pass.
					result.Failures++;
					Logging.LogException(ex, $"MemberProfileRelocation: department {departmentId}, user {profile.UserId}");
				}
			}

			return result;
		}
	}
}
