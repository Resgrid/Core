using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Model.Tracking;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Models.v4.UnitTracking;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Administrative lifecycle and status operations for Unit hardware tracking bindings.
	/// </summary>
	[Route("api/v4")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class UnitTrackingDevicesController : V4AuthenticatedApiControllerbase
	{
		private readonly IUnitTrackingService _unitTrackingService;
		private readonly IUnitTrackingCatalogService _catalogService;
		private readonly IUnitTrackingStatusService _statusService;
		private readonly IUnitTrackingIdentifierService _identifierService;
		private readonly Resgrid.Model.Services.IAuthorizationService _authorizationService;

		public UnitTrackingDevicesController(
			IUnitTrackingService unitTrackingService,
			IUnitTrackingCatalogService catalogService,
			IUnitTrackingStatusService statusService,
			IUnitTrackingIdentifierService identifierService,
			Resgrid.Model.Services.IAuthorizationService authorizationService)
		{
			_unitTrackingService = unitTrackingService;
			_catalogService = catalogService;
			_statusService = statusService;
			_identifierService = identifierService;
			_authorizationService = authorizationService;
		}

		[HttpGet("unit-tracking/catalog")]
		[ProducesResponseType(typeof(IReadOnlyCollection<UnitTrackingCatalogProfileData>), StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Unit_View)]
		public async Task<ActionResult<IReadOnlyCollection<UnitTrackingCatalogProfileData>>> GetCatalog(
			CancellationToken cancellationToken)
		{
			var profiles = await _catalogService.GetProfilesAsync(cancellationToken);
			return Ok(profiles.Select(MapProfile).ToList());
		}

		[HttpGet("units/{unitId:int}/trackers")]
		[ProducesResponseType(typeof(IReadOnlyCollection<UnitTrackingDeviceData>), StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		[Authorize(Policy = ResgridResources.Unit_View)]
		public async Task<ActionResult<IReadOnlyCollection<UnitTrackingDeviceData>>> GetUnitTrackers(
			int unitId,
			CancellationToken cancellationToken)
		{
			if (!await _authorizationService.CanUserViewUnitAsync(UserId, unitId))
				return Forbid();

			var canManage = await _authorizationService.CanUserModifyUnitAsync(UserId, unitId);
			var devices = await _unitTrackingService.GetDevicesForUnitAsync(DepartmentId, unitId);
			var mapped = new List<UnitTrackingDeviceData>(devices.Count);
			foreach (var device in devices)
				mapped.Add(await MapDeviceAsync(device, canManage, false, cancellationToken));

			return Ok(mapped);
		}

		[HttpPost("units/{unitId:int}/trackers")]
		[ProducesResponseType(typeof(UnitTrackingDeviceData), StatusCodes.Status201Created)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		[Authorize(Policy = ResgridResources.Unit_Update)]
		public async Task<ActionResult<UnitTrackingDeviceData>> CreateTracker(
			int unitId,
			[FromBody] CreateUnitTrackingDeviceInput input,
			CancellationToken cancellationToken)
		{
			if (!await _authorizationService.CanUserModifyUnitAsync(UserId, unitId))
				return Forbid();
			if (input == null)
				return BadRequest();

			var profile = await GetSelectableProfileAsync(input.ProfileKey, cancellationToken);
			var profileError = ValidateProfileInput(profile, input.DeviceIdentifier);
			if (profileError != null)
				return BadRequest(new { error = profileError });

			try
			{
				var saved = await _unitTrackingService.CreateDeviceAsync(
					BuildDevice(unitId, input, profile),
					DepartmentId,
					UserId,
					cancellationToken);
				var response = await MapDeviceAsync(saved, true, false, cancellationToken);
				return CreatedAtAction(nameof(GetTracker), new { id = saved.UnitTrackingDeviceId }, response);
			}
			catch (ArgumentException ex)
			{
				return BadRequest(new { error = ex.Message });
			}
			catch (InvalidOperationException ex)
			{
				return BadRequest(new { error = ex.Message });
			}
		}

		[HttpGet("unit-trackers/{id}")]
		[ProducesResponseType(typeof(UnitTrackingDeviceData), StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.Unit_View)]
		public async Task<ActionResult<UnitTrackingDeviceData>> GetTracker(
			string id,
			CancellationToken cancellationToken)
		{
			var device = await _unitTrackingService.GetDeviceByIdAsync(id, DepartmentId);
			if (device == null)
				return NotFound();
			if (!await _authorizationService.CanUserViewUnitAsync(UserId, device.UnitId))
				return Forbid();

			var canManage = await _authorizationService.CanUserModifyUnitAsync(UserId, device.UnitId);
			return Ok(await MapDeviceAsync(device, canManage, true, cancellationToken));
		}

		[HttpPut("unit-trackers/{id}")]
		[ProducesResponseType(typeof(UnitTrackingDeviceData), StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.Unit_Update)]
		public async Task<ActionResult<UnitTrackingDeviceData>> UpdateTracker(
			string id,
			[FromBody] UpdateUnitTrackingDeviceInput input,
			CancellationToken cancellationToken)
		{
			var existing = await _unitTrackingService.GetDeviceByIdAsync(id, DepartmentId);
			if (existing == null)
				return NotFound();
			if (!await _authorizationService.CanUserModifyUnitAsync(UserId, existing.UnitId))
				return Forbid();
			if (input == null)
				return BadRequest();

			var profile = await GetSelectableProfileAsync(input.ProfileKey, cancellationToken);
			var profileError = ValidateProfileInput(profile, input.DeviceIdentifier);
			if (profileError != null)
				return BadRequest(new { error = profileError });

			ApplyUpdate(existing, input, profile);
			try
			{
				var saved = await _unitTrackingService.UpdateDeviceAsync(
					existing,
					DepartmentId,
					UserId,
					cancellationToken);
				return Ok(await MapDeviceAsync(saved, true, true, cancellationToken));
			}
			catch (ArgumentException ex)
			{
				return BadRequest(new { error = ex.Message });
			}
			catch (InvalidOperationException ex)
			{
				return BadRequest(new { error = ex.Message });
			}
		}

		[HttpPost("unit-trackers/{id}/credentials")]
		[ProducesResponseType(typeof(UnitTrackingCredentialProvisionData), StatusCodes.Status201Created)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.Unit_Update)]
		public async Task<ActionResult<UnitTrackingCredentialProvisionData>> CreateCredential(
			string id,
			[FromBody] CreateUnitTrackingCredentialInput input,
			CancellationToken cancellationToken)
		{
			var authorization = await AuthorizeDeviceUpdateAsync(id);
			if (authorization.Device == null)
				return NotFound();
			if (!authorization.Allowed)
				return Forbid();
			if (input == null ||
			    !Enum.IsDefined(typeof(UnitTrackingAuthMode), input.AuthMode) ||
			    input.AuthMode == (int)UnitTrackingAuthMode.Unknown)
				return BadRequest();

			var profile = await _catalogService.GetProfileAsync(
				authorization.Device.ModelKey,
				cancellationToken);
			var authMode = (UnitTrackingAuthMode)input.AuthMode;
			if (profile == null || !profile.SupportedAuthModes.Contains(authMode))
				return BadRequest(new { error = "The selected authentication mode is not supported by this profile." });

			try
			{
				var provisioned = await _unitTrackingService.CreateCredentialAsync(
					id,
					DepartmentId,
					authMode,
					UserId,
					input.HeaderName,
					input.BasicUsername,
					cancellationToken);
				SetOneTimeCredentialResponseHeaders();
				var response = BuildProvisioningResponse(provisioned);
				return CreatedAtAction(nameof(GetTracker), new { id }, response);
			}
			catch (ArgumentException ex)
			{
				return BadRequest(new { error = ex.Message });
			}
			catch (InvalidOperationException ex)
			{
				return BadRequest(new { error = ex.Message });
			}
		}

		[HttpPost("unit-trackers/{id}/credentials/{credentialId}/rotate")]
		[ProducesResponseType(typeof(UnitTrackingCredentialProvisionData), StatusCodes.Status201Created)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.Unit_Update)]
		public async Task<ActionResult<UnitTrackingCredentialProvisionData>> RotateCredential(
			string id,
			string credentialId,
			[FromBody] RotateUnitTrackingCredentialInput input,
			CancellationToken cancellationToken)
		{
			var authorization = await AuthorizeDeviceUpdateAsync(id);
			if (authorization.Device == null)
				return NotFound();
			if (!authorization.Allowed)
				return Forbid();
			if (input?.OverlapHours is < 0 or > 168)
				return BadRequest();

			try
			{
				var provisioned = await _unitTrackingService.RotateCredentialAsync(
					id,
					credentialId,
					DepartmentId,
					UserId,
					input?.OverlapHours.HasValue == true
						? TimeSpan.FromHours(input.OverlapHours.Value)
						: null,
					cancellationToken);
				SetOneTimeCredentialResponseHeaders();
				var response = BuildProvisioningResponse(provisioned);
				return CreatedAtAction(nameof(GetTracker), new { id }, response);
			}
			catch (ArgumentException ex)
			{
				return BadRequest(new { error = ex.Message });
			}
			catch (InvalidOperationException ex)
			{
				return BadRequest(new { error = ex.Message });
			}
		}

		[HttpPost("unit-trackers/{id}/credentials/{credentialId}/revoke")]
		[ProducesResponseType(typeof(UnitTrackingCredentialData), StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.Unit_Update)]
		public async Task<ActionResult<UnitTrackingCredentialData>> RevokeCredential(
			string id,
			string credentialId,
			CancellationToken cancellationToken)
		{
			var authorization = await AuthorizeDeviceUpdateAsync(id);
			if (authorization.Device == null)
				return NotFound();
			if (!authorization.Allowed)
				return Forbid();

			try
			{
				var revoked = await _unitTrackingService.RevokeCredentialAsync(
					id,
					credentialId,
					DepartmentId,
					UserId,
					cancellationToken);
				return Ok(MapCredential(revoked));
			}
			catch (InvalidOperationException)
			{
				return NotFound();
			}
		}

		[HttpPost("unit-trackers/{id}/disable")]
		[ProducesResponseType(typeof(UnitTrackingDeviceData), StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.Unit_Update)]
		public async Task<ActionResult<UnitTrackingDeviceData>> DisableTracker(
			string id,
			CancellationToken cancellationToken)
		{
			var authorization = await AuthorizeDeviceUpdateAsync(id);
			if (authorization.Device == null)
				return NotFound();
			if (!authorization.Allowed)
				return Forbid();

			var disabled = await _unitTrackingService.DisableDeviceAsync(
				id,
				DepartmentId,
				UserId,
				cancellationToken);
			return Ok(await MapDeviceAsync(disabled, true, true, cancellationToken));
		}

		[HttpDelete("unit-trackers/{id}")]
		[ProducesResponseType(StatusCodes.Status204NoContent)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.Unit_Update)]
		public async Task<IActionResult> DeleteTracker(
			string id,
			CancellationToken cancellationToken)
		{
			var authorization = await AuthorizeDeviceUpdateAsync(id);
			if (authorization.Device == null)
				return NotFound();
			if (!authorization.Allowed)
				return Forbid();

			await _unitTrackingService.DeleteDeviceAsync(
				id,
				DepartmentId,
				UserId,
				cancellationToken);
			return NoContent();
		}

		[HttpPost("unit-trackers/{id}/rebind")]
		[ProducesResponseType(typeof(UnitTrackingDeviceData), StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.Unit_Update)]
		public async Task<ActionResult<UnitTrackingDeviceData>> RebindTracker(
			string id,
			[FromBody] RebindUnitTrackingDeviceInput input,
			CancellationToken cancellationToken)
		{
			var authorization = await AuthorizeDeviceUpdateAsync(id);
			if (authorization.Device == null)
				return NotFound();
			if (!authorization.Allowed)
				return Forbid();
			if (input == null)
				return BadRequest();
			if (!await _authorizationService.CanUserModifyUnitAsync(UserId, input.UnitId))
				return Forbid();

			try
			{
				var rebound = await _unitTrackingService.RebindDeviceAsync(
					id,
					DepartmentId,
					input.UnitId,
					UserId,
					cancellationToken);
				return Ok(await MapDeviceAsync(rebound, true, false, cancellationToken));
			}
			catch (InvalidOperationException ex)
			{
				return BadRequest(new { error = ex.Message });
			}
			catch (ArgumentException ex)
			{
				return BadRequest(new { error = ex.Message });
			}
		}

		[HttpGet("unit-trackers/{id}/status")]
		[ProducesResponseType(typeof(UnitTrackingDeviceData), StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.Unit_View)]
		public async Task<ActionResult<UnitTrackingDeviceData>> GetStatus(
			string id,
			CancellationToken cancellationToken)
		{
			var device = await _unitTrackingService.GetDeviceByIdAsync(id, DepartmentId);
			if (device == null)
				return NotFound();
			if (!await _authorizationService.CanUserViewUnitAsync(UserId, device.UnitId))
				return Forbid();

			var canManage = await _authorizationService.CanUserModifyUnitAsync(UserId, device.UnitId);
			return Ok(await MapDeviceAsync(device, canManage, false, cancellationToken));
		}

		private async Task<(UnitTrackingDevice Device, bool Allowed)> AuthorizeDeviceUpdateAsync(
			string deviceId)
		{
			var device = await _unitTrackingService.GetDeviceByIdAsync(deviceId, DepartmentId);
			if (device == null)
				return (null, false);

			return (
				device,
				await _authorizationService.CanUserModifyUnitAsync(UserId, device.UnitId));
		}

		private async Task<UnitTrackingCatalogProfile> GetSelectableProfileAsync(
			string profileKey,
			CancellationToken cancellationToken)
		{
			var profile = await _catalogService.GetProfileAsync(profileKey, cancellationToken);
			return profile?.IsSelectable == true ? profile : null;
		}

		private static string ValidateProfileInput(
			UnitTrackingCatalogProfile profile,
			string deviceIdentifier)
		{
			if (profile == null)
				return "The selected tracking profile was not found.";
			if (profile.IdentifierRequired && string.IsNullOrWhiteSpace(deviceIdentifier))
				return "A device identifier is required for the selected tracking profile.";
			return null;
		}

		private static UnitTrackingDevice BuildDevice(
			int unitId,
			CreateUnitTrackingDeviceInput input,
			UnitTrackingCatalogProfile profile)
		{
			return new UnitTrackingDevice
			{
				UnitId = unitId,
				DisplayName = input.DisplayName,
				ManufacturerKey = profile.ManufacturerKey,
				ModelKey = profile.Key,
				TransportType = (int)profile.TransportType,
				ProtocolKey = profile.ProtocolKey,
				PayloadAdapterKey = profile.PayloadAdapterKey,
				DeviceIdentifier = input.DeviceIdentifier,
				SecondaryIdentifier = input.SecondaryIdentifier,
				IsEnabled = true,
				SourcePriority = input.SourcePriority,
				AllowedSourceCidrs = input.AllowedSourceCidrs,
				FirmwareVersion = input.FirmwareVersion
			};
		}

		private static void ApplyUpdate(
			UnitTrackingDevice device,
			UpdateUnitTrackingDeviceInput input,
			UnitTrackingCatalogProfile profile)
		{
			device.DisplayName = input.DisplayName;
			device.ManufacturerKey = profile.ManufacturerKey;
			device.ModelKey = profile.Key;
			device.TransportType = (int)profile.TransportType;
			device.ProtocolKey = profile.ProtocolKey;
			device.PayloadAdapterKey = profile.PayloadAdapterKey;
			device.DeviceIdentifier = input.DeviceIdentifier;
			device.SecondaryIdentifier = input.SecondaryIdentifier;
			device.IsEnabled = input.IsEnabled;
			device.SourcePriority = input.SourcePriority;
			device.AllowedSourceCidrs = input.AllowedSourceCidrs;
			device.FirmwareVersion = input.FirmwareVersion;
		}

		private async Task<UnitTrackingDeviceData> MapDeviceAsync(
			UnitTrackingDevice device,
			bool exposeIdentifier,
			bool includeCredentials,
			CancellationToken cancellationToken)
		{
			var status = await _statusService.GetEffectiveStatusAsync(
				device,
				cancellationToken: cancellationToken);
			var credentials = includeCredentials
				? await _unitTrackingService.GetCredentialsForDeviceAsync(
					device.UnitTrackingDeviceId,
					DepartmentId)
				: new List<UnitTrackingCredential>();

			return new UnitTrackingDeviceData
			{
				UnitTrackingDeviceId = device.UnitTrackingDeviceId,
				UnitId = device.UnitId,
				DisplayName = device.DisplayName,
				ManufacturerKey = device.ManufacturerKey,
				ModelKey = device.ModelKey,
				TransportType = device.TransportType,
				TransportTypeName = Enum.IsDefined(typeof(UnitTrackingTransportType), device.TransportType)
					? ((UnitTrackingTransportType)device.TransportType).ToString()
					: UnitTrackingTransportType.Unknown.ToString(),
				ProtocolKey = device.ProtocolKey,
				PayloadAdapterKey = device.PayloadAdapterKey,
				DeviceIdentifier = exposeIdentifier
					? device.DeviceIdentifier
					: _identifierService.Mask(device.DeviceIdentifier),
				SecondaryIdentifier = exposeIdentifier
					? device.SecondaryIdentifier
					: _identifierService.Mask(device.SecondaryIdentifier),
				IdentifierMasked = !exposeIdentifier,
				IsEnabled = device.IsEnabled,
				SourcePriority = device.SourcePriority,
				AllowedSourceCidrs = exposeIdentifier ? device.AllowedSourceCidrs : null,
				LastSeenOn = device.LastSeenOn,
				LastPositionOn = device.LastPositionOn,
				LastReceivedOn = device.LastReceivedOn,
				Status = (int)status,
				StatusName = status.ToString(),
				LastErrorCode = device.LastErrorCode,
				FirmwareVersion = device.FirmwareVersion,
				CreatedOn = device.CreatedOn,
				UpdatedOn = device.UpdatedOn,
				Credentials = credentials.Select(MapCredential).ToList()
			};
		}

		private UnitTrackingCredentialProvisionData BuildProvisioningResponse(
			UnitTrackingCredentialProvisionResult provisioned)
		{
			return new UnitTrackingCredentialProvisionData
			{
				Credential = MapCredential(provisioned.Credential),
				Token = provisioned.Token,
				EndpointUrl = provisioned.EndpointUrl,
				HeaderName = provisioned.HeaderName,
				HeaderValue = provisioned.HeaderValue,
				BasicUsername = provisioned.BasicUsername
			};
		}

		private void SetOneTimeCredentialResponseHeaders()
		{
			Response.Headers.CacheControl = "no-store, no-cache, max-age=0";
			Response.Headers.Pragma = "no-cache";
		}

		private static UnitTrackingCatalogProfileData MapProfile(
			UnitTrackingCatalogProfile profile) =>
			new()
			{
				Key = profile.Key,
				ManufacturerKey = profile.ManufacturerKey,
				ManufacturerName = profile.ManufacturerName,
				Model = profile.Model,
				TransportType = (int)profile.TransportType,
				TransportTypeName = profile.TransportType.ToString(),
				ProtocolKey = profile.ProtocolKey,
				PayloadAdapterKey = profile.PayloadAdapterKey,
				DecoderVariant = profile.DecoderVariant,
				SupportedTransports =
					profile.SupportedTransports,
				CertifiedTransports =
					profile.CertifiedTransports,
				CertificationStatus = (int)profile.CertificationStatus,
				CertificationStatusName = profile.CertificationStatus.ToString(),
				ProtocolDocumentVersion =
					profile.ProtocolDocumentVersion,
				IdentifierRequired = profile.IdentifierRequired,
				IsSelectable = profile.IsSelectable,
				SupportedAuthModes = profile.SupportedAuthModes
					.Select(mode => (int)mode)
					.ToList(),
				SetupSummary = profile.SetupSummary,
				RetryExpectation = profile.RetryExpectation
			};

		private static UnitTrackingCredentialData MapCredential(
			UnitTrackingCredential credential) =>
			new()
			{
				UnitTrackingCredentialId = credential.UnitTrackingCredentialId,
				AuthMode = credential.AuthMode,
				AuthModeName = Enum.IsDefined(typeof(UnitTrackingAuthMode), credential.AuthMode)
					? ((UnitTrackingAuthMode)credential.AuthMode).ToString()
					: UnitTrackingAuthMode.Unknown.ToString(),
				HeaderName = credential.HeaderName,
				BasicUsername = credential.BasicUsername,
				KeyPrefix = credential.KeyPrefix,
				ValidFrom = credential.ValidFrom,
				ExpiresOn = credential.ExpiresOn,
				RevokedOn = credential.RevokedOn,
				LastUsedOn = credential.LastUsedOn,
				CreatedOn = credential.CreatedOn
			};
	}
}
