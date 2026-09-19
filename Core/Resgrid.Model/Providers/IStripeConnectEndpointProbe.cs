using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Providers
{
	/// <summary>
	/// Asks Stripe, with the platform key, whether a webhook endpoint exists for this cluster (plan B2.5a). Separated
	/// from the service so the health logic is testable without the Stripe SDK.
	/// </summary>
	public interface IStripeConnectEndpointProbe
	{
		/// <summary>
		/// True when an enabled endpoint at <paramref name="expectedUrl"/> exists in the given live/test mode and its
		/// enabled events include every entry of <paramref name="requiredEvents"/> (or "*"); false when no such endpoint
		/// is listed; null when the platform key is missing or Stripe could not be reached.
		/// </summary>
		Task<bool?> IsEndpointRegisteredAsync(string expectedUrl, bool liveMode, IReadOnlyCollection<string> requiredEvents);
	}
}
