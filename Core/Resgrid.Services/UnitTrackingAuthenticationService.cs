using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class UnitTrackingAuthenticationService : IUnitTrackingAuthenticationService
	{
		private readonly IUnitTrackingCredentialsRepository _credentialsRepository;
		private readonly IUnitTrackingDevicesRepository _devicesRepository;
		private readonly IUnitTrackingIdentifierService _identifierService;
		private readonly ICacheProvider _cacheProvider;

		public UnitTrackingAuthenticationService(
			IUnitTrackingCredentialsRepository credentialsRepository,
			IUnitTrackingDevicesRepository devicesRepository,
			IUnitTrackingIdentifierService identifierService,
			ICacheProvider cacheProvider)
		{
			_credentialsRepository = credentialsRepository;
			_devicesRepository = devicesRepository;
			_identifierService = identifierService;
			_cacheProvider = cacheProvider;
		}

		public UnitTrackingGeneratedCredential GenerateCredential()
		{
			EnsurePepperConfigured();

			var secretBytes = RandomNumberGenerator.GetBytes(32);
			var prefixBytes = RandomNumberGenerator.GetBytes(6);
			var encodedSecret = EncodeBase64Url(secretBytes);
			var keyPrefix = EncodeBase64Url(prefixBytes);
			var token = $"rgtrk_{keyPrefix}_{encodedSecret}";

			return new UnitTrackingGeneratedCredential
			{
				Token = token,
				KeyPrefix = keyPrefix,
				SecretHash = ComputeSecretHash(token)
			};
		}

		public string ComputeSecretHash(string token)
		{
			if (string.IsNullOrWhiteSpace(token))
				throw new ArgumentNullException(nameof(token));

			EnsurePepperConfigured();

			using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(UnitTrackingConfig.CredentialPepper));
			return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
		}

		public bool VerifySecret(string token, string storedHash)
		{
			if (string.IsNullOrWhiteSpace(token) ||
			    string.IsNullOrWhiteSpace(storedHash) ||
			    storedHash.Length != 64)
				return false;

			try
			{
				var computed = Convert.FromHexString(ComputeSecretHash(token));
				var stored = Convert.FromHexString(storedHash);
				return CryptographicOperations.FixedTimeEquals(computed, stored);
			}
			catch (FormatException)
			{
				return false;
			}
		}

		public async Task<UnitTrackingAuthenticationResult> AuthenticateAsync(
			string token,
			DateTime? utcNow = null,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(token))
				return null;

			cancellationToken.ThrowIfCancellationRequested();
			var secretHash = ComputeSecretHash(token);

			async Task<UnitTrackingAuthenticationResult> load()
			{
				var credential = await _credentialsRepository.GetBySecretHashAsync(secretHash);
				if (credential == null || !VerifySecret(token, credential.SecretHash))
					return null;

				var device = await _devicesRepository.GetByIdAsync(credential.UnitTrackingDeviceId);
				if (device == null)
					return null;

				return new UnitTrackingAuthenticationResult
				{
					Device = device,
					Credential = credential
				};
			}

			var result = SystemBehaviorConfig.CacheEnabled
				? await _cacheProvider.RetrieveAsync(
					UnitTrackingCacheKeys.Credential(secretHash),
					load,
					TimeSpan.FromSeconds(Math.Max(1, Math.Min(60, UnitTrackingConfig.CredentialCacheSeconds))))
				: await load();

			return IsActive(result, utcNow ?? DateTime.UtcNow) ? result : null;
		}

		public async Task<UnitTrackingDevice> GetEnabledDeviceByEndpointIdAsync(
			string deviceId,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(deviceId))
				return null;

			cancellationToken.ThrowIfCancellationRequested();

			async Task<UnitTrackingDevice> load()
			{
				var device = await _devicesRepository.GetByIdAsync(deviceId);
				return IsEnabled(device) ? device : null;
			}

			return SystemBehaviorConfig.CacheEnabled
				? await _cacheProvider.RetrieveAsync(
					UnitTrackingCacheKeys.Endpoint(deviceId),
					load,
					TimeSpan.FromSeconds(Math.Max(1, Math.Min(300, UnitTrackingConfig.DeviceMappingCacheSeconds))))
				: await load();
		}

		public async Task<UnitTrackingDevice> GetEnabledDeviceByProtocolIdentifierAsync(
			string protocolKey,
			string deviceIdentifier,
			CancellationToken cancellationToken = default)
		{
			var normalizedProtocol = NormalizeKey(protocolKey);
			var normalizedIdentifier = _identifierService.Normalize(deviceIdentifier);
			if (normalizedProtocol == null || normalizedIdentifier == null)
				return null;

			cancellationToken.ThrowIfCancellationRequested();

			async Task<UnitTrackingDevice> load()
			{
				var device = await _devicesRepository.GetByProtocolIdentifierAsync(
					normalizedProtocol,
					normalizedIdentifier);
				return IsEnabled(device) ? device : null;
			}

			return SystemBehaviorConfig.CacheEnabled
				? await _cacheProvider.RetrieveAsync(
					UnitTrackingCacheKeys.ProtocolIdentifier(normalizedProtocol, normalizedIdentifier),
					load,
					TimeSpan.FromSeconds(Math.Max(1, Math.Min(300, UnitTrackingConfig.DeviceMappingCacheSeconds))))
				: await load();
		}

		public async Task<IReadOnlyCollection<UnitTrackingCredential>> GetActiveCredentialsForDeviceAsync(
			string deviceId,
			DateTime? utcNow = null,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(deviceId))
				return Array.Empty<UnitTrackingCredential>();

			cancellationToken.ThrowIfCancellationRequested();
			var device = await GetEnabledDeviceByEndpointIdAsync(deviceId, cancellationToken);
			if (device == null)
				return Array.Empty<UnitTrackingCredential>();

			async Task<List<UnitTrackingCredential>> load()
			{
				var credentials = await _credentialsRepository.GetAllByDeviceIdAsync(deviceId);
				return credentials?
					.Select(SanitizeCredential)
					.ToList() ?? new List<UnitTrackingCredential>();
			}

			var cached = SystemBehaviorConfig.CacheEnabled
				? await _cacheProvider.RetrieveAsync(
					UnitTrackingCacheKeys.DeviceCredentials(deviceId),
					load,
					TimeSpan.FromSeconds(Math.Max(1, Math.Min(60, UnitTrackingConfig.CredentialCacheSeconds))))
				: await load();
			cached ??= new List<UnitTrackingCredential>();
			var now = utcNow ?? DateTime.UtcNow;

			return cached
				.Where(credential => IsActive(credential, now))
				.ToList();
		}

		public async Task InvalidateCredentialAsync(string secretHash)
		{
			if (!string.IsNullOrWhiteSpace(secretHash))
				await _cacheProvider.RemoveAsync(UnitTrackingCacheKeys.Credential(secretHash));
		}

		public async Task InvalidateDeviceAsync(UnitTrackingDevice device)
		{
			if (device == null || string.IsNullOrWhiteSpace(device.UnitTrackingDeviceId))
				return;

			await _cacheProvider.RemoveAsync(UnitTrackingCacheKeys.Endpoint(device.UnitTrackingDeviceId));
			await _cacheProvider.RemoveAsync(UnitTrackingCacheKeys.DeviceCredentials(device.UnitTrackingDeviceId));

			var protocolKey = NormalizeKey(device.ProtocolKey);
			var identifier = _identifierService.Normalize(device.DeviceIdentifier);
			if (protocolKey != null && identifier != null)
			{
				await _cacheProvider.RemoveAsync(
					UnitTrackingCacheKeys.ProtocolIdentifier(protocolKey, identifier));
			}
		}

		private static bool IsActive(UnitTrackingAuthenticationResult result, DateTime utcNow)
		{
			if (result?.Credential == null || !IsEnabled(result.Device))
				return false;

			return result.Credential.ValidFrom <= utcNow &&
			       !result.Credential.RevokedOn.HasValue &&
			       (!result.Credential.ExpiresOn.HasValue || result.Credential.ExpiresOn > utcNow);
		}

		private static bool IsActive(UnitTrackingCredential credential, DateTime utcNow)
		{
			return credential != null &&
			       credential.ValidFrom <= utcNow &&
			       !credential.RevokedOn.HasValue &&
			       (!credential.ExpiresOn.HasValue || credential.ExpiresOn > utcNow);
		}

		private static bool IsEnabled(UnitTrackingDevice device) =>
			device != null && device.IsEnabled && !device.IsDeleted;

		private static string NormalizeKey(string value) =>
			string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();

		private static string EncodeBase64Url(byte[] value) =>
			Convert.ToBase64String(value)
				.TrimEnd('=')
				.Replace('+', '-')
				.Replace('/', '_');

		private static UnitTrackingCredential SanitizeCredential(UnitTrackingCredential credential)
		{
			return new UnitTrackingCredential
			{
				UnitTrackingCredentialId = credential.UnitTrackingCredentialId,
				UnitTrackingDeviceId = credential.UnitTrackingDeviceId,
				AuthMode = credential.AuthMode,
				HeaderName = credential.HeaderName,
				BasicUsername = credential.BasicUsername,
				KeyPrefix = credential.KeyPrefix,
				ValidFrom = credential.ValidFrom,
				ExpiresOn = credential.ExpiresOn,
				RevokedOn = credential.RevokedOn,
				LastUsedOn = credential.LastUsedOn,
				CreatedByUserId = credential.CreatedByUserId,
				CreatedOn = credential.CreatedOn
			};
		}

		private static void EnsurePepperConfigured()
		{
			if (string.IsNullOrWhiteSpace(UnitTrackingConfig.CredentialPepper))
			{
				throw new InvalidOperationException(
					"UnitTrackingConfig.CredentialPepper must be configured before using tracking credentials.");
			}
		}
	}
}
