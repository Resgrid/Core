using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Twilio;
using Twilio.Base;
using Twilio.Exceptions;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Rest.Api.V2010.Account.AvailablePhoneNumberCountry;
using Twilio.Types;

namespace Resgrid.Providers.NumberProvider
{
	public class TwilioProvider : INumberProvider
	{
		public async Task<bool> ProvisionNumber(string country, string number)
		{
			TwilioClient.Init(Config.NumberProviderConfig.TwilioAccountSid, Config.NumberProviderConfig.TwilioAuthToken);

			try
			{
				var incomingPhoneNumber = await IncomingPhoneNumberResource.CreateAsync(phoneNumber: new PhoneNumber(number),
																		     smsUrl: new Uri(Config.NumberProviderConfig.TwilioApiUrl),
																			 smsMethod: "GET",
																			 voiceUrl: new Uri(Config.NumberProviderConfig.TwilioVoiceApiUrl),
																			 voiceMethod: "GET");
			}
			catch
			{
				return false;
			}

			return true;
		}

		public async Task<List<TextNumber>> GetAvailableNumbers(string country, string areaCode)
		{
			var availableNumbers = new List<TextNumber>();
			TwilioClient.Init(Config.NumberProviderConfig.TwilioAccountSid, Config.NumberProviderConfig.TwilioAuthToken);
			ResourceSet<LocalResource> numbers;

			//if (country == "US" || country == "CA" || country == "GB")
			//{
			//	if (!string.IsNullOrWhiteSpace(areaCode))
			//		numbers = await LocalResource.ReadAsync(country, areaCode: int.Parse(areaCode), smsEnabled: true);
			//	else
			//		numbers = await LocalResource.ReadAsync(country, smsEnabled: true);

			//	if (numbers != null)
			//	{
			//		foreach (var number in numbers)
			//		{
			//			var textNumber = new TextNumber();
			//			textNumber.msisdn = number.PhoneNumber.ToString();
			//			textNumber.country = number.IsoCountry;
			//			textNumber.Number = number.FriendlyName.ToString();

			//			availableNumbers.Add(textNumber);
			//		}
			//	}
			//}
			//else
			//{
				ResourceSet<MobileResource> mobileNumbers;

				if (!string.IsNullOrWhiteSpace(areaCode))
					mobileNumbers = await MobileResource.ReadAsync(country, areaCode: int.Parse(areaCode), smsEnabled: true);
				else
					mobileNumbers = await MobileResource.ReadAsync(country, smsEnabled: true);

				if (mobileNumbers != null)
				{
					foreach (var number in mobileNumbers)
					{
						var textNumber = new TextNumber();
						textNumber.msisdn = number.PhoneNumber.ToString();
						textNumber.country = number.IsoCountry;
						textNumber.Number = number.FriendlyName.ToString();

						availableNumbers.Add(textNumber);
					}
				}
			//}

			if (availableNumbers.Count > 10)
				return availableNumbers.Take(10).ToList();

			return availableNumbers;
		}

		public Task<List<AvailablePhoneNumber>> SearchDedicatedNumbersAsync(string countryCode, string areaCode, string contains, int limit,
			CancellationToken cancellationToken = default) =>
			SearchDedicatedNumbersAsync(new DedicatedNumberSearch { CountryCode = countryCode, AreaCode = areaCode, Contains = contains, Limit = limit },
				cancellationToken);

