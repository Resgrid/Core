using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class UnitTrackingService : IUnitTrackingService
	{
		private readonly IUnitTrackingDevicesRepository _devicesRepository;
		private readonly IUnitTrackingCredentialsRepository _credentialsRepository;
		private readonly IUnitTrackingAuthenticationService _authenticationService;
		private readonly IUnitTrackingIdentifierService _identifierService;
		private readonly IUnitsService _unitsService;
		private readonly IEventAggregator _eventAggregator;
		private readonly IUnitOfWork _unitOfWork;

		public UnitTrackingService(
			IUnitTrackingDevicesRepository devicesRepository,
			IUnitTrackingCredentialsRepository credentialsRepository,
			IUnitTrackingAuthenticationService authenticationService,
			IUnitTrackingIdentifierService identifierService,
			IUnitsService unitsService,
			IEventAggregator eventAggregator,
			IUnitOfWork unitOfWork)
		{
			_devicesRepository = devicesRepository;
			_credentialsRepository = credentialsRepository;
			_authenticationService = authenticationService;
			_identifierService = identifierService;
			_unitsService = unitsService;
			_eventAggregator = eventAggregator;
			_unitOfWork = unitOfWork;
		}

		public async Task<UnitTrackingDevice> GetDeviceByIdAsync(string deviceId, int departmentId)
		{
			var device = await _devicesRepository.GetByIdAsync(deviceId);
			return device != null && device.DepartmentId == departmentId && !device.IsDeleted
				? device
				: null;
		}

		public async Task<List<UnitTrackingDevice>> GetDevicesForDepartmentAsync(int departmentId)
		{
			var devices = await _devicesRepository.GetAllByDepartmentIdAsync(departmentId);
			return devices?.Where(device => !device.IsDeleted).ToList() ?? new List<UnitTrackingDevice>();
		}

		public async Task<List<UnitTrackingDevice>> GetDevicesForUnitAsync(int departmentId, int unitId)
		{
			var devices = await _devicesRepository.GetAllByUnitIdAsync(departmentId, unitId);
			return devices?.Where(device => !device.IsDeleted).ToList() ?? new List<UnitTrackingDevice>();
		}

		public async Task<List<UnitTrackingCredential>> GetCredentialsForDeviceAsync(string deviceId, int departmentId)
		{
			await GetOwnedDeviceAsync(deviceId, departmentId, includeDeleted: false);
			var credentials = await _credentialsRepository.GetAllByDeviceIdAsync(deviceId);
			return credentials?.Select(SanitizeCredential).ToList() ?? new List<UnitTrackingCredential>();
		}

		public async Task<UnitTrackingDevice> CreateDeviceAsync(
			UnitTrackingDevice device,
			int departmentId,
			string userId,
			CancellationToken cancellationToken = default)
		{
			if (device == null)
				throw new ArgumentNullException(nameof(device));

			ValidateUserAndDepartment(userId, departmentId);
			await ValidateUnitOwnershipAsync(device.UnitId, departmentId);

			var now = DateTime.UtcNow;
			var created = new UnitTrackingDevice
			{
				UnitTrackingDeviceId = Guid.NewGuid().ToString(),
				DepartmentId = departmentId,
				UnitId = device.UnitId,
				DisplayName = TrimToNull(device.DisplayName),
				ManufacturerKey = NormalizeKey(device.ManufacturerKey),
				ModelKey = NormalizeKey(device.ModelKey),
				TransportType = ValidateTransportType(device.TransportType),
				ProtocolKey = NormalizeKey(device.ProtocolKey),
				PayloadAdapterKey = NormalizeKey(device.PayloadAdapterKey),
				DeviceIdentifier = _identifierService.Normalize(device.DeviceIdentifier),
				SecondaryIdentifier = _identifierService.Normalize(device.SecondaryIdentifier),
				IsEnabled = device.IsEnabled,
				IsDeleted = false,
				SourcePriority = device.SourcePriority,
				AllowedSourceCidrs = TrimToNull(device.AllowedSourceCidrs),
				LastStatus = device.IsEnabled
					? (int)UnitTrackingDeviceStatus.NeverSeen
					: (int)UnitTrackingDeviceStatus.Disabled,
				FirmwareVersion = TrimToNull(device.FirmwareVersion),
				CreatedByUserId = userId,
				CreatedOn = now
			};

			var saved = await _devicesRepository.InsertAsync(created, cancellationToken);
			await _authenticationService.InvalidateDeviceAsync(saved);
			PublishAudit(departmentId, userId, AuditLogTypes.UnitTrackingDeviceCreated, null, DeviceAuditSnapshot(saved));
			return saved;
		}

		public async Task<UnitTrackingDevice> UpdateDeviceAsync(
			UnitTrackingDevice device,
			int departmentId,
			string userId,
			CancellationToken cancellationToken = default)
		{
			if (device == null)
				throw new ArgumentNullException(nameof(device));

			ValidateUserAndDepartment(userId, departmentId);
			var existing = await GetOwnedDeviceAsync(device.UnitTrackingDeviceId, departmentId, includeDeleted: false);

			if (device.UnitId != existing.UnitId)
			{
				throw new InvalidOperationException(
					"A physical tracker must be rebound by creating a new binding; UnitId cannot be edited in place.");
			}

			if (existing.IsEnabled && !device.IsEnabled)
				return await DisableDeviceAsync(existing.UnitTrackingDeviceId, departmentId, userId, cancellationToken);

			await ValidateUnitOwnershipAsync(existing.UnitId, departmentId);
			var credentials = await GetCredentialEntitiesAsync(existing.UnitTrackingDeviceId);
			var before = DeviceAuditSnapshot(existing);
			var oldCacheIdentity = CopyCacheIdentity(existing);

			existing.DisplayName = TrimToNull(device.DisplayName);
			existing.ManufacturerKey = NormalizeKey(device.ManufacturerKey);
			existing.ModelKey = NormalizeKey(device.ModelKey);
			existing.TransportType = ValidateTransportType(device.TransportType);
			existing.ProtocolKey = NormalizeKey(device.ProtocolKey);
			existing.PayloadAdapterKey = NormalizeKey(device.PayloadAdapterKey);
			existing.DeviceIdentifier = _identifierService.Normalize(device.DeviceIdentifier);
			existing.SecondaryIdentifier = _identifierService.Normalize(device.SecondaryIdentifier);
			existing.IsEnabled = device.IsEnabled;
			existing.SourcePriority = device.SourcePriority;
			existing.AllowedSourceCidrs = TrimToNull(device.AllowedSourceCidrs);
			existing.FirmwareVersion = TrimToNull(device.FirmwareVersion);
			existing.UpdatedByUserId = userId;
			existing.UpdatedOn = DateTime.UtcNow;
			if (device.IsEnabled && existing.LastStatus == (int)UnitTrackingDeviceStatus.Disabled)
				existing.LastStatus = (int)UnitTrackingDeviceStatus.NeverSeen;

			var saved = await _devicesRepository.UpdateAsync(existing, cancellationToken);
			await InvalidateDeviceAndCredentialsAsync(oldCacheIdentity, saved, credentials);
			PublishAudit(departmentId, userId, AuditLogTypes.UnitTrackingDeviceUpdated, before, DeviceAuditSnapshot(saved));
			return saved;
		}

		public Task<UnitTrackingDevice> DisableDeviceAsync(
			string deviceId,
			int departmentId,
			string userId,
			CancellationToken cancellationToken = default)
		{
			return DisableOrDeleteDeviceAsync(deviceId, departmentId, userId, false, cancellationToken);
		}

		public Task<UnitTrackingDevice> DeleteDeviceAsync(
			string deviceId,
			int departmentId,
			string userId,
			CancellationToken cancellationToken = default)
		{
			return DisableOrDeleteDeviceAsync(deviceId, departmentId, userId, true, cancellationToken);
		}

		public async Task<UnitTrackingDevice> RebindDeviceAsync(
			string deviceId,
			int departmentId,
			int newUnitId,
			string userId,
			CancellationToken cancellationToken = default)
		{
			ValidateUserAndDepartment(userId, departmentId);
			var existing = await GetOwnedDeviceAsync(deviceId, departmentId, includeDeleted: false);
			if (existing.UnitId == newUnitId)
				throw new InvalidOperationException("The tracking device is already bound to the requested Unit.");

			await ValidateUnitOwnershipAsync(newUnitId, departmentId);
			var credentials = await GetCredentialEntitiesAsync(deviceId);
			var before = DeviceAuditSnapshot(existing);
			var oldCacheIdentity = CopyCacheIdentity(existing);
			var now = DateTime.UtcNow;

			_unitOfWork.CreateOrGetConnection();
			UnitTrackingDevice replacement;
			try
			{
				existing.IsEnabled = false;
				existing.IsDeleted = true;
				existing.LastStatus = (int)UnitTrackingDeviceStatus.Disabled;
				existing.UpdatedByUserId = userId;
				existing.UpdatedOn = now;
				await _devicesRepository.UpdateAsync(existing, cancellationToken);
				await RevokeCredentialsAsync(credentials, now, cancellationToken);

				replacement = CopyForRebind(existing, newUnitId, userId, now);
				await _devicesRepository.InsertAsync(replacement, cancellationToken);
				_unitOfWork.CommitChanges();
			}
			catch
			{
				_unitOfWork.DiscardChanges();
				throw;
			}

			await InvalidateDeviceAndCredentialsAsync(oldCacheIdentity, replacement, credentials);
			PublishAudit(departmentId, userId, AuditLogTypes.UnitTrackingDeviceDeleted, before, DeviceAuditSnapshot(existing));
			PublishAudit(departmentId, userId, AuditLogTypes.UnitTrackingDeviceCreated, null, DeviceAuditSnapshot(replacement));
			return replacement;
		}

		public async Task<UnitTrackingCredentialProvisionResult> CreateCredentialAsync(
			string deviceId,
			int departmentId,
			UnitTrackingAuthMode authMode,
			string userId,
			string headerName = null,
			string basicUsername = null,
			CancellationToken cancellationToken = default)
		{
			ValidateUserAndDepartment(userId, departmentId);
			var device = await GetOwnedDeviceAsync(deviceId, departmentId, includeDeleted: false);
			if (!device.IsEnabled)
				throw new InvalidOperationException("Credentials cannot be created for a disabled tracking device.");

			ValidateAuthMode(authMode, headerName, basicUsername);
			var generated = _authenticationService.GenerateCredential();
			var now = DateTime.UtcNow;
			var credential = new UnitTrackingCredential
			{
				UnitTrackingCredentialId = Guid.NewGuid().ToString(),
				UnitTrackingDeviceId = deviceId,
				AuthMode = (int)authMode,
				HeaderName = authMode == UnitTrackingAuthMode.CustomHeader ? headerName.Trim() : null,
				BasicUsername = authMode == UnitTrackingAuthMode.Basic ? basicUsername.Trim() : null,
				KeyPrefix = generated.KeyPrefix,
				SecretHash = generated.SecretHash,
				ValidFrom = now,
				CreatedByUserId = userId,
				CreatedOn = now
			};

			var saved = await _credentialsRepository.InsertAsync(credential, cancellationToken);
			await _authenticationService.InvalidateCredentialAsync(saved.SecretHash);
			await _authenticationService.InvalidateDeviceAsync(device);
			PublishAudit(
				departmentId,
				userId,
				AuditLogTypes.UnitTrackingCredentialCreated,
				null,
				CredentialAuditSnapshot(saved));

			return BuildProvisioningResult(device, saved, generated.Token);
		}

		public async Task<UnitTrackingCredentialProvisionResult> RotateCredentialAsync(
			string deviceId,
			string credentialId,
			int departmentId,
			string userId,
			TimeSpan? overlap = null,
			CancellationToken cancellationToken = default)
		{
			ValidateUserAndDepartment(userId, departmentId);
			var device = await GetOwnedDeviceAsync(deviceId, departmentId, includeDeleted: false);
			if (!device.IsEnabled)
				throw new InvalidOperationException("Credentials cannot be rotated for a disabled tracking device.");

			var existing = await GetOwnedCredentialAsync(credentialId, deviceId);
			if (existing.RevokedOn.HasValue)
				throw new InvalidOperationException("A revoked tracking credential cannot be rotated.");

			var rotationOverlap = overlap ??
				TimeSpan.FromHours(Math.Max(0, UnitTrackingConfig.CredentialRotationOverlapHours));
			if (rotationOverlap < TimeSpan.Zero)
				throw new ArgumentOutOfRangeException(nameof(overlap));

			var generated = _authenticationService.GenerateCredential();
			var now = DateTime.UtcNow;
			var replacement = new UnitTrackingCredential
			{
				UnitTrackingCredentialId = Guid.NewGuid().ToString(),
				UnitTrackingDeviceId = deviceId,
				AuthMode = existing.AuthMode,
				HeaderName = existing.HeaderName,
				BasicUsername = existing.BasicUsername,
				KeyPrefix = generated.KeyPrefix,
				SecretHash = generated.SecretHash,
				ValidFrom = now,
				CreatedByUserId = userId,
				CreatedOn = now
			};
			var before = CredentialAuditSnapshot(existing);

			_unitOfWork.CreateOrGetConnection();
			try
			{
				existing.ExpiresOn = now.Add(rotationOverlap);
				await _credentialsRepository.UpdateAsync(existing, cancellationToken);
				await _credentialsRepository.InsertAsync(replacement, cancellationToken);
				_unitOfWork.CommitChanges();
			}
			catch
			{
				_unitOfWork.DiscardChanges();
				throw;
			}

			await _authenticationService.InvalidateCredentialAsync(existing.SecretHash);
			await _authenticationService.InvalidateCredentialAsync(replacement.SecretHash);
			await _authenticationService.InvalidateDeviceAsync(device);
			PublishAudit(
				departmentId,
				userId,
				AuditLogTypes.UnitTrackingCredentialRotated,
				before,
				new
				{
					Previous = CredentialAuditSnapshot(existing),
					Current = CredentialAuditSnapshot(replacement)
				});

			return BuildProvisioningResult(device, replacement, generated.Token);
		}

		public async Task<UnitTrackingCredential> RevokeCredentialAsync(
			string deviceId,
			string credentialId,
			int departmentId,
			string userId,
			CancellationToken cancellationToken = default)
		{
			ValidateUserAndDepartment(userId, departmentId);
			var device = await GetOwnedDeviceAsync(deviceId, departmentId, includeDeleted: false);
			var credential = await GetOwnedCredentialAsync(credentialId, deviceId);
			var before = CredentialAuditSnapshot(credential);

			if (!credential.RevokedOn.HasValue)
			{
				credential.RevokedOn = DateTime.UtcNow;
				await _credentialsRepository.UpdateAsync(credential, cancellationToken);
			}

			await _authenticationService.InvalidateCredentialAsync(credential.SecretHash);
			await _authenticationService.InvalidateDeviceAsync(device);
			PublishAudit(
				departmentId,
				userId,
				AuditLogTypes.UnitTrackingCredentialRevoked,
				before,
				CredentialAuditSnapshot(credential));

			return SanitizeCredential(credential);
		}

		private async Task<UnitTrackingDevice> DisableOrDeleteDeviceAsync(
			string deviceId,
			int departmentId,
			string userId,
			bool delete,
			CancellationToken cancellationToken)
		{
			ValidateUserAndDepartment(userId, departmentId);
			var device = await GetOwnedDeviceAsync(deviceId, departmentId, includeDeleted: false);
			var before = DeviceAuditSnapshot(device);
			var oldCacheIdentity = CopyCacheIdentity(device);
			var credentials = await GetCredentialEntitiesAsync(deviceId);
			var now = DateTime.UtcNow;

			_unitOfWork.CreateOrGetConnection();
			try
			{
				device.IsEnabled = false;
				device.IsDeleted = delete;
				device.LastStatus = (int)UnitTrackingDeviceStatus.Disabled;
				device.UpdatedByUserId = userId;
				device.UpdatedOn = now;
				await _devicesRepository.UpdateAsync(device, cancellationToken);
				await RevokeCredentialsAsync(credentials, now, cancellationToken);
				_unitOfWork.CommitChanges();
			}
			catch
			{
				_unitOfWork.DiscardChanges();
				throw;
			}

			await InvalidateDeviceAndCredentialsAsync(oldCacheIdentity, device, credentials);
			PublishAudit(
				departmentId,
				userId,
				delete ? AuditLogTypes.UnitTrackingDeviceDeleted : AuditLogTypes.UnitTrackingDeviceDisabled,
				before,
				DeviceAuditSnapshot(device));

			return device;
		}

		private async Task<UnitTrackingDevice> GetOwnedDeviceAsync(
			string deviceId,
			int departmentId,
			bool includeDeleted)
		{
			if (string.IsNullOrWhiteSpace(deviceId))
				throw new ArgumentNullException(nameof(deviceId));

			var device = await _devicesRepository.GetByIdAsync(deviceId);
			if (device == null || device.DepartmentId != departmentId || (!includeDeleted && device.IsDeleted))
				throw new InvalidOperationException("The tracking device was not found for this department.");

			return device;
		}

		private async Task<UnitTrackingCredential> GetOwnedCredentialAsync(string credentialId, string deviceId)
		{
			if (string.IsNullOrWhiteSpace(credentialId))
				throw new ArgumentNullException(nameof(credentialId));

			var credential = await _credentialsRepository.GetByIdAsync(credentialId);
			if (credential == null ||
			    !string.Equals(
				    credential.UnitTrackingDeviceId,
				    deviceId,
				    StringComparison.OrdinalIgnoreCase))
				throw new InvalidOperationException("The tracking credential was not found for this device.");

			return credential;
		}

		private async Task<List<UnitTrackingCredential>> GetCredentialEntitiesAsync(string deviceId)
		{
			var credentials = await _credentialsRepository.GetAllByDeviceIdAsync(deviceId);
			return credentials?.ToList() ?? new List<UnitTrackingCredential>();
		}

		private async Task ValidateUnitOwnershipAsync(int unitId, int departmentId)
		{
			if (unitId <= 0)
				throw new ArgumentOutOfRangeException(nameof(unitId));

			var unit = await _unitsService.GetUnitByIdAsync(unitId);
			if (unit == null || unit.DepartmentId != departmentId)
				throw new InvalidOperationException("The selected Unit was not found for this department.");
		}

		private async Task RevokeCredentialsAsync(
			IEnumerable<UnitTrackingCredential> credentials,
			DateTime revokedOn,
			CancellationToken cancellationToken)
		{
			foreach (var credential in credentials.Where(item => !item.RevokedOn.HasValue))
			{
				credential.RevokedOn = revokedOn;
				await _credentialsRepository.UpdateAsync(credential, cancellationToken);
			}
		}

		private async Task InvalidateDeviceAndCredentialsAsync(
			UnitTrackingDevice oldIdentity,
			UnitTrackingDevice currentIdentity,
			IEnumerable<UnitTrackingCredential> credentials)
		{
			await _authenticationService.InvalidateDeviceAsync(oldIdentity);
			await _authenticationService.InvalidateDeviceAsync(currentIdentity);

			foreach (var credential in credentials)
				await _authenticationService.InvalidateCredentialAsync(credential.SecretHash);
		}

		private void PublishAudit(
			int departmentId,
			string userId,
			AuditLogTypes type,
			object before,
			object after)
		{
			_eventAggregator.SendMessage(new AuditEvent
			{
				DepartmentId = departmentId,
				UserId = userId,
				Type = type,
				Before = before == null ? null : JsonConvert.SerializeObject(before),
				After = after == null ? null : JsonConvert.SerializeObject(after),
				Successful = true,
				ServerName = Environment.MachineName
			});
		}

		private object DeviceAuditSnapshot(UnitTrackingDevice device)
		{
			if (device == null)
				return null;

			return new
			{
				device.UnitTrackingDeviceId,
				device.DepartmentId,
				device.UnitId,
				device.DisplayName,
				device.ManufacturerKey,
				device.ModelKey,
				device.TransportType,
				device.ProtocolKey,
				device.PayloadAdapterKey,
				DeviceIdentifier = _identifierService.Mask(device.DeviceIdentifier),
				SecondaryIdentifier = _identifierService.Mask(device.SecondaryIdentifier),
				device.IsEnabled,
				device.IsDeleted,
				device.SourcePriority,
				device.LastStatus,
				device.LastErrorCode,
				device.FirmwareVersion,
				device.CreatedByUserId,
				device.CreatedOn,
				device.UpdatedByUserId,
				device.UpdatedOn
			};
		}

		private static object CredentialAuditSnapshot(UnitTrackingCredential credential)
		{
			if (credential == null)
				return null;

			return new
			{
				credential.UnitTrackingCredentialId,
				credential.UnitTrackingDeviceId,
				credential.AuthMode,
				credential.HeaderName,
				credential.BasicUsername,
				credential.KeyPrefix,
				credential.ValidFrom,
				credential.ExpiresOn,
				credential.RevokedOn,
				credential.LastUsedOn,
				credential.CreatedByUserId,
				credential.CreatedOn
			};
		}

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

		private static UnitTrackingCredentialProvisionResult BuildProvisioningResult(
			UnitTrackingDevice device,
			UnitTrackingCredential credential,
			string token)
		{
			var mode = (UnitTrackingAuthMode)credential.AuthMode;
			var relativeEndpoint = mode == UnitTrackingAuthMode.CapabilityPath
				? $"/api/v4/unit-trackers/c/{Uri.EscapeDataString(token)}"
				: $"/api/v4/unit-trackers/{Uri.EscapeDataString(device.UnitTrackingDeviceId)}/positions";
			var configuredBaseUrl = UnitTrackingConfig.PublicHttpsBaseUrl?.Trim().TrimEnd('/');
			var baseUrl = Uri.TryCreate(configuredBaseUrl, UriKind.Absolute, out var publicUri) &&
			              string.Equals(publicUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
				? configuredBaseUrl
				: null;
			var result = new UnitTrackingCredentialProvisionResult
			{
				Credential = SanitizeCredential(credential),
				Token = token,
				EndpointUrl = string.IsNullOrWhiteSpace(baseUrl)
					? relativeEndpoint
					: baseUrl + relativeEndpoint
			};

			switch (mode)
			{
				case UnitTrackingAuthMode.Bearer:
					result.HeaderName = "Authorization";
					result.HeaderValue = $"Bearer {token}";
					break;
				case UnitTrackingAuthMode.Basic:
					result.HeaderName = "Authorization";
					result.BasicUsername = credential.BasicUsername;
					result.HeaderValue = "Basic " + Convert.ToBase64String(
						Encoding.UTF8.GetBytes($"{credential.BasicUsername}:{token}"));
					break;
				case UnitTrackingAuthMode.CustomHeader:
					result.HeaderName = credential.HeaderName;
					result.HeaderValue = token;
					break;
			}

			return result;
		}

		private static UnitTrackingDevice CopyCacheIdentity(UnitTrackingDevice device)
		{
			return new UnitTrackingDevice
			{
				UnitTrackingDeviceId = device.UnitTrackingDeviceId,
				ProtocolKey = device.ProtocolKey,
				DeviceIdentifier = device.DeviceIdentifier
			};
		}

		private static UnitTrackingDevice CopyForRebind(
			UnitTrackingDevice device,
			int newUnitId,
			string userId,
			DateTime now)
		{
			return new UnitTrackingDevice
			{
				UnitTrackingDeviceId = Guid.NewGuid().ToString(),
				DepartmentId = device.DepartmentId,
				UnitId = newUnitId,
				DisplayName = device.DisplayName,
				ManufacturerKey = device.ManufacturerKey,
				ModelKey = device.ModelKey,
				TransportType = device.TransportType,
				ProtocolKey = device.ProtocolKey,
				PayloadAdapterKey = device.PayloadAdapterKey,
				DeviceIdentifier = device.DeviceIdentifier,
				SecondaryIdentifier = device.SecondaryIdentifier,
				IsEnabled = true,
				IsDeleted = false,
				SourcePriority = device.SourcePriority,
				AllowedSourceCidrs = device.AllowedSourceCidrs,
				LastStatus = (int)UnitTrackingDeviceStatus.NeverSeen,
				FirmwareVersion = device.FirmwareVersion,
				CreatedByUserId = userId,
				CreatedOn = now
			};
		}

		private static int ValidateTransportType(int transportType)
		{
			if (!Enum.IsDefined(typeof(UnitTrackingTransportType), transportType) ||
			    transportType == (int)UnitTrackingTransportType.Unknown)
				throw new ArgumentOutOfRangeException(nameof(transportType));

			return transportType;
		}

		private static void ValidateAuthMode(
			UnitTrackingAuthMode authMode,
			string headerName,
			string basicUsername)
		{
			if (!Enum.IsDefined(typeof(UnitTrackingAuthMode), authMode) ||
			    authMode == UnitTrackingAuthMode.Unknown)
				throw new ArgumentOutOfRangeException(nameof(authMode));

			if (authMode == UnitTrackingAuthMode.CustomHeader && string.IsNullOrWhiteSpace(headerName))
				throw new ArgumentNullException(nameof(headerName));
			if (authMode == UnitTrackingAuthMode.CustomHeader &&
			    (headerName.Length > 128 || !headerName.All(IsHttpTokenCharacter)))
				throw new ArgumentException("The custom credential header name is invalid.", nameof(headerName));

			if (authMode == UnitTrackingAuthMode.Basic && string.IsNullOrWhiteSpace(basicUsername))
				throw new ArgumentNullException(nameof(basicUsername));
			if (authMode == UnitTrackingAuthMode.Basic && basicUsername.Trim().Length > 128)
				throw new ArgumentOutOfRangeException(
					nameof(basicUsername),
					"Basic authentication usernames cannot exceed 128 characters.");
		}

		private static bool IsHttpTokenCharacter(char character) =>
			char.IsLetterOrDigit(character) ||
			"!#$%&'*+-.^_`|~".Contains(character);

		private static void ValidateUserAndDepartment(string userId, int departmentId)
		{
			if (departmentId <= 0)
				throw new ArgumentOutOfRangeException(nameof(departmentId));
			if (string.IsNullOrWhiteSpace(userId))
				throw new ArgumentNullException(nameof(userId));
		}

		private static string NormalizeKey(string value) =>
			string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();

		private static string TrimToNull(string value) =>
			string.IsNullOrWhiteSpace(value) ? null : value.Trim();
	}
}
