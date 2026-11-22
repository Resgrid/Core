using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Providers
{
	/// <summary>
	/// Push to the Dispatch app only (M0324). The Responder and Dispatch apps share the user's Novu subscriber, so desk pushes
	/// (offers, break reminders, queue alerts) go to a separate Dispatch subscriber, <c>{code}_Dispatch_User_{userId}</c>, which
	/// only the Dispatch app registers its device on. Everything else the Dispatch app receives still comes through the user
	/// subscriber, unchanged.
	/// </summary>
	public interface IDispatcherDeskPushProvider
	{
		/// <summary>Adds the device token to the user's Dispatch subscriber (platform: 1 iOS, 2 Android, 3 web, as Devices/RegisterDevice).</summary>
		Task<bool> RegisterDeviceAsync(int departmentId, string userId, int platform, string token, CancellationToken cancellationToken = default);

		Task<bool> UnregisterDeviceAsync(int departmentId, string userId, int platform, string token, CancellationToken cancellationToken = default);

		/// <summary>Sends one desk push to the user's Dispatch subscriber.</summary>
		/// <param name="eventCode">What the app opens: <c>DQ:{itemId}</c> (desk item) or <c>DB:{breakId}</c> (break).</param>
		/// <param name="sound">One of the desk <see cref="PushSoundTypes"/> (28–32), so the Dispatch app plays its own sound.</param>
		Task<bool> SendAsync(int departmentId, string userId, string title, string body, string eventCode, PushSoundTypes sound,
			CancellationToken cancellationToken = default);
	}

	/// <summary>Twilio voice operations for the dispatcher desk (connecting dispatchers to held callers).</summary>
	public interface IDispatcherVoiceProvider
	{
		/// <summary>Places an outbound call that fetches <paramref name="twimlUrl"/> when answered. Returns the provider call id, or null on failure.</summary>
		Task<string> PlaceCallAsync(string to, string from, string twimlUrl, string statusCallbackUrl, int timeoutSeconds,
			CancellationToken cancellationToken = default);

		/// <summary>Sends a live call to new TwiML (e.g. a held caller to the overflow handling).</summary>
		Task<bool> RedirectCallAsync(string callSid, string twimlUrl, CancellationToken cancellationToken = default);

		Task<bool> EndCallAsync(string callSid, CancellationToken cancellationToken = default);

		/// <summary>
		/// Deletes an emptied hold queue (each held caller gets its own queue; Twilio keeps queues until they are deleted). Only an
		/// empty queue can be deleted, so it is called once the caller has left it.
		/// </summary>
		Task<bool> DeleteQueueAsync(string queueSid, CancellationToken cancellationToken = default);
	}
}