		public async Task<List<AvailablePhoneNumber>> SearchDedicatedNumbersAsync(DedicatedNumberSearch search, CancellationToken cancellationToken = default)
		{
			if (search == null)
				throw new ArgumentNullException(nameof(search));

			var take = Math.Clamp(search.Limit, 1, 50);
			var localOptions = LocalSearchOptions(search, take);
			var mobileOptions = MobileSearchOptions(search, take);

			TwilioClient.Init(Config.NumberProviderConfig.TwilioAccountSid, Config.NumberProviderConfig.TwilioAuthToken);

			var results = new List<AvailablePhoneNumber>();
			Exception failure = null;
			var answered = false;

			// Local numbers first (the only kind in the US); mobile numbers where the country sells them. A number type the country does
			// not offer answers 404, which simply means none.
			try
			{
				var local = await LocalResource.ReadAsync(localOptions);
				results.AddRange(local.Select(n => Map(n.PhoneNumber, n.FriendlyName, n.Locality, n.Region, n.PostalCode, n.IsoCountry, n.Capabilities,
					DedicatedNumberTypes.Local)));
				answered = true;
			}
			catch (ApiException ex) when (ex.Status == 404) { answered = true; }
			catch (Exception ex) { failure = ex; }

			cancellationToken.ThrowIfCancellationRequested();

			if (results.Count < take)
			{
				try
				{
					var mobile = await MobileResource.ReadAsync(mobileOptions);
					results.AddRange(mobile.Select(n => Map(n.PhoneNumber, n.FriendlyName, n.Locality, n.Region, n.PostalCode, n.IsoCountry, n.Capabilities,
						DedicatedNumberTypes.Mobile)));
					answered = true;
				}
				catch (ApiException ex) when (ex.Status == 404) { answered = true; }
				catch (Exception ex) { failure ??= ex; }
			}

			if (!answered && failure != null)
				throw new InvalidOperationException("The carrier number search failed.", failure);

			return results.Where(n => !string.IsNullOrWhiteSpace(n.PhoneNumber) && n.SmsCapable && n.VoiceCapable)
				.GroupBy(n => n.PhoneNumber).Select(g => g.First()).Take(take).ToList();
		}

		/// <summary>The Twilio AvailablePhoneNumbers/Local query for a dedicated number search (built without calling Twilio).</summary>
		public static ReadLocalOptions LocalSearchOptions(DedicatedNumberSearch search, int limit)
		{
			var f = SearchFilters.From(search);
			return new ReadLocalOptions(f.Country)
			{
				AreaCode = f.AreaCode, Contains = f.Contains, SmsEnabled = true, VoiceEnabled = true, InRegion = f.Region, InLocality = f.Locality,
				InPostalCode = f.PostalCode, NearLatLong = f.NearLatLong, Distance = f.Distance, Limit = limit, PageSize = limit
			};
		}

		/// <summary>The Twilio AvailablePhoneNumbers/Mobile query for a dedicated number search (built without calling Twilio).</summary>
		public static ReadMobileOptions MobileSearchOptions(DedicatedNumberSearch search, int limit)
		{
			var f = SearchFilters.From(search);
			return new ReadMobileOptions(f.Country)
			{
				AreaCode = f.AreaCode, Contains = f.Contains, SmsEnabled = true, VoiceEnabled = true, InRegion = f.Region, InLocality = f.Locality,
				InPostalCode = f.PostalCode, NearLatLong = f.NearLatLong, Distance = f.Distance, Limit = limit, PageSize = limit
			};
		}

		/// <summary>
		/// A search as Twilio filters. Area code, region, postal code and near-point filters apply only to the United States and Canada
		/// (Twilio ignores or rejects them elsewhere), so outside NANP they are dropped and an area code narrows the digit pattern instead.
		/// </summary>
		private readonly record struct SearchFilters(string Country, int? AreaCode, string Contains, string Region, string Locality, string PostalCode,
			string NearLatLong, int? Distance)
		{
			public static SearchFilters From(DedicatedNumberSearch search)
			{
				var country = (search?.CountryCode ?? "").Trim().ToUpperInvariant();
				var nanp = DedicatedNumberSearch.IsNanp(country);
				var areaCode = (search?.AreaCode ?? "").Trim();
				int? area = nanp && int.TryParse(areaCode, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedArea) ? parsedArea : null;
				var contains = !string.IsNullOrWhiteSpace(search?.Contains) ? search.Contains.Trim() : !nanp && areaCode.Length > 0 ? areaCode : null;

				string near = null;
				int? distance = null;
				if (nanp && search?.HasNearPoint == true)
				{
					near = search.NearLatitude.Value.ToString("0.######", CultureInfo.InvariantCulture) + "," +
						search.NearLongitude.Value.ToString("0.######", CultureInfo.InvariantCulture);
					distance = Math.Clamp(search.DistanceMiles ?? DedicatedNumberSearch.DefaultDistanceMiles, 1, DedicatedNumberSearch.MaxDistanceMiles);
				}

				return new SearchFilters(country, area, contains,
					nanp ? Blank(search?.Region)?.ToUpperInvariant() : null,
					Blank(search?.Locality),
					nanp ? Blank(search?.PostalCode)?.ToUpperInvariant() : null,
					near, distance);
			}

			private static string Blank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
		}

