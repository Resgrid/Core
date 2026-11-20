using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Providers
{
	public interface INumberProvider
	{
		Task<List<TextNumber>> GetAvailableNumbers(string country, string areaCode);
		Task<bool> ProvisionNumber(string country, string number);
		string ConvertCountryToCode(string country);

		/// <summary>
		/// Dedicated Number add-on: numbers the carrier offers in the ISO country (local and mobile), optionally in a NANP area code or
		/// containing digits, that can take both SMS and voice. Empty when the carrier offers none; throws when the carrier cannot be asked.
		/// </summary>
		Task<List<AvailablePhoneNumber>> SearchDedicatedNumbersAsync(string countryCode, string areaCode, string contains, int limit,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Dedicated Number add-on (plan §8 D13): local and mobile numbers in the search's country that take both SMS and voice, filtered by
		/// region, locality, postal code, area code, digits and a near point with radius (region, postal code, area code and near are US/CA
		/// only). Results carry locality, region, postal code and number type. Empty when the carrier offers none; throws when it cannot be asked.
		/// </summary>
		Task<List<AvailablePhoneNumber>> SearchDedicatedNumbersAsync(DedicatedNumberSearch search, CancellationToken cancellationToken = default);

		/// <summary>
		/// Dedicated Number add-on: buys the E.164 number with its SMS and voice webhooks pointed at Resgrid (and the country's regulatory
		/// bundle when one is configured). Never throws for a carrier refusal; the result says why.
		/// </summary>
		Task<CarrierNumberPurchase> PurchaseDedicatedNumberAsync(string phoneNumber, string countryCode, string friendlyName,
			CancellationToken cancellationToken = default);

		/// <summary>Dedicated Number add-on: returns the number to the carrier by its carrier id. True when released or already gone.</summary>
		Task<bool> ReleaseDedicatedNumberAsync(string providerNumberId, CancellationToken cancellationToken = default);
	}

	/// <summary>The outcome of a carrier purchase for a dedicated number.</summary>
	public sealed class CarrierNumberPurchase
	{
		public bool Success { get; set; }

		/// <summary>"Twilio".</summary>
		public string Provider { get; set; }

		/// <summary>The carrier's id for the bought number (Twilio IncomingPhoneNumber SID "PN…").</summary>
		public string ProviderNumberId { get; set; }

		/// <summary>E.164 as the carrier reports it.</summary>
		public string PhoneNumber { get; set; }

		public bool SmsCapable { get; set; }

		public bool VoiceCapable { get; set; }

		/// <summary>Why the purchase failed (the carrier's code and message; never a credential).</summary>
		public string Error { get; set; }
	}
}
