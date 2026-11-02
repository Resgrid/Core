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
	/// Per-address caps on the signed-out account forms over the shared cache. Each use increments the form's fixed-window counter;
	/// the use that reaches the cap sets a refusal marker for one window, which later uses check first. Addresses are only stored hashed.
	/// Any cache failure lets the form through: these caps slow automation, and an outage must not stop people registering or recovering.
	/// </summary>
	public class PublicFormRateLimiter : IPublicFormRateLimiter
	{
		private const string CachePrefix = "security:form-limit:";
		private readonly ICacheProvider _cacheProvider;

		public PublicFormRateLimiter(ICacheProvider cacheProvider)
		{
			_cacheProvider = cacheProvider;
		}

		/// <summary>The cap for a form and its window; a cap of 0 turns the form's limit off.</summary>
		private static (int Cap, TimeSpan Window) LimitOf(PublicFormLimit form) => form switch
		{
			PublicFormLimit.Register => (SessionSecurityConfig.RegisterIpLimitPerHour, TimeSpan.FromHours(1)),
			PublicFormLimit.RegisterCreated => (SessionSecurityConfig.RegisterIpAccountsPerDay, TimeSpan.FromDays(1)),
			PublicFormLimit.PasswordReset => (SessionSecurityConfig.PasswordResetIpLimitPerHour, TimeSpan.FromHours(1)),
			PublicFormLimit.FactorRecovery => (SessionSecurityConfig.FactorRecoveryIpLimitPerHour, TimeSpan.FromHours(1)),
			PublicFormLimit.Invite => (SessionSecurityConfig.InviteIpLimitPerHour, TimeSpan.FromHours(1)),
			_ => (0, TimeSpan.FromHours(1))
		};

		public TimeSpan WindowOf(PublicFormLimit form) => LimitOf(form).Window;

		public async Task<bool> TryAcquireAsync(PublicFormLimit form, string ipAddress, CancellationToken cancellationToken = default)
		{
			if (await IsLimitedAsync(form, ipAddress, cancellationToken))
				return false;

			await RecordAsync(form, ipAddress, cancellationToken);
			return true;
		}

		public async Task<bool> IsLimitedAsync(PublicFormLimit form, string ipAddress, CancellationToken cancellationToken = default)
		{
			if (LimitOf(form).Cap <= 0 || string.IsNullOrWhiteSpace(ipAddress))
				return false;

			try
			{
				return !string.IsNullOrEmpty(await _cacheProvider.GetStringAsync(Key(form, ipAddress, "limited")));
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"The {form} rate limit could not be read; the form continues.");
				return false;
			}
		}

		public async Task RecordAsync(PublicFormLimit form, string ipAddress, CancellationToken cancellationToken = default)
		{
			var (cap, window) = LimitOf(form);
			if (cap <= 0 || string.IsNullOrWhiteSpace(ipAddress))
				return;

			try
			{
				if (await _cacheProvider.IncrementAsync(Key(form, ipAddress, "count"), window) >= cap)
					await _cacheProvider.SetStringAsync(Key(form, ipAddress, "limited"), "1", window);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"A use of the {form} form could not be counted.");
			}
		}

		private static string Key(PublicFormLimit form, string ipAddress, string part) =>
			$"{CachePrefix}{form}:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ipAddress.Trim()))).ToLowerInvariant()}:{part}";
	}
}