		public async Task<CarrierNumberPurchase> PurchaseDedicatedNumberAsync(string phoneNumber, string countryCode, string friendlyName,
			CancellationToken cancellationToken = default)
		{
			var result = new CarrierNumberPurchase { Provider = DedicatedNumberCarrierSettings.Provider };
			var smsUrl = DedicatedNumberCarrierSettings.SmsUrl();
			var voiceUrl = DedicatedNumberCarrierSettings.VoiceUrl();
			if (smsUrl == null || voiceUrl == null)
			{
				result.Error = "SystemBehaviorConfig.ResgridApiBaseUrl is not an absolute https URL.";
				return result;
			}

			TwilioClient.Init(Config.NumberProviderConfig.TwilioAccountSid, Config.NumberProviderConfig.TwilioAuthToken);

			try
			{
				var name = string.IsNullOrWhiteSpace(friendlyName) ? null : friendlyName.Trim();
				if (name != null && name.Length > 64)
					name = name.Substring(0, 64);

				var created = await IncomingPhoneNumberResource.CreateAsync(phoneNumber: new PhoneNumber(phoneNumber), friendlyName: name,
					smsUrl: smsUrl, smsMethod: DedicatedNumberCarrierSettings.SmsMethod,
					voiceUrl: voiceUrl, voiceMethod: DedicatedNumberCarrierSettings.VoiceMethod,
					bundleSid: DedicatedNumberCarrierSettings.BundleSidFor(countryCode));

				if (created == null || string.IsNullOrWhiteSpace(created.Sid))
				{
					result.Error = "The carrier returned no number.";
					return result;
				}

				result.Success = true;
				result.ProviderNumberId = created.Sid;
				result.PhoneNumber = created.PhoneNumber?.ToString() ?? phoneNumber;
				result.SmsCapable = created.Capabilities?.Sms != false;
				result.VoiceCapable = created.Capabilities?.Voice != false;
				return result;
			}
			catch (ApiException ex)
			{
				// The carrier's own code and message (number taken, bundle required, …); never the request or credentials.
				result.Error = $"Twilio {ex.Code} (HTTP {ex.Status}): {ex.Message}";
				return result;
			}
			catch (Exception ex)
			{
				result.Error = "The carrier purchase failed: " + ex.GetType().Name;
				return result;
			}
		}

		public async Task<bool> ReleaseDedicatedNumberAsync(string providerNumberId, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(providerNumberId))
				return false;

			TwilioClient.Init(Config.NumberProviderConfig.TwilioAccountSid, Config.NumberProviderConfig.TwilioAuthToken);

			try
			{
				return await IncomingPhoneNumberResource.DeleteAsync(pathSid: providerNumberId);
			}
			catch (ApiException ex) when (ex.Status == 404)
			{
				// Already gone from the account (released by hand or by an earlier attempt).
				return true;
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, $"Twilio release of dedicated number {providerNumberId} failed.");
				return false;
			}
		}

		private static AvailablePhoneNumber Map(PhoneNumber number, PhoneNumber friendlyName, string locality, string region, string postalCode,
			string isoCountry, PhoneNumberCapabilities capabilities, DedicatedNumberTypes type) => new AvailablePhoneNumber
			{
				PhoneNumber = number?.ToString(),
				FriendlyName = friendlyName?.ToString(),
				Locality = locality,
				Region = region,
				PostalCode = postalCode,
				CountryCode = isoCountry,
				NumberType = type,
				SmsCapable = capabilities?.Sms != false,
				VoiceCapable = capabilities?.Voice != false
			};

		public string ConvertCountryToCode(string country)
		{
			switch (country)
			{
				case "United States":
					return "US";
				case "United Kingdom":
					return "GB";
				case "Australia":
					return "AU";
				case "Canada":
					return "CA";

				default:
					throw new Exception("Not supported country code for Twilio numbers.");

			}
		}
	}
}
