using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Per-address password sign-in throttle over the shared cache, so every Web and API instance sees the same counts. Counters are
	/// fixed windows from an address's first failure; reaching a limit sets a pause marker that sign-ins check before any password is
	/// verified. Addresses and usernames are only ever stored hashed. Any cache failure lets the sign-in through: the account lockout
	/// still applies, and an outage must never lock everyone out.
	/// </summary>
	public class LoginThrottleService : ILoginThrottleService
	{
		private const string CachePrefix = "security:login-throttle:";
		private readonly ICacheProvider _cacheProvider;

		public LoginThrottleService(ICacheProvider cacheProvider)
		{
			_cacheProvider = cacheProvider;
		}

		public TimeSpan Window => TimeSpan.FromMinutes(Math.Max(1, SessionSecurityConfig.LoginThrottleWindowMinutes));

		private static bool Enabled =>
			SessionSecurityConfig.LoginThrottleDistinctAccountLimit > 0 || SessionSecurityConfig.LoginThrottleAttemptLimit > 0;

		public async Task<bool> IsThrottledAsync(string ipAddress, CancellationToken cancellationToken = default)
		{
			if (!Enabled || string.IsNullOrWhiteSpace(ipAddress))
				return false;

			try
			{
				return !string.IsNullOrEmpty(await _cacheProvider.GetStringAsync(PausedKey(ipAddress)));
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "The sign-in throttle could not be read; the sign-in continues.");
				return false;
			}
		}

		public async Task RecordFailureAsync(string ipAddress, string username, CancellationToken cancellationToken = default)
		{
			if (!Enabled || string.IsNullOrWhiteSpace(ipAddress))
				return;

			try
			{
				var window = Window;
				var address = Hash(ipAddress.Trim());
				var attempts = await _cacheProvider.IncrementAsync($"{CachePrefix}ip:{address}:attempts", window);

				// A username counts once per window however often it fails here: the first failure for it adds one account.
				var account = Hash(address + "\n" + (username ?? string.Empty).Trim().ToUpperInvariant());
				var accounts = await _cacheProvider.IncrementAsync($"{CachePrefix}ip:{address}:account:{account}", window) == 1
					? await _cacheProvider.IncrementAsync($"{CachePrefix}ip:{address}:accounts", window)
					: 0;

				var distinctLimit = SessionSecurityConfig.LoginThrottleDistinctAccountLimit;
				var attemptLimit = SessionSecurityConfig.LoginThrottleAttemptLimit;
				if ((distinctLimit > 0 && accounts >= distinctLimit) || (attemptLimit > 0 && attempts >= attemptLimit))
				{
					await _cacheProvider.SetStringAsync(PausedKey(ipAddress), "1", window);
					Logging.LogInfo($"Password sign-ins from address {address.Substring(0, 12)} paused for {window.TotalMinutes:0} minutes after {attempts} failed attempts.");
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "A failed sign-in could not be counted against its address.");
			}
		}

		private static string PausedKey(string ipAddress) => $"{CachePrefix}ip:{Hash(ipAddress.Trim())}:paused";

		private static string Hash(string value) =>
			Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
	}
}
