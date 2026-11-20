using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Identity;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Dedicated department numbers (Dedicated Number add-on, PlanAddonTypes 7; call-intake-and-ai-destinations-plan.md D1/D5, §8 D10–D13).
	/// A department with the add-on searches (country, state / province, city, postal code, area code, digits or near its address) and
	/// buys one Twilio number; it routes inbound SMS to intake (inbound only) and inbound voice to the member IVR and Enhanced AI voice
	/// intake. The Active number is mirrored into the legacy TextToCallNumber setting (DepartmentSettingTypes 10).
	/// <para>
	/// Stopped paying (D10) = no paid add-on interval, or the plan known not to be an active paid entity plan; unknown billing changes
	/// nothing. T0 (<see cref="DepartmentPhoneNumber.AddonInactiveSince"/>) is when worker 80 first sees it. Intake stops at
	/// T0 + <see cref="DedicatedNumberAddonConfig.LapseDelayHours"/> (Lapsed), the number is released at T0 +
	/// <see cref="DedicatedNumberAddonConfig.ReleaseGraceDays"/>, and paying again before release restores it. The department's active
	/// admins are warned at T0 and <see cref="DedicatedNumberAddonConfig.ReleaseWarningDays"/> before release (D12).
	/// </para>
	/// </summary>
	public sealed class DepartmentPhoneNumbersService : IDepartmentPhoneNumbersService
	{
		private static readonly int[] HeldStatuses =
		{
			(int)DepartmentPhoneNumberStatuses.Provisioning, (int)DepartmentPhoneNumberStatuses.Active, (int)DepartmentPhoneNumberStatuses.Lapsed
		};

		private static readonly int[] RoutingStatuses = { (int)DepartmentPhoneNumberStatuses.Active, (int)DepartmentPhoneNumberStatuses.Lapsed };

		private const int SearchLimit = 20;

		/// <summary>
		/// A stop seen only through the plan (the add-on still paid) must hold this long before the "payment stopped" warning goes out: the
		/// Billing API answers a plan read it could not resolve with the free plan, so one odd read must never alarm a paying department.
		/// Any paying sweep in between clears T0.
		/// </summary>
		public static readonly TimeSpan PlanStopConfirmation = TimeSpan.FromHours(1);

		private static readonly Regex UsPostalCode = new Regex(@"^(\d{5})(-\d{4})?$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
		private static readonly Regex CanadaPostalCode = new Regex(@"^([A-Z]\d[A-Z])(?: ?(\d[A-Z]\d))?$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

		private readonly IDepartmentPhoneNumbersRepository _repository;
		private readonly IDedicatedNumberAccessService _access;
		private readonly INumberProvider _carrier;
		private readonly IPhoneNumberProcesserProvider _phoneNumbers;
		private readonly IIntakeChannelsService _intakeChannels;
		private readonly IDepartmentSettingsService _settings;
		private readonly IDepartmentsService _departments;
		private readonly ICommunicationService _communication;
		private readonly IGeoLocationProvider _geo;
		private readonly IAddressService _addresses;

		public DepartmentPhoneNumbersService(IDepartmentPhoneNumbersRepository repository, IDedicatedNumberAccessService access, INumberProvider carrier,
			IPhoneNumberProcesserProvider phoneNumbers, IIntakeChannelsService intakeChannels, IDepartmentSettingsService settings,
			IDepartmentsService departments, ICommunicationService communication, IGeoLocationProvider geo, IAddressService addresses)
		{
			_repository = repository;
			_access = access;
			_carrier = carrier;
			_phoneNumbers = phoneNumbers;
			_intakeChannels = intakeChannels;
			_settings = settings;
			_departments = departments;
			_communication = communication;
			_geo = geo;
			_addresses = addresses;
		}

		public async Task<DepartmentPhoneNumber> GetActiveForDepartmentAsync(int departmentId) =>
			(await GetAllForDepartmentAsync(departmentId)).FirstOrDefault(n => n.Status == (int)DepartmentPhoneNumberStatuses.Active);

		public async Task<List<DepartmentPhoneNumber>> GetAllForDepartmentAsync(int departmentId)
		{
			if (departmentId <= 0)
				return new List<DepartmentPhoneNumber>();

			var rows = await _repository.GetAllByDepartmentIdAsync(departmentId);
			return (rows ?? Enumerable.Empty<DepartmentPhoneNumber>()).Where(r => r != null && r.DepartmentId == departmentId)
				.OrderByDescending(r => r.CreatedOn).ToList();
		}

		public async Task<DepartmentPhoneNumber> GetByNumberAsync(string phoneNumber)
		{
			var e164 = NormalizeE164(phoneNumber);
			if (e164 == null)
				return null;

			var rows = await _repository.GetByNumberAsync(e164, RoutingStatuses);
			return rows?.Where(r => r != null).OrderBy(r => r.Status == (int)DepartmentPhoneNumberStatuses.Active ? 0 : 1).FirstOrDefault();
		}

		public Task<List<AvailablePhoneNumber>> SearchAvailableAsync(int departmentId, string countryCode, string areaCode, string contains,
			CancellationToken cancellationToken = default) =>
			SearchAvailableAsync(departmentId, new DedicatedNumberSearch { CountryCode = countryCode, AreaCode = areaCode, Contains = contains },
				cancellationToken);

		public async Task<List<AvailablePhoneNumber>> SearchAvailableAsync(int departmentId, DedicatedNumberSearch search, CancellationToken cancellationToken = default)
		{
			search ??= new DedicatedNumberSearch();

			var access = await _access.GetAccessAsync(departmentId);
			if (!access.HasActiveAddon)
				throw new DedicatedNumberException(DedicatedNumberErrors.AddonRequired);

			var country = (search.CountryCode ?? "").Trim().ToUpperInvariant();
			if (access.AllowedCountries == null || !access.AllowedCountries.Contains(country))
				throw new DedicatedNumberException(DedicatedNumberErrors.CountryNotAllowed);

			var query = await BuildCarrierSearchAsync(departmentId, country, search, cancellationToken);

			List<AvailablePhoneNumber> numbers;
			try
			{
				numbers = await _carrier.SearchDedicatedNumbersAsync(query, cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, $"Dedicated number search failed for DepartmentId: {departmentId}, country {country}");
				throw new DedicatedNumberException(DedicatedNumberErrors.CarrierUnavailable);
			}

			return (numbers ?? new List<AvailablePhoneNumber>())
				.Where(n => n != null && NormalizeE164(n.PhoneNumber) != null &&
					(string.IsNullOrWhiteSpace(n.CountryCode) || string.Equals(n.CountryCode, country, StringComparison.OrdinalIgnoreCase)))
				.Take(SearchLimit).ToList();
		}

		/// <summary>
		/// The admin's filters, validated and normalized for the carrier. Region, postal code, area code and near-the-department are US/CA
		/// filters and are dropped elsewhere (an area code there narrows the digit pattern, as before). Bad input is InvalidNumber.
		/// </summary>
		private async Task<DedicatedNumberSearch> BuildCarrierSearchAsync(int departmentId, string country, DedicatedNumberSearch search,
			CancellationToken cancellationToken)
		{
			var nanp = DedicatedNumberSearch.IsNanp(country);

			var area = (search.AreaCode ?? "").Trim();
			var pattern = (search.Contains ?? "").Trim();
			if (area.Length > 6 || !area.All(IsAsciiDigit) || pattern.Length == 1 || pattern.Length > 10 || !pattern.All(c => IsAsciiDigit(c) || c == '*'))
				throw new DedicatedNumberException(DedicatedNumberErrors.InvalidNumber);

			var locality = Regex.Replace((search.Locality ?? "").Trim(), @"\s+", " ");
			if (locality.Length > DedicatedNumberSearch.MaxLocalityLength || !locality.All(c => char.IsLetter(c) || c == ' ' || c == '-' || c == '\'' || c == '.'))
				throw new DedicatedNumberException(DedicatedNumberErrors.InvalidNumber);

			string region = null, postalCode = null;
			double? latitude = null, longitude = null;
			int? distance = null;
			if (nanp)
			{
				if (!string.IsNullOrWhiteSpace(search.Region))
					region = DedicatedNumberSearch.NormalizeRegion(country, search.Region) ?? throw new DedicatedNumberException(DedicatedNumberErrors.InvalidNumber);

				if (!string.IsNullOrWhiteSpace(search.PostalCode))
					postalCode = NormalizePostalCode(country, search.PostalCode) ?? throw new DedicatedNumberException(DedicatedNumberErrors.InvalidNumber);

				if (search.NearDepartment)
				{
					var point = await DepartmentLocationAsync(departmentId, cancellationToken) ??
						throw new DedicatedNumberException(DedicatedNumberErrors.DepartmentLocationUnknown);
					latitude = point.Latitude;
					longitude = point.Longitude;
					distance = Math.Clamp(search.DistanceMiles ?? DedicatedNumberSearch.DefaultDistanceMiles, 1, DedicatedNumberSearch.MaxDistanceMiles);
				}
			}

			return new DedicatedNumberSearch
			{
				CountryCode = country,
				Region = region,
				Locality = locality.Length == 0 ? null : locality,
				PostalCode = postalCode,
				AreaCode = area.Length == 0 ? null : area,
				Contains = pattern.Length == 0 ? null : pattern,
				NearDepartment = latitude.HasValue,
				NearLatitude = latitude,
				NearLongitude = longitude,
				DistanceMiles = distance,
				Limit = SearchLimit
			};
		}

		/// <summary>"89501-1234" → "89501"; "k1a0b1" → "K1A 0B1"; "K1A" (a Canadian forward sortation area) stays. Null when it is not one.</summary>
		public static string NormalizePostalCode(string countryCode, string postalCode)
		{
			var value = Regex.Replace((postalCode ?? "").Trim().ToUpperInvariant(), @"\s+", " ");
			switch ((countryCode ?? "").Trim().ToUpperInvariant())
			{
				case "US":
					var us = UsPostalCode.Match(value);
					return us.Success ? us.Groups[1].Value : null;
				case "CA":
					var ca = CanadaPostalCode.Match(value);
					return !ca.Success ? null : ca.Groups[2].Success ? ca.Groups[1].Value + " " + ca.Groups[2].Value : ca.Groups[1].Value;
				default:
					return null;
			}
		}

		/// <summary>
		/// Where "near the department" is: the department's map center (BigBoard center coordinates) when set, else its address geocoded.
		/// Null when neither is known.
		/// </summary>
		private async Task<(double Latitude, double Longitude)?> DepartmentLocationAsync(int departmentId, CancellationToken cancellationToken)
		{
			try
			{
				var center = await _settings.GetBigBoardCenterGpsCoordinatesDepartmentAsync(departmentId);
				var parts = (center ?? "").Split(',');
				if (parts.Length == 2 && double.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) &&
					double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var lon) && ValidPoint(lat, lon))
					return (lat, lon);

				var department = await _departments.GetDepartmentByIdAsync(departmentId, false);
				if (department?.DepartmentId != departmentId)
					return null;

				var address = department.Address;
				if ((address == null || string.IsNullOrWhiteSpace(address.Address1)) && department.AddressId.HasValue)
					address = await _addresses.GetAddressByIdAsync(department.AddressId.Value);

				var text = address == null ? null : string.Join(" ", new[] { address.Address1, address.City, address.State, address.PostalCode, address.Country }
					.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()));
				if (string.IsNullOrWhiteSpace(text))
					return null;

				var geocoded = await _geo.GeocodeAddressAsync(text, cancellationToken);
				if (geocoded?.Outcome == GeocodeOutcome.Found && geocoded.Latitude.HasValue && geocoded.Longitude.HasValue &&
					ValidPoint(geocoded.Latitude.Value, geocoded.Longitude.Value))
					return (geocoded.Latitude.Value, geocoded.Longitude.Value);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, $"Dedicated number search: the location of DepartmentId {departmentId} could not be read.");
			}

			return null;
		}

		private static bool ValidPoint(double latitude, double longitude) =>
			!double.IsNaN(latitude) && !double.IsNaN(longitude) && latitude >= -90 && latitude <= 90 && longitude >= -180 && longitude <= 180 &&
			!(latitude == 0 && longitude == 0);

		private static bool IsAsciiDigit(char c) => c >= '0' && c <= '9';

		public async Task<DepartmentPhoneNumber> ProvisionAsync(int departmentId, string phoneNumber, string userId, CancellationToken cancellationToken = default)
		{
			var e164 = NormalizeE164(phoneNumber);
			if (departmentId <= 0 || e164 == null)
				throw new DedicatedNumberException(DedicatedNumberErrors.InvalidNumber);

			var access = await _access.GetAccessAsync(departmentId);
			if (!access.HasActiveAddon)
				throw new DedicatedNumberException(DedicatedNumberErrors.AddonRequired);

			// A new purchase also needs the active paid entity plan the add-on is sold with (an unreadable plan refuses too; the admin
			// retries): an add-on still inside its paid interval after the plan ended must not buy a number that D10 then releases.
			if (!access.HasBillingRelationship)
				throw new DedicatedNumberException(DedicatedNumberErrors.PlanRequired);

			var country = CountryOf(e164);
			if (country == null || access.AllowedCountries == null || !access.AllowedCountries.Contains(country))
				throw new DedicatedNumberException(DedicatedNumberErrors.CountryNotAllowed);

			// One number per department: a Lapsed number comes back through ReactivateAsync instead of a second purchase.
			if ((await GetAllForDepartmentAsync(departmentId)).Any(n => n.IsHeld))
				throw new DedicatedNumberException(DedicatedNumberErrors.AlreadyHasNumber);
			if ((await _repository.GetByNumberAsync(e164, HeldStatuses, cancellationToken))?.Any() == true)
				throw new DedicatedNumberException(DedicatedNumberErrors.NumberUnavailable);

			var now = DateTime.UtcNow;
			var row = new DepartmentPhoneNumber
			{
				DepartmentPhoneNumberId = Guid.NewGuid().ToString(),
				DepartmentId = departmentId,
				PhoneNumber = e164,
				Provider = "Twilio",
				CountryCode = country,
				SmsCapable = true,
				VoiceCapable = true,
				Status = (int)DepartmentPhoneNumberStatuses.Provisioning,
				SmsIntakeEnabled = true,
				VoiceIntakeEnabled = false,
				ProvisionedByUserId = userId,
				CreatedOn = now
			};

			// The Provisioning row claims the number before anything is bought (UX_DepartmentPhoneNumbers_Number), so a failed or crashed
			// purchase is visible and two departments can never buy the same number.
			try
			{
				await _repository.InsertAsync(row, cancellationToken, true);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception)
			{
				if ((await _repository.GetByNumberAsync(e164, HeldStatuses, CancellationToken.None))?.Any(r => r.DepartmentPhoneNumberId != row.DepartmentPhoneNumberId) == true)
					throw new DedicatedNumberException(DedicatedNumberErrors.NumberUnavailable);
				throw;
			}

			// Two admins buying at once: both rows exist now, and only the earliest claim proceeds to the carrier.
			var claims = (await GetAllForDepartmentAsync(departmentId)).Where(n => n.IsHeld).ToList();
			var winner = claims.OrderBy(n => n.CreatedOn).ThenBy(n => n.DepartmentPhoneNumberId, StringComparer.Ordinal).FirstOrDefault();
			if (winner != null && winner.DepartmentPhoneNumberId != row.DepartmentPhoneNumberId)
			{
				await FailAsync(row, "Another number request for the department was already in progress.", CancellationToken.None);
				throw new DedicatedNumberException(DedicatedNumberErrors.AlreadyHasNumber);
			}

			CarrierNumberPurchase purchase;
			try
			{
				purchase = await _carrier.PurchaseDedicatedNumberAsync(e164, country, await FriendlyNameAsync(departmentId), cancellationToken);
			}
			catch (Exception ex)
			{
				purchase = new CarrierNumberPurchase { Error = "The carrier purchase failed: " + ex.GetType().Name };
			}

			if (purchase == null || !purchase.Success || string.IsNullOrWhiteSpace(purchase.ProviderNumberId))
			{
				var error = purchase?.Error ?? "The carrier returned no number.";
				Framework.Logging.LogError($"Dedicated number purchase failed for DepartmentId: {departmentId}, number {e164}: {error}");
				await FailAsync(row, error, CancellationToken.None);
				return row;
			}

			row.Status = (int)DepartmentPhoneNumberStatuses.Active;
			row.ProviderNumberId = purchase.ProviderNumberId;
			row.Provider = string.IsNullOrWhiteSpace(purchase.Provider) ? row.Provider : purchase.Provider;
			row.PhoneNumber = NormalizeE164(purchase.PhoneNumber) ?? e164;
			row.SmsCapable = purchase.SmsCapable;
			row.VoiceCapable = purchase.VoiceCapable;
			row.ProvisionedOn = DateTime.UtcNow;
			row.UpdatedOn = row.ProvisionedOn;
			row.LastError = null;

			try
			{
				// The number is bought: record it even if the request is being abandoned.
				await _repository.UpdateAsync(row, CancellationToken.None, true);
			}
			catch (Exception ex)
			{
				// Never keep paying for a number nothing records: give it back, and leave the Provisioning row for worker 80 to fail.
				Framework.Logging.LogException(ex, $"Dedicated number {e164} ({purchase.ProviderNumberId}) was bought for DepartmentId: {departmentId} but could not be recorded; releasing it.");
				await ReleaseAtCarrierAsync(new[] { row }, CancellationToken.None);
				throw;
			}

			await StartRoutingAsync(row, userId);
			return row;
		}

		public async Task<DepartmentPhoneNumber> UpdateIntakeOptionsAsync(int departmentId, bool smsIntakeEnabled, bool voiceIntakeEnabled, string userId,
			CancellationToken cancellationToken = default)
		{
			var row = await GetActiveForDepartmentAsync(departmentId) ?? throw new DedicatedNumberException(DedicatedNumberErrors.NoActiveNumber);

			row.SmsIntakeEnabled = smsIntakeEnabled;
			row.VoiceIntakeEnabled = voiceIntakeEnabled;
			row.UpdatedOn = DateTime.UtcNow;
			await _repository.UpdateAsync(row, cancellationToken, true);

			// SMS intake is the SMS channel; the voice channel also carries the member IVR, so the voice switch is read from the row by intake.
			if (smsIntakeEnabled)
				await EnsureChannelAsync(row, IntakeChannelTypes.Sms, userId);
			else
				await DisableChannelAsync(row, IntakeChannelTypes.Sms);

			return row;
		}

		public async Task MarkLapsedAsync(int departmentId, DateTime releaseAfterUtc, CancellationToken cancellationToken = default)
		{
			foreach (var row in (await GetAllForDepartmentAsync(departmentId)).Where(n => n.Status == (int)DepartmentPhoneNumberStatuses.Active))
				await LapseAsync(row, releaseAfterUtc, cancellationToken);
		}

		public async Task<bool> ReleaseAsync(string departmentPhoneNumberId, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(departmentPhoneNumberId))
				return false;

			var row = await _repository.GetByIdAsync(departmentPhoneNumberId);
			if (row == null)
				return false;

			return await ReleaseRowAsync(row, cancellationToken);
		}

		public async Task<DepartmentPhoneNumber> ReactivateAsync(int departmentId, string userId, CancellationToken cancellationToken = default)
		{
			var rows = await GetAllForDepartmentAsync(departmentId);
			var active = rows.FirstOrDefault(n => n.Status == (int)DepartmentPhoneNumberStatuses.Active);
			if (active != null)
				return active;

			var lapsed = rows.FirstOrDefault(n => n.Status == (int)DepartmentPhoneNumberStatuses.Lapsed);
			if (lapsed == null)
				return null;

			// D10: paying again means the add-on and the plan it needs. A plan known not to be one would only lapse the number again with a
			// fresh T0 (a later release date), so it is refused; an unreadable plan does not block the admin.
			var state = await _access.GetPaymentStateAsync(departmentId) ?? new DedicatedNumberPaymentState();
			if (state.AddonActive != true)
				throw new DedicatedNumberException(DedicatedNumberErrors.AddonRequired);
			if (state.PaidEntityPlan == false)
				throw new DedicatedNumberException(DedicatedNumberErrors.PlanRequired);

			await RestoreAsync(lapsed, userId, cancellationToken);
			return lapsed;
		}

		public async Task<int> ReleaseAtCarrierAsync(IReadOnlyCollection<DepartmentPhoneNumber> numbers, CancellationToken cancellationToken = default)
		{
			var released = 0;
			foreach (var number in (numbers ?? Array.Empty<DepartmentPhoneNumber>()).Where(n => n != null && n.IsHeld && !string.IsNullOrWhiteSpace(n.ProviderNumberId)))
			{
				try
				{
					if (await _carrier.ReleaseDedicatedNumberAsync(number.ProviderNumberId, cancellationToken))
					{
						released++;
						Framework.Logging.LogInfo($"Dedicated number {number.PhoneNumber} ({number.ProviderNumberId}) of DepartmentId {number.DepartmentId} released at the carrier.");
					}
					else
					{
						Framework.Logging.LogError($"Dedicated number {number.PhoneNumber} ({number.ProviderNumberId}) of DepartmentId {number.DepartmentId} could not be released at the carrier; release it by hand.");
					}
				}
				catch (Exception ex)
				{
					Framework.Logging.LogException(ex, $"Dedicated number {number.PhoneNumber} ({number.ProviderNumberId}) of DepartmentId {number.DepartmentId} could not be released at the carrier; release it by hand.");
				}
			}

			return released;
		}

		public async Task<int> ProcessLifecycleAsync(DateTime nowUtc, CancellationToken cancellationToken = default)
		{
			// Only departments that hold a number are ever asked about their add-on (never an all-department sweep).
			var held = await _repository.GetByStatusesAsync(HeldStatuses, cancellationToken) ?? new List<DepartmentPhoneNumber>();
			var changed = 0;

			foreach (var department in held.Where(n => n != null).GroupBy(n => n.DepartmentId))
			{
				cancellationToken.ThrowIfCancellationRequested();
				try
				{
					changed += await ProcessDepartmentAsync(department.Key, department.ToList(), nowUtc, cancellationToken);
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					Framework.Logging.LogException(ex, $"Dedicated number lifecycle failed for DepartmentId: {department.Key}");
				}
			}

			return changed;
		}

		private async Task<int> ProcessDepartmentAsync(int departmentId, List<DepartmentPhoneNumber> rows, DateTime nowUtc, CancellationToken cancellationToken)
		{
			var changed = 0;

			// A request that crashed between claiming the number and the carrier answering. The carrier may still have sold it, so say so.
			foreach (var stale in rows.Where(n => n.Status == (int)DepartmentPhoneNumberStatuses.Provisioning &&
				n.CreatedOn < nowUtc.AddMinutes(-Math.Max(1, DedicatedNumberAddonConfig.ProvisioningTimeoutMinutes))))
			{
				Framework.Logging.LogError($"Dedicated number {stale.PhoneNumber} for DepartmentId {departmentId} never finished provisioning; check the carrier account for it.");
				await FailAsync(stale, "Provisioning did not complete; check the carrier account for this number before buying it again.", cancellationToken);
				changed++;
			}

			var routing = rows.Where(n => n.Status == (int)DepartmentPhoneNumberStatuses.Active || n.Status == (int)DepartmentPhoneNumberStatuses.Lapsed).ToList();
			if (routing.Count == 0)
				return changed;

			// One billing read per department. Unknown (billing unconfigured or unreachable, an unreadable plan) changes nothing at all:
			// no T0, no lapse, no release, no restore, no warning.
			var state = await _access.GetPaymentStateAsync(departmentId) ?? new DedicatedNumberPaymentState();
			if (state.IsUnknown)
				return changed;

			var active = routing.Where(n => n.Status == (int)DepartmentPhoneNumberStatuses.Active).ToList();
			var lapsed = routing.Where(n => n.Status == (int)DepartmentPhoneNumberStatuses.Lapsed).OrderByDescending(n => n.CreatedOn).ToList();

			if (state.Paying)
			{
				foreach (var row in active.Where(HasStopMarkers))
				{
					ClearStopMarkers(row);
					row.UpdatedOn = nowUtc;
					await _repository.UpdateAsync(row, cancellationToken, true);
				}

				// Paying again before the number was released (even past its release date, while the carrier still holds it).
				if (active.Count == 0 && lapsed.Count > 0)
				{
					await RestoreAsync(lapsed[0], lapsed[0].ProvisionedByUserId, cancellationToken);
					changed++;
				}

				return changed;
			}

			// Stopped paying (D10). T0 is the first sweep that saw it; intake stops at T0 + LapseDelayHours, release at T0 + ReleaseGraceDays.
			foreach (var row in active)
			{
				if (!row.AddonInactiveSince.HasValue)
				{
					row.AddonInactiveSince = nowUtc;
					row.UpdatedOn = nowUtc;
					await _repository.UpdateAsync(row, cancellationToken, true);
					Framework.Logging.LogInfo($"DepartmentId {departmentId} stopped paying for dedicated number {row.PhoneNumber} " +
						$"({(state.PlanStopped ? "plan" : "add-on")}); intake stops after {nowUtc + DedicatedNumberReleaseSchedule.LapseDelay:u}.");
				}

				if (nowUtc - row.AddonInactiveSince.Value >= DedicatedNumberReleaseSchedule.LapseDelay)
				{
					await LapseAsync(row, row.AddonInactiveSince.Value + DedicatedNumberReleaseSchedule.Grace, cancellationToken);
					changed++;
				}
			}

			foreach (var row in lapsed)
			{
				// A number lapsed before T0 was recorded: its stop is now, and an existing release date is kept.
				if (!row.AddonInactiveSince.HasValue || !row.ReleaseAfter.HasValue)
				{
					row.AddonInactiveSince ??= nowUtc;
					row.ReleaseAfter ??= row.AddonInactiveSince.Value + DedicatedNumberReleaseSchedule.Grace;
					row.UpdatedOn = nowUtc;
					await _repository.UpdateAsync(row, cancellationToken, true);
				}

				if (row.ReleaseAfter.Value <= nowUtc && await ReleaseRowAsync(row, cancellationToken))
					changed++;
			}

			// D12: the admins hear about every number still held (a released one needs no warning).
			foreach (var row in active.Concat(lapsed).Where(n => n.IsHeld))
				await WarnAsync(row, state, nowUtc, cancellationToken);

			return changed;
		}

		/// <summary>
		/// D12: sends the latest release warning that is due and not yet sent: "payment stopped" at T0 (a plan-only stop after
		/// <see cref="PlanStopConfirmation"/>), then each <see cref="DedicatedNumberAddonConfig.ReleaseWarningDays"/> stage before release.
		/// Earlier due stages are marked sent without sending (a late sweep sends one message). Flags are saved only after an admin was
		/// reached, so a failed send is retried by the next sweep and a sent stage never goes twice.
		/// </summary>
		private async Task WarnAsync(DepartmentPhoneNumber row, DedicatedNumberPaymentState state, DateTime nowUtc, CancellationToken cancellationToken)
		{
			try
			{
				// Past its release date the number is only waiting for the carrier to take it back (a refused release is retried).
				var schedule = DedicatedNumberReleaseSchedule.For(row);
				if (schedule?.StoppedOn == null || schedule.ReleaseOn <= nowUtc)
					return;

				var sent = (DedicatedNumberReleaseWarnings)row.ReleaseWarningsSent;
				var stoppedDue = schedule.StoppedOn.Value + (state.PlanStopped ? PlanStopConfirmation : TimeSpan.Zero);
				var stages = new List<(DedicatedNumberReleaseWarnings Stage, int Days, DateTime DueOn, int Order)>
				{
					(DedicatedNumberReleaseWarnings.Stopped, 0, stoppedDue, 0)
				};
				var order = 1;
				foreach (var (stage, days) in DedicatedNumberReleaseSchedule.DayStages(DedicatedNumberAddonConfig.ReleaseWarningDays))
				{
					// A stage never goes before "payment stopped" (a short grace period puts some stages before T0).
					var due = schedule.ReleaseOn.AddDays(-days);
					stages.Add((stage, days, due < stoppedDue ? stoppedDue : due, order++));
				}

				var dueStages = stages.Where(s => s.DueOn <= nowUtc && (sent & s.Stage) == 0).ToList();
				if (dueStages.Count == 0)
					return;

				var latest = dueStages.OrderByDescending(s => s.DueOn).ThenBy(s => s.Order).First();

				var department = await _departments.GetDepartmentByIdAsync(row.DepartmentId, false);
				if (department?.DepartmentId != row.DepartmentId)
					return;

				var (title, text) = latest.Stage == DedicatedNumberReleaseWarnings.Stopped
					? StoppedWarning(department, row, schedule, state, nowUtc)
					: ReleaseWarning(department, row, schedule, state, nowUtc);

				if (!await NotifyAdminsAsync(department, title, text))
					return;

				foreach (var stage in dueStages)
					sent |= stage.Stage;
				row.ReleaseWarningsSent = (int)sent;
				row.LastReleaseWarningOn = nowUtc;
				row.UpdatedOn = nowUtc;
				await _repository.UpdateAsync(row, CancellationToken.None, true);
				Framework.Logging.LogInfo($"Dedicated number {row.PhoneNumber} of DepartmentId {row.DepartmentId}: release warning {latest.Stage} sent.");
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, $"Dedicated number release warning failed for DepartmentId: {row.DepartmentId}");
			}
		}

		public async Task<bool> NotifyCancellationScheduledAsync(int departmentId, DedicatedNumberCancellations cancelled, DateTime? endsOnUtc,
			CancellationToken cancellationToken = default)
		{
			try
			{
				var now = DateTime.UtcNow;
				// An end that is already here is the lifecycle's "payment stopped" warning, which follows within one sweep.
				if (endsOnUtc.HasValue && endsOnUtc.Value <= now.Add(PlanStopConfirmation))
					return false;

				var number = (await GetAllForDepartmentAsync(departmentId)).FirstOrDefault(n =>
					n.Status == (int)DepartmentPhoneNumberStatuses.Active || n.Status == (int)DepartmentPhoneNumberStatuses.Lapsed);
				if (number == null)
					return false;

				var department = await _departments.GetDepartmentByIdAsync(departmentId, false);
				if (department?.DepartmentId != departmentId)
					return false;

				var (title, text) = CancellationNotice(department, number, cancelled, endsOnUtc);
				return await NotifyAdminsAsync(department, title, text);
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, $"Dedicated number cancellation notice failed for DepartmentId: {departmentId}");
				return false;
			}
		}

		/// <summary>
		/// The department's active admins (deleted, disabled and hidden members excluded by the department service), each once. True when
		/// at least one was reached on some channel (an email, text, push or chat that went out), the only result that counts as sent.
		/// </summary>
		private async Task<bool> NotifyAdminsAsync(Department department, string title, string text)
		{
			var admins = await _departments.GetActiveAdminsForDepartmentAsync(department.DepartmentId) ?? new List<IdentityUser>();
			var delivered = false;
			foreach (var userId in admins.Where(a => a != null && !string.IsNullOrWhiteSpace(a.UserId)).Select(a => a.UserId).Distinct(StringComparer.OrdinalIgnoreCase))
			{
				try
				{
					// No department number: notices travel from the master Resgrid number (dedicated numbers are inbound only, D5).
					delivered |= await _communication.SendNotificationDeliveredAsync(userId, department.DepartmentId, text, null, department, title, null, false, null);
				}
				catch (Exception ex)
				{
					Framework.Logging.LogException(ex, $"Dedicated number notice to an admin of DepartmentId {department.DepartmentId} failed.");
				}
			}

			return delivered;
		}

		// Outbound notices are English by design (outbound-message localization gap); dates are in the department's time zone.

		private static (string Title, string Text) StoppedWarning(Department department, DepartmentPhoneNumber row, DedicatedNumberReleaseSchedule schedule,
			DedicatedNumberPaymentState state, DateTime nowUtc)
		{
			var lapse = schedule.IntakeStopsOn ?? nowUtc;
			var intake = schedule.IntakeStopped || lapse <= nowUtc
				? $"Texts and calls to {row.PhoneNumber} stopped on {LocalDateTime(lapse, department)}"
				: $"Texts and calls to {row.PhoneNumber} stop on {LocalDateTime(lapse, department)}";

			if (state.PlanStopped)
				return ("Private Inbound Dispatch Number: plan payment stopped",
					$"{department.Name}: the department's Resgrid plan is no longer an active paid plan, which the Private Inbound Dispatch Number add-on needs. " +
					$"{intake} and the number is released on {LocalDate(schedule.ReleaseOn, department)}. Renew your plan before then to keep it.");

			return ("Private Inbound Dispatch Number payment stopped",
				$"{department.Name}: payment for the Private Inbound Dispatch Number add-on has stopped (cancelled or unpaid). " +
				$"{intake} and the number is released on {LocalDate(schedule.ReleaseOn, department)}. Renew the add-on (and your plan) before then to keep it.");
		}

		private static (string Title, string Text) ReleaseWarning(Department department, DepartmentPhoneNumber row, DedicatedNumberReleaseSchedule schedule,
			DedicatedNumberPaymentState state, DateTime nowUtc)
		{
			var days = Math.Max(1, (int)Math.Ceiling((schedule.ReleaseOn - nowUtc).TotalDays));
			var inDays = days == 1 ? "1 day" : $"{days} days";
			var reason = state.PlanStopped
				? "the department's Resgrid plan is no longer an active paid plan, which the Private Inbound Dispatch Number add-on needs"
				: "payment for the Private Inbound Dispatch Number add-on has stopped";
			var lapse = schedule.IntakeStopsOn;
			var intake = lapse == null ? "Texts and calls to it have stopped. "
				: schedule.IntakeStopped || lapse.Value <= nowUtc ? $"Texts and calls to it stopped on {LocalDateTime(lapse.Value, department)}. "
				: $"Texts and calls to it stop on {LocalDateTime(lapse.Value, department)}. ";

			return ($"Private Inbound Dispatch Number release in {inDays}",
				$"{department.Name}: the private inbound dispatch number {row.PhoneNumber} will be released on {LocalDate(schedule.ReleaseOn, department)}, in {inDays}, " +
				$"because {reason}. {intake}" +
				"Renew the add-on (and your plan) before then to keep the number; once released it goes back to the carrier and may not be available again.");
		}

		private static (string Title, string Text) CancellationNotice(Department department, DepartmentPhoneNumber row, DedicatedNumberCancellations cancelled,
			DateTime? endsOnUtc)
		{
			var plan = cancelled == DedicatedNumberCancellations.Plan;
			var what = plan
				? $"{department.Name}: the department's Resgrid plan was cancelled. The Private Inbound Dispatch Number add-on needs an active paid plan, so "
				: $"{department.Name}: the Private Inbound Dispatch Number add-on renewal was cancelled. ";
			var renew = plan ? "Renew the plan before then to keep the number." : "Renew the add-on before then to keep the number.";
			var title = plan ? "Private Inbound Dispatch Number: plan cancelled" : "Private Inbound Dispatch Number add-on cancelled";

			if (endsOnUtc.HasValue)
			{
				var end = endsOnUtc.Value;
				var schedule = $"texts and calls to {row.PhoneNumber} stop on {LocalDateTime(end + DedicatedNumberReleaseSchedule.LapseDelay, department)} " +
					$"and the number is released on {LocalDate(end + DedicatedNumberReleaseSchedule.Grace, department)}. ";
				return (title, plan
					? $"{what}when the plan ends on {LocalDate(end, department)}, {schedule}{renew}"
					: $"{what}The add-on ends on {LocalDate(end, department)}; {schedule}{renew}");
			}

			var hours = Math.Max(0, DedicatedNumberAddonConfig.LapseDelayHours);
			var grace = Math.Max(0, DedicatedNumberAddonConfig.ReleaseGraceDays);
			var unknown = $"texts and calls to {row.PhoneNumber} stop {hours} hours after it ends and the number is released {grace} days after it ends. ";
			return (title, plan
				? $"{what}when the plan ends, {unknown}{renew}"
				: $"{what}When the paid period ends, {unknown}{renew}");
		}

		private static string LocalDate(DateTime utc, Department department) =>
			utc.TimeConverter(department).ToString("MMMM d, yyyy", CultureInfo.InvariantCulture);

		private static string LocalDateTime(DateTime utc, Department department) =>
			utc.TimeConverter(department).ToString("MMMM d, yyyy, HH:mm", CultureInfo.InvariantCulture);

		private static bool HasStopMarkers(DepartmentPhoneNumber row) =>
			row.AddonInactiveSince.HasValue || row.ReleaseWarningsSent != 0 || row.LastReleaseWarningOn.HasValue || row.ReleaseAfter.HasValue;

		/// <summary>Paying again: T0, the release date and the warnings of that unpaid period all go.</summary>
		private static void ClearStopMarkers(DepartmentPhoneNumber row)
		{
			row.AddonInactiveSince = null;
			row.ReleaseAfter = null;
			row.ReleaseWarningsSent = 0;
			row.LastReleaseWarningOn = null;
		}

		private async Task LapseAsync(DepartmentPhoneNumber row, DateTime releaseAfterUtc, CancellationToken cancellationToken)
		{
			row.Status = (int)DepartmentPhoneNumberStatuses.Lapsed;
			row.ReleaseAfter = releaseAfterUtc;
			row.AddonInactiveSince ??= DateTime.UtcNow;
			row.UpdatedOn = DateTime.UtcNow;
			await _repository.UpdateAsync(row, cancellationToken, true);
			Framework.Logging.LogInfo($"Dedicated number {row.PhoneNumber} of DepartmentId {row.DepartmentId} lapsed; release after {releaseAfterUtc:u}.");

			await StopRoutingAsync(row);
		}

		private async Task RestoreAsync(DepartmentPhoneNumber row, string userId, CancellationToken cancellationToken)
		{
			row.Status = (int)DepartmentPhoneNumberStatuses.Active;
			ClearStopMarkers(row);
			row.UpdatedOn = DateTime.UtcNow;
			await _repository.UpdateAsync(row, cancellationToken, true);
			Framework.Logging.LogInfo($"Dedicated number {row.PhoneNumber} of DepartmentId {row.DepartmentId} reactivated.");

			await StartRoutingAsync(row, userId);
		}

		private async Task<bool> ReleaseRowAsync(DepartmentPhoneNumber row, CancellationToken cancellationToken)
		{
			if (row.Status == (int)DepartmentPhoneNumberStatuses.Released)
				return true;

			// A failed purchase holds nothing at the carrier.
			if (row.Status == (int)DepartmentPhoneNumberStatuses.Failed && string.IsNullOrWhiteSpace(row.ProviderNumberId))
				return true;

			if (!string.IsNullOrWhiteSpace(row.ProviderNumberId))
			{
				bool released;
				try
				{
					released = await _carrier.ReleaseDedicatedNumberAsync(row.ProviderNumberId, cancellationToken);
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					Framework.Logging.LogException(ex, $"Dedicated number {row.PhoneNumber} release failed for DepartmentId: {row.DepartmentId}");
					released = false;
				}

				if (!released)
				{
					// Nothing else changes: the number still routes (or stays held) and worker 80 retries a lapsed one.
					row.LastError = "The carrier did not release the number; it will be retried.";
					row.UpdatedOn = DateTime.UtcNow;
					await _repository.UpdateAsync(row, CancellationToken.None, true);
					return false;
				}
			}

			row.Status = (int)DepartmentPhoneNumberStatuses.Released;
			row.ReleasedOn = DateTime.UtcNow;
			row.ReleaseAfter = null;
			row.LastError = null;
			row.UpdatedOn = row.ReleasedOn;
			await _repository.UpdateAsync(row, CancellationToken.None, true);
			Framework.Logging.LogInfo($"Dedicated number {row.PhoneNumber} of DepartmentId {row.DepartmentId} released.");

			await StopRoutingAsync(row);
			return true;
		}

		private async Task FailAsync(DepartmentPhoneNumber row, string error, CancellationToken cancellationToken)
		{
			row.Status = (int)DepartmentPhoneNumberStatuses.Failed;
			row.LastError = Truncate(error, 1000);
			row.UpdatedOn = DateTime.UtcNow;
			await _repository.UpdateAsync(row, cancellationToken, true);
		}

		/// <summary>The Active number takes over TextToCallNumber and its intake channels. Each step is best effort: the number is already bought.</summary>
		private async Task StartRoutingAsync(DepartmentPhoneNumber row, string userId)
		{
			try
			{
				var current = await _settings.GetTextToCallNumberForDepartmentAsync(row.DepartmentId);
				if (!string.IsNullOrWhiteSpace(current) && Digits(current) != Digits(row.PhoneNumber))
					Framework.Logging.LogInfo($"DepartmentId {row.DepartmentId} TextToCallNumber {current} is replaced by its dedicated number {row.PhoneNumber}.");

				// The legacy setting holds digits only (NumbersService.ProvisionNumberAsync); every existing reader expects that shape.
				await _settings.SaveOrUpdateSettingAsync(row.DepartmentId, Digits(row.PhoneNumber), DepartmentSettingTypes.TextToCallNumber);
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, $"Dedicated number {row.PhoneNumber}: TextToCallNumber could not be set for DepartmentId: {row.DepartmentId}");
			}

			if (row.SmsIntakeEnabled)
				await EnsureChannelAsync(row, IntakeChannelTypes.Sms, userId);
			await EnsureChannelAsync(row, IntakeChannelTypes.Voice, userId);
		}

		/// <summary>Intake stops: both channels off, and TextToCallNumber cleared while it still names this number.</summary>
		private async Task StopRoutingAsync(DepartmentPhoneNumber row)
		{
			await DisableChannelAsync(row, IntakeChannelTypes.Sms);
			await DisableChannelAsync(row, IntakeChannelTypes.Voice);

			try
			{
				var current = await _settings.GetTextToCallNumberForDepartmentAsync(row.DepartmentId);
				if (!string.IsNullOrWhiteSpace(current) && Digits(current) == Digits(row.PhoneNumber))
					await _settings.DeleteSettingAsync(row.DepartmentId, DepartmentSettingTypes.TextToCallNumber);
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, $"Dedicated number {row.PhoneNumber}: TextToCallNumber could not be cleared for DepartmentId: {row.DepartmentId}");
			}
		}

		private async Task EnsureChannelAsync(DepartmentPhoneNumber row, IntakeChannelTypes type, string userId)
		{
			try
			{
				await _intakeChannels.EnsureChannelAsync(row.DepartmentId, type, row.PhoneNumber, ChannelName(type, row.PhoneNumber), userId);
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, $"Dedicated number {row.PhoneNumber}: the {type} intake channel could not be ensured for DepartmentId: {row.DepartmentId}");
			}
		}

		private async Task DisableChannelAsync(DepartmentPhoneNumber row, IntakeChannelTypes type)
		{
			try
			{
				await _intakeChannels.DisableChannelsForAddressAsync(row.DepartmentId, type, row.PhoneNumber);
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, $"Dedicated number {row.PhoneNumber}: the {type} intake channel could not be disabled for DepartmentId: {row.DepartmentId}");
			}
		}

		private async Task<string> FriendlyNameAsync(int departmentId)
		{
			try
			{
				var department = await _departments.GetDepartmentByIdAsync(departmentId, false);
				if (department?.DepartmentId == departmentId && !string.IsNullOrWhiteSpace(department.Name))
					return Truncate($"Resgrid {departmentId} {department.Name}", 64);
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, $"Dedicated number: department name unavailable for DepartmentId: {departmentId}");
			}

			return $"Resgrid {departmentId}";
		}

		/// <summary>The ISO country of an E.164 number, or null when it does not parse as a valid number.</summary>
		private string CountryOf(string e164)
		{
			try
			{
				var parsed = _phoneNumbers.Process(e164);
				return parsed != null && parsed.IsValid && !string.IsNullOrWhiteSpace(parsed.Region) ? parsed.Region.Trim().ToUpperInvariant() : null;
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, "Dedicated number country could not be read.");
				return null;
			}
		}

		/// <summary>Intake channel names are stored data, read by department admins next to the channel's address.</summary>
		private static string ChannelName(IntakeChannelTypes type, string phoneNumber) =>
			type == IntakeChannelTypes.Voice ? "Dispatch number voice " + phoneNumber : "Dispatch number SMS " + phoneNumber;

		/// <summary>"+1 (775) 555-0100" → "+17755550100". Null unless it has 8 to 15 digits (E.164 allows at most 15).</summary>
		public static string NormalizeE164(string phoneNumber)
		{
			if (string.IsNullOrWhiteSpace(phoneNumber))
				return null;

			var digits = Digits(phoneNumber);
			return digits.Length >= 8 && digits.Length <= 15 && digits[0] != '0' ? "+" + digits : null;
		}

		private static string Digits(string value)
		{
			var builder = new StringBuilder(value?.Length ?? 0);
			foreach (var c in value ?? "")
				if (c >= '0' && c <= '9')
					builder.Append(c);
			return builder.ToString();
		}

		private static string Truncate(string value, int length) =>
			string.IsNullOrEmpty(value) || value.Length <= length ? value : value.Substring(0, length);
	}
}
