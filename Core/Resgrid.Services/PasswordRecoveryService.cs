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

		public async Task<PasswordRecoveryIssueResult> IssueAsync(string userId, string email, string ipAddress,
			long authenticationGeneration, string securityStamp,
			CancellationToken cancellationToken = default)
		{
			var rateWindow = TimeSpan.FromHours(1);
			var normalizedEmail = (email ?? string.Empty).Trim().ToUpperInvariant();
			var ipCount = await _cacheProvider.IncrementAsync(
				$"{CachePrefix}rate:ip:{Hash(ipAddress ?? "unknown")}", rateWindow);
			var accountCount = await _cacheProvider.IncrementAsync(
				$"{CachePrefix}rate:account:{Hash(normalizedEmail)}", rateWindow);

			if (ipCount == 0 || accountCount == 0 ||
				ipCount > SessionSecurityConfig.PublicResetIpLimitPerHour ||
				accountCount > SessionSecurityConfig.PublicResetAccountLimitPerHour)
			{
				return new PasswordRecoveryIssueResult { RateLimited = true };
			}

			// Rate-limit unknown accounts too, but only persist a reset request for an eligible user.
			if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(email))
				return new PasswordRecoveryIssueResult();

			var now = DateTime.UtcNow;
			var lifetime = TimeSpan.FromMinutes(Math.Max(5, SessionSecurityConfig.PublicResetLinkLifetimeMinutes));
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
			return new PasswordRecoveryIssueResult { Issued = saved, Token = saved ? token : null };
		}

		public async Task<PasswordRecoveryRequest> GetAsync(string token, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(token))
				return null;

			var json = await _cacheProvider.GetStringAsync(GetRequestKey(token));
			if (string.IsNullOrWhiteSpace(json))
				return null;

			PasswordRecoveryRequest request;
			try
			{
				request = JsonConvert.DeserializeObject<PasswordRecoveryRequest>(json);
			}
			catch (JsonException)
			{
				return null;
			}

			return request?.ExpiresOn > DateTime.UtcNow ? request : null;
		}

		public async Task<bool> TryConsumeAsync(string token, CancellationToken cancellationToken = default)
		{
			if (await GetAsync(token, cancellationToken) == null)
				return false;

			var lifetime = TimeSpan.FromMinutes(Math.Max(5, SessionSecurityConfig.PublicResetLinkLifetimeMinutes));
			return await _cacheProvider.IncrementAsync(GetUsedKey(token), lifetime) == 1;
		}

		public Task RemoveAsync(string token, CancellationToken cancellationToken = default) =>
			_cacheProvider.RemoveAsync(GetRequestKey(token));

		private static string GetRequestKey(string token) => $"{CachePrefix}request:{Hash(token)}";
		private static string GetUsedKey(string token) => $"{CachePrefix}used:{Hash(token)}";

		private static string Hash(string value) =>
			Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty))).ToLowerInvariant();

		private static string ToBase64Url(byte[] value) =>
			Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
	}
}
