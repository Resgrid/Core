using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class DepartmentApiKeysService : IDepartmentApiKeysService
	{
		/// <summary>Every key starts with this, so leaked keys are easy to find with secret scanners.</summary>
		public const string KeyPrefix = "rgk_";

		private const string CacheKeyFormat = "DepartmentApiKey_{0}";
		private static readonly TimeSpan MarkUsedInterval = TimeSpan.FromMinutes(5);

		private readonly IDepartmentApiKeysRepository _apiKeysRepository;
		private readonly IDepartmentsService _departmentsService;
		private readonly ICacheProvider _cacheProvider;
		private readonly IEventAggregator _eventAggregator;

		public DepartmentApiKeysService(IDepartmentApiKeysRepository apiKeysRepository, IDepartmentsService departmentsService,
			ICacheProvider cacheProvider, IEventAggregator eventAggregator)
		{
			_apiKeysRepository = apiKeysRepository;
			_departmentsService = departmentsService;
			_cacheProvider = cacheProvider;
			_eventAggregator = eventAggregator;
		}

		public async Task<List<DepartmentApiKey>> GetKeysForDepartmentAsync(int departmentId)
		{
			return await _apiKeysRepository.GetAllForDepartmentAsync(departmentId) ?? new List<DepartmentApiKey>();
		}

		public async Task<DepartmentApiKey> GetKeyForDepartmentAsync(int departmentId, string departmentApiKeyId)
		{
			if (string.IsNullOrWhiteSpace(departmentApiKeyId))
				return null;

			var key = await _apiKeysRepository.GetByIdAsync(departmentApiKeyId);

			return key != null && key.DepartmentId == departmentId ? key : null;
		}

		public async Task<DepartmentApiKeyCreateResult> CreateKeyAsync(int departmentId, string name, IEnumerable<string> scopes, DateTime expiresOnUtc,
			string allowedIpRanges, string createdByUserId, string ipAddress = null, CancellationToken cancellationToken = default)
		{
			var now = DateTime.UtcNow;
			name = name?.Trim();
			var grantedScopes = DepartmentApiKeyScopes.Parse(string.Join(" ", scopes ?? Enumerable.Empty<string>()));
			allowedIpRanges = NormalizeIpRanges(allowedIpRanges);

			if (string.IsNullOrWhiteSpace(name))
				return Failed("Give the key a name.");

			if (name.Length > 100)
				return Failed("The name can be at most 100 characters.");

			if (grantedScopes.Count == 0)
				return Failed("Choose at least one thing the key may do.");

			if (expiresOnUtc <= now)
				return Failed("The expiry must be in the future.");

			var maxDays = Math.Max(1, SecurityConfig.DepartmentApiKeyMaxLifetimeDays);
			if (expiresOnUtc > now.AddDays(maxDays))
				return Failed($"A key can be valid for at most {maxDays} days.");

			var badRange = ValidateAllowedIpRanges(allowedIpRanges);
			if (badRange != null)
				return Failed($"\"{badRange}\" is not an IP address or CIDR range.");

			if (allowedIpRanges != null && allowedIpRanges.Length > 1000)
				return Failed("The allowed addresses can be at most 1000 characters.");

			var existing = await _apiKeysRepository.GetAllForDepartmentAsync(departmentId) ?? new List<DepartmentApiKey>();
			var maxActive = Math.Max(1, SecurityConfig.DepartmentApiKeyMaxActivePerDepartment);
			if (existing.Count(x => x.IsActiveAt(now)) >= maxActive)
				return Failed($"The department already has {maxActive} active keys. Revoke one you no longer use first.");

			var secretBytes = RandomNumberGenerator.GetBytes(32);
			var prefixBytes = RandomNumberGenerator.GetBytes(6);
			var keyPrefix = EncodeBase64Url(prefixBytes);
			var plainKey = $"{KeyPrefix}{keyPrefix}_{EncodeBase64Url(secretBytes)}";

			var apiKey = new DepartmentApiKey
			{
				DepartmentApiKeyId = Guid.NewGuid().ToString(),
				DepartmentId = departmentId,
				Name = name,
				KeyPrefix = keyPrefix,
				SecretHash = ComputeSecretHash(plainKey),
				Scopes = DepartmentApiKeyScopes.Serialize(grantedScopes),
				AllowedIpRanges = allowedIpRanges,
				CreatedByUserId = createdByUserId,
				CreatedOn = now,
				ExpiresOn = DateTime.SpecifyKind(expiresOnUtc, DateTimeKind.Utc)
			};

			apiKey = await _apiKeysRepository.InsertAsync(apiKey, cancellationToken);

			PublishAudit(departmentId, createdByUserId, AuditLogTypes.DepartmentApiKeyCreated, null, AuditSnapshot(apiKey), ipAddress);

			return new DepartmentApiKeyCreateResult { Success = true, ApiKey = apiKey, Key = plainKey };
		}

		public async Task<bool> RevokeKeyAsync(int departmentId, string departmentApiKeyId, string revokedByUserId, string ipAddress = null,
			CancellationToken cancellationToken = default)
		{
			var apiKey = await GetKeyForDepartmentAsync(departmentId, departmentApiKeyId);
			if (apiKey == null || apiKey.RevokedOn.HasValue)
				return false;

			var before = AuditSnapshot(apiKey);
			apiKey.RevokedOn = DateTime.UtcNow;
			apiKey.RevokedByUserId = revokedByUserId;
			await _apiKeysRepository.UpdateAsync(apiKey, cancellationToken);

			await _cacheProvider.RemoveAsync(string.Format(CacheKeyFormat, apiKey.SecretHash));

			PublishAudit(departmentId, revokedByUserId, AuditLogTypes.DepartmentApiKeyRevoked, before, AuditSnapshot(apiKey), ipAddress);

			return true;
		}

		public async Task<DepartmentApiKeyAuthenticationResult> AuthenticateAsync(string key, string remoteIpAddress, CancellationToken cancellationToken = default)
		{
			var invalid = new DepartmentApiKeyAuthenticationResult { Status = DepartmentApiKeyAuthenticationStatus.Invalid };

			// Shape check first: anything that is not one of our keys never reaches the database.
			if (!LooksLikeKey(key))
				return invalid;

			var secretHash = ComputeSecretHash(key);

			async Task<DepartmentApiKey> load() => await _apiKeysRepository.GetBySecretHashAsync(secretHash);

			var apiKey = SystemBehaviorConfig.CacheEnabled
				? await _cacheProvider.RetrieveAsync(string.Format(CacheKeyFormat, secretHash), load,
					TimeSpan.FromSeconds(Math.Max(1, Math.Min(60, SecurityConfig.DepartmentApiKeyCacheSeconds))))
				: await load();

			// A blank cached payload deserializes to an empty entity rather than null.
			if (apiKey == null || string.IsNullOrWhiteSpace(apiKey.DepartmentApiKeyId) || !HashesEqual(secretHash, apiKey.SecretHash))
				return invalid;

			var now = DateTime.UtcNow;

			if (apiKey.RevokedOn.HasValue)
				return new DepartmentApiKeyAuthenticationResult { Status = DepartmentApiKeyAuthenticationStatus.Revoked, ApiKey = apiKey };

			if (apiKey.ExpiresOn <= now)
				return new DepartmentApiKeyAuthenticationResult { Status = DepartmentApiKeyAuthenticationStatus.Expired, ApiKey = apiKey };

			if (!IsAddressAllowed(apiKey.AllowedIpRanges, remoteIpAddress))
				return new DepartmentApiKeyAuthenticationResult { Status = DepartmentApiKeyAuthenticationStatus.AddressNotAllowed, ApiKey = apiKey };

			var department = await _departmentsService.GetDepartmentByIdAsync(apiKey.DepartmentId, false);
			if (department == null || department.DepartmentId != apiKey.DepartmentId || string.IsNullOrWhiteSpace(department.ManagingUserId))
				return new DepartmentApiKeyAuthenticationResult { Status = DepartmentApiKeyAuthenticationStatus.DepartmentUnavailable, ApiKey = apiKey };

			if (!apiKey.LastUsedOn.HasValue || apiKey.LastUsedOn.Value < now - MarkUsedInterval)
			{
				try
				{
					await _apiKeysRepository.MarkUsedAsync(apiKey.DepartmentApiKeyId, now, remoteIpAddress, cancellationToken);
					apiKey.LastUsedOn = now;
					await _cacheProvider.RemoveAsync(string.Format(CacheKeyFormat, secretHash));
				}
				catch (Exception ex)
				{
					// Bookkeeping only: a failed write must not fail the caller's request.
					Logging.LogException(ex, $"Could not record the use of department API key {apiKey.DepartmentApiKeyId}.");
				}
			}

			return new DepartmentApiKeyAuthenticationResult
			{
				Status = DepartmentApiKeyAuthenticationStatus.Success,
				ApiKey = apiKey,
				Department = department,
				Scopes = apiKey.GetScopes()
			};
		}

		public string ValidateAllowedIpRanges(string allowedIpRanges)
		{
			foreach (var line in SplitRanges(allowedIpRanges))
			{
				if (!TryParseRange(line, out _))
					return line;
			}

			return null;
		}

		internal static bool LooksLikeKey(string key)
		{
			// rgk_ + 8-char prefix + _ + 43-char secret (56); allow a little slack, never unbounded input.
			return !string.IsNullOrWhiteSpace(key) && key.Length >= 40 && key.Length <= 100 &&
				key.StartsWith(KeyPrefix, StringComparison.Ordinal);
		}

		internal static string ComputeSecretHash(string key)
		{
			using var hmac = new HMACSHA256(GetPepper());
			return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
		}

		/// <summary>
		/// The configured pepper, or one derived from the system encryption key, so a copy of the database alone is never
		/// enough to test guesses against the stored hashes.
		/// </summary>
		private static byte[] GetPepper()
		{
			if (!string.IsNullOrWhiteSpace(SecurityConfig.DepartmentApiKeyPepper))
				return Encoding.UTF8.GetBytes(SecurityConfig.DepartmentApiKeyPepper);

			using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(SecurityConfig.EncryptionKey ?? string.Empty));
			return hmac.ComputeHash(Encoding.UTF8.GetBytes("resgrid-department-api-key-pepper"));
		}

		private static bool HashesEqual(string a, string b)
		{
			if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b) || a.Length != b.Length)
				return false;

			return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(a), Encoding.ASCII.GetBytes(b.ToLowerInvariant()));
		}

		internal static bool IsAddressAllowed(string allowedIpRanges, string remoteIpAddress)
		{
			var ranges = SplitRanges(allowedIpRanges).ToList();
			if (ranges.Count == 0)
				return true;

			if (string.IsNullOrWhiteSpace(remoteIpAddress) || !IPAddress.TryParse(remoteIpAddress, out var address))
				return false;

			if (address.IsIPv4MappedToIPv6)
				address = address.MapToIPv4();

			foreach (var range in ranges)
			{
				if (TryParseRange(range, out var network) && network.Contains(address))
					return true;
			}

			return false;
		}

		private static bool TryParseRange(string value, out IPNetwork network)
		{
			network = default;
			if (string.IsNullOrWhiteSpace(value))
				return false;

			value = value.Trim();

			if (value.Contains('/'))
				return IPNetwork.TryParse(value, out network);

			if (!IPAddress.TryParse(value, out var single))
				return false;

			if (single.IsIPv4MappedToIPv6)
				single = single.MapToIPv4();

			network = new IPNetwork(single, single.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128);
			return true;
		}

		private static IEnumerable<string> SplitRanges(string value)
		{
			if (string.IsNullOrWhiteSpace(value))
				return Enumerable.Empty<string>();

			return value.Split(new[] { '\r', '\n', ',', ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
				.Select(x => x.Trim())
				.Where(x => x.Length > 0);
		}

		private static string NormalizeIpRanges(string value)
		{
			var lines = SplitRanges(value).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			return lines.Count == 0 ? null : string.Join("\n", lines);
		}

		private static string EncodeBase64Url(byte[] value) =>
			Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

		private static DepartmentApiKeyCreateResult Failed(string error) =>
			new DepartmentApiKeyCreateResult { Success = false, Error = error };

		private static object AuditSnapshot(DepartmentApiKey apiKey) => new
		{
			apiKey.DepartmentApiKeyId,
			apiKey.Name,
			apiKey.KeyPrefix,
			apiKey.Scopes,
			apiKey.AllowedIpRanges,
			apiKey.CreatedOn,
			apiKey.ExpiresOn,
			apiKey.RevokedOn,
			apiKey.RevokedByUserId
		};

		private void PublishAudit(int departmentId, string userId, AuditLogTypes type, object before, object after, string ipAddress)
		{
			_eventAggregator.SendMessage(new AuditEvent
			{
				DepartmentId = departmentId,
				UserId = userId,
				Type = type,
				Before = before == null ? null : JsonConvert.SerializeObject(before),
				After = after == null ? null : JsonConvert.SerializeObject(after),
				IpAddress = ipAddress,
				Successful = true,
				ServerName = Environment.MachineName
			});
		}
	}
}
