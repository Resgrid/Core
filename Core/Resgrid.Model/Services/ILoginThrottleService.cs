using System;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Pauses password sign-ins (Web LogOn and the API password grant) from one network address after its failures cover too many
	/// different usernames, or too many attempts, in a window: password spraying and credential stuffing, which the per-account
	/// Identity lockout never sees because each account is tried only a few times. One account's own repeated failures stay the
	/// lockout's job, so a device stuck on an old password never shuts out everyone else on its station's network.
	/// </summary>
	public interface ILoginThrottleService
	{
		/// <summary>How long an address stays paused once it reaches a limit, for the message the user is shown.</summary>
		TimeSpan Window { get; }

		/// <summary>Whether password sign-ins from <paramref name="ipAddress"/> are paused. False when the cache cannot say (never blocks on it).</summary>
		Task<bool> IsThrottledAsync(string ipAddress, CancellationToken cancellationToken = default);

		/// <summary>Counts a refused password sign-in for <paramref name="username"/> from <paramref name="ipAddress"/>; pauses the address at a limit.</summary>
		Task RecordFailureAsync(string ipAddress, string username, CancellationToken cancellationToken = default);
	}
}
