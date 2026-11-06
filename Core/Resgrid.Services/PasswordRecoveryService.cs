using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Config;
using Resgrid.Model.Providers;
using Resgrid.Model.Security;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class PasswordRecoveryService : IPasswordRecoveryService
	{
		private const string CachePrefix = "security:password-recovery:";
		private readonly ICacheProvider _cacheProvider;

		public PasswordRecoveryService(ICacheProvider cacheProvider)
		{
			_cacheProvider = cacheProvider;
		}

		private static readonly TimeSpan RateWindow = TimeSpan.FromHours(1);

		private static TimeSpan PublicLinkLifetime =>
			TimeSpan.FromMinutes(Math.Max(5, SessionSecurityConfig.PublicResetLinkLifetimeMinutes));

		private static TimeSpan AdministratorLinkLifetime =>
			TimeSpan.FromMinutes(Math.Max(5, SessionSecurityConfig.AdministratorResetLinkLifetimeMinutes));

		public async Task<PasswordRecoveryIssueResult> IssueAsync(string userId, string email, string ipAddress,
			long authenticationGeneration, string securityStamp,
			CancellationToken cancellationToken = default)
		{
			var ipCount = await _cacheProvider.IncrementAsync(
				$"{CachePrefix}rate:ip:{Hash(ipAddress ?? "unknown")}", RateWindow);
			var accountCount = await CountAccountRequestAsync(email);

			if (ipCount == 0 || accountCount == 0 ||
				ipCount > SessionSecurityConfig.PublicResetIpLimitPerHour ||
				accountCount > SessionSecurityConfig.PublicResetAccountLimitPerHour)
			{
				return new PasswordRecoveryIssueResult
				{
					RateLimited = true,
					IpRateLimited = ipCount > SessionSecurityConfig.PublicResetIpLimitPerHour
				};
			}

			// Rate-limit unknown accounts too, but only persist a reset request for an eligible user.
			if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(email))
				return new PasswordRecoveryIssueResult();

			return await SaveRequestAsync(userId, email, authenticationGeneration, securityStamp, PublicLinkLifetime);
		}

		public async Task<PasswordRecoveryIssueResult> IssueForAdministratorAsync(string userId, string email,
			long authenticationGeneration, string securityStamp,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(email))
				return new PasswordRecoveryIssueResult();

			// The member's own hourly limit still applies (the same counter as their forgot-password requests), so nobody can
			// fill a member's inbox with reset emails.
			var accountCount = await CountAccountRequestAsync(email);
			if (accountCount == 0 || accountCount > SessionSecurityConfig.PublicResetAccountLimitPerHour)
				return new PasswordRecoveryIssueResult { RateLimited = true };

			return await SaveRequestAsync(userId, email, authenticationGeneration, securityStamp, AdministratorLinkLifetime);
		}

		private Task<long> CountAccountRequestAsync(string email) =>
			_cacheProvider.IncrementAsync(
				$"{CachePrefix}rate:account:{Hash((email ?? string.Empty).Trim().ToUpperInvariant())}", RateWindow);

		private async Task<PasswordRecoveryIssueResult> SaveRequestAsync(string userId, string email,
			long authenticationGeneration, string securityStamp, TimeSpan lifetime)
		{
			var now = DateTime.UtcNow;
			var token = ToBase64Url(RandomNumberGenerator.GetBytes(32));
			var request = new PasswordRecoveryRequest
			{
				UserId = userId,
				Email = email,
				AuthenticationGeneration = authenticationGeneration,
				SecurityStampHash = Hash(securityStamp),
				CreatedOn = now,
				ExpiresOn = now.Add(lifetime)
			};

			var saved = await _cacheProvider.SetStringAsync(GetRequestKey(token),
				JsonConvert.SerializeObject(request), lifetime);
			return saved
				? new PasswordRecoveryIssueResult { Issued = true, Token = token, ExpiresOn = request.ExpiresOn, Lifetime = lifetime }
				: new PasswordRecoveryIssueResult();
		}

		public async Task<PasswordRecoveryLookupResult> GetAsync(string token, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(token))
				return PasswordRecoveryLookupResult.NotFound();

			var json = await _cacheProvider.GetStringAsync(GetRequestKey(token));
			if (string.IsNullOrWhiteSpace(json))
				return PasswordRecoveryLookupResult.NotFound();

			PasswordRecoveryRequest request;
			try
			{
				request = JsonConvert.DeserializeObject<PasswordRecoveryRequest>(json);
			}
			catch (JsonException)
			{
				// A token whose stored payload no longer parses is spent as far as this flow is concerned;
				// it is reported the same as a missing one so callers cannot leak the difference.
				return PasswordRecoveryLookupResult.NotFound();
			}

			return request?.ExpiresOn > DateTime.UtcNow
				? PasswordRecoveryLookupResult.ForRequest(request)
				: PasswordRecoveryLookupResult.NotFound();
		}

		public async Task<bool> TryConsumeAsync(string token, CancellationToken cancellationToken = default)
		{
			var lookup = await GetAsync(token, cancellationToken);
			if (!lookup.Found)
				return false;

			// The used marker only has to outlive the grant it guards, whichever lifetime that grant was issued with.
			var remaining = lookup.Request.ExpiresOn - DateTime.UtcNow;
			return await _cacheProvider.IncrementAsync(GetUsedKey(token),
				remaining > TimeSpan.FromMinutes(1) ? remaining : TimeSpan.FromMinutes(1)) == 1;
		}

		public async Task ReleaseAsync(string token, CancellationToken cancellationToken = default)
		{
			await _cacheProvider.RemoveAsync(GetUsedKey(token));
		}

		public async Task RemoveAsync(string token, CancellationToken cancellationToken = default)
		{
			await _cacheProvider.RemoveAsync(GetRequestKey(token));
		}

		private static string GetRequestKey(string token) => $"{CachePrefix}request:{Hash(token)}";
		private static string GetUsedKey(string token) => $"{CachePrefix}used:{Hash(token)}";

		private static string Hash(string value) =>
			Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty))).ToLowerInvariant();

		private static string ToBase64Url(byte[] value) =>
			Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
	}
}
