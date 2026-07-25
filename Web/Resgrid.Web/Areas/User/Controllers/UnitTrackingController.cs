using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Model.Tracking;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Models.UnitTracking;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Areas.User.Controllers
{
	[Area("User")]
	public class UnitTrackingController : SecureBaseController
	{
		private readonly IUnitTrackingService _unitTrackingService;
		private readonly IUnitTrackingCatalogService _catalogService;
		private readonly IUnitTrackingStatusService _statusService;
		private readonly IUnitTrackingIdentifierService _identifierService;
		private readonly IUnitsService _unitsService;
		private readonly Resgrid.Model.Services.IAuthorizationService _authorizationService;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.Units.Units> _localizer;

		public UnitTrackingController(
			IUnitTrackingService unitTrackingService,
			IUnitTrackingCatalogService catalogService,
			IUnitTrackingStatusService statusService,
			IUnitTrackingIdentifierService identifierService,
			IUnitsService unitsService,
			Resgrid.Model.Services.IAuthorizationService authorizationService,
			IStringLocalizer<Resgrid.Localization.Areas.User.Units.Units> localizer)
		{
			_unitTrackingService = unitTrackingService;
			_catalogService = catalogService;
			_statusService = statusService;
			_identifierService = identifierService;
			_unitsService = unitsService;
			_authorizationService = authorizationService;
			_localizer = localizer;
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Unit_View)]
		public async Task<IActionResult> Index(int unitId, CancellationToken cancellationToken)
		{
			var unit = await GetOwnedUnitAsync(unitId);
			if (unit == null)
				return Unauthorized();
			if (!await _authorizationService.CanUserViewUnitAsync(UserId, unitId))
				return Unauthorized();

			var canManage = await _authorizationService.CanUserModifyUnitAsync(UserId, unitId);
			var devices = await _unitTrackingService.GetDevicesForUnitAsync(DepartmentId, unitId);
			return View(new UnitTrackingIndexView
			{
				Unit = unit,
				CanManage = canManage,
				Devices = await MapDevicesAsync(devices, canManage, cancellationToken)
			});
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Unit_Update)]
		public async Task<IActionResult> New(int unitId, CancellationToken cancellationToken)
		{
			var unit = await GetOwnedUnitAsync(unitId);
			if (unit == null ||
			    !await _authorizationService.CanUserModifyUnitAsync(UserId, unitId))
				return Unauthorized();

			return View(await BuildEditorAsync(
				unit,
				new UnitTrackingEditorView
				{
					Unit = unit,
					IsEnabled = true,
					SourcePriority = 100
				},
				cancellationToken));
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Unit_Update)]
		public async Task<IActionResult> New(
			int unitId,
			UnitTrackingEditorView model,
			CancellationToken cancellationToken)
		{
			var unit = await GetOwnedUnitAsync(unitId);
			if (unit == null ||
			    !await _authorizationService.CanUserModifyUnitAsync(UserId, unitId))
				return Unauthorized();

			var profile = await _catalogService.GetProfileAsync(model.ProfileKey, cancellationToken);
			ValidateProfile(profile, model.DeviceIdentifier);
			if (!ModelState.IsValid)
				return View(await BuildEditorAsync(unit, model, cancellationToken));

			try
			{
				var saved = await _unitTrackingService.CreateDeviceAsync(
					BuildDevice(unitId, model, profile),
					DepartmentId,
					UserId,
					cancellationToken);
				TempData["UnitTrackingSuccess"] = _localizer["TrackingBindingCreatedMessage"].Value;
				return RedirectToAction(nameof(Details), new { id = saved.UnitTrackingDeviceId });
			}
			catch (ArgumentException ex)
			{
				ModelState.AddModelError(string.Empty, ex.Message);
				return View(await BuildEditorAsync(unit, model, cancellationToken));
			}
			catch (InvalidOperationException ex)
			{
				ModelState.AddModelError(string.Empty, ex.Message);
				return View(await BuildEditorAsync(unit, model, cancellationToken));
			}
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Unit_View)]
		public async Task<IActionResult> Details(string id, CancellationToken cancellationToken)
		{
			var context = await GetAuthorizedDeviceAsync(id, false);
			if (context.Device == null || !context.Allowed)
				return Unauthorized();

			var credentials = await _unitTrackingService.GetCredentialsForDeviceAsync(
				id,
				DepartmentId);
			var profile = await _catalogService.GetProfileAsync(
				context.Device.ModelKey,
				cancellationToken);
			return View(new UnitTrackingDetailsView
			{
				Unit = context.Unit,
				Device = context.Device,
				Profile = profile,
				Status = await _statusService.GetEffectiveStatusAsync(
					context.Device,
					cancellationToken: cancellationToken),
				DisplayIdentifier = context.CanManage
					? context.Device.DeviceIdentifier
					: _identifierService.Mask(context.Device.DeviceIdentifier),
				CanManage = context.CanManage,
				CanPreviewJson =
					SystemBehaviorConfig.Environment != SystemEnvironment.Prod &&
					ClaimsAuthorizationHelper.IsUserDepartmentAdmin(),
				SupportedAuthModes = profile?.SupportedAuthModes ??
					Array.Empty<UnitTrackingAuthMode>(),
				Credentials = credentials,
				CredentialInput = new CreateUnitTrackingCredentialView
				{
					UnitTrackingDeviceId = id,
					AuthMode = (int)(profile?.SupportedAuthModes.FirstOrDefault() ??
						UnitTrackingAuthMode.Bearer)
				}
			});
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Unit_Update)]
		public async Task<IActionResult> Edit(string id, CancellationToken cancellationToken)
		{
			var context = await GetAuthorizedDeviceAsync(id, true);
			if (context.Device == null || !context.Allowed)
				return Unauthorized();

			return View(await BuildEditorAsync(
				context.Unit,
				new UnitTrackingEditorView
				{
					Unit = context.Unit,
					UnitTrackingDeviceId = id,
					IsEdit = true,
					ProfileKey = context.Device.ModelKey,
					DisplayName = context.Device.DisplayName,
					DeviceIdentifier = context.Device.DeviceIdentifier,
					SecondaryIdentifier = context.Device.SecondaryIdentifier,
					IsEnabled = context.Device.IsEnabled,
					SourcePriority = context.Device.SourcePriority,
					AllowedSourceCidrs = context.Device.AllowedSourceCidrs,
					FirmwareVersion = context.Device.FirmwareVersion
				},
				cancellationToken));
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Unit_Update)]
		public async Task<IActionResult> Edit(
			string id,
			UnitTrackingEditorView model,
			CancellationToken cancellationToken)
		{
			var context = await GetAuthorizedDeviceAsync(id, true);
			if (context.Device == null || !context.Allowed)
				return Unauthorized();

			var profile = await _catalogService.GetProfileAsync(model.ProfileKey, cancellationToken);
			ValidateProfile(profile, model.DeviceIdentifier);
			if (!ModelState.IsValid)
				return View(await BuildEditorAsync(context.Unit, model, cancellationToken));

			var disableRequested = context.Device.IsEnabled && !model.IsEnabled;
			ApplyUpdate(context.Device, model, profile);
			try
			{
				await _unitTrackingService.UpdateDeviceAsync(
					context.Device,
					DepartmentId,
					UserId,
					cancellationToken);
				TempData["UnitTrackingSuccess"] = _localizer[
					disableRequested
						? "TrackingBindingDisabledMessage"
						: "TrackingBindingUpdatedMessage"].Value;
				return RedirectToAction(nameof(Details), new { id });
			}
			catch (ArgumentException ex)
			{
				ModelState.AddModelError(string.Empty, ex.Message);
				return View(await BuildEditorAsync(context.Unit, model, cancellationToken));
			}
			catch (InvalidOperationException ex)
			{
				ModelState.AddModelError(string.Empty, ex.Message);
				return View(await BuildEditorAsync(context.Unit, model, cancellationToken));
			}
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Unit_Update)]
		public async Task<IActionResult> CreateCredential(
			CreateUnitTrackingCredentialView model,
			CancellationToken cancellationToken)
		{
			var context = await GetAuthorizedDeviceAsync(model.UnitTrackingDeviceId, true);
			if (context.Device == null || !context.Allowed)
				return Unauthorized();

			var profile = await _catalogService.GetProfileAsync(
				context.Device.ModelKey,
				cancellationToken);
			var authMode = (UnitTrackingAuthMode)model.AuthMode;
			if (profile == null || !profile.SupportedAuthModes.Contains(authMode))
				ModelState.AddModelError(
					nameof(model.AuthMode),
					_localizer["UnsupportedTrackingAuth"].Value);
			if (authMode == UnitTrackingAuthMode.CustomHeader &&
			    string.IsNullOrWhiteSpace(model.HeaderName))
				ModelState.AddModelError(
					nameof(model.HeaderName),
					_localizer["CustomHeaderRequired"].Value);
			if (authMode == UnitTrackingAuthMode.Basic &&
			    string.IsNullOrWhiteSpace(model.BasicUsername))
				ModelState.AddModelError(
					nameof(model.BasicUsername),
					_localizer["BasicUsernameRequired"].Value);

			if (!ModelState.IsValid)
			{
				TempData["UnitTrackingError"] =
					string.Join(" ", ModelState.Values.SelectMany(value => value.Errors)
						.Select(error => error.ErrorMessage));
				return RedirectToAction(nameof(Details), new { id = model.UnitTrackingDeviceId });
			}

			try
			{
				var provisioned = await _unitTrackingService.CreateCredentialAsync(
					context.Device.UnitTrackingDeviceId,
					DepartmentId,
					authMode,
					UserId,
					model.HeaderName,
					model.BasicUsername,
					cancellationToken);
				return OneTimeCredential(context.Unit, context.Device, profile, provisioned);
			}
			catch (ArgumentException ex)
			{
				TempData["UnitTrackingError"] = ex.Message;
				return RedirectToAction(nameof(Details), new { id = model.UnitTrackingDeviceId });
			}
			catch (InvalidOperationException ex)
			{
				TempData["UnitTrackingError"] = ex.Message;
				return RedirectToAction(nameof(Details), new { id = model.UnitTrackingDeviceId });
			}
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Unit_Update)]
		public async Task<IActionResult> RotateCredential(
			RotateUnitTrackingCredentialView model,
			CancellationToken cancellationToken)
		{
			var context = await GetAuthorizedDeviceAsync(model.UnitTrackingDeviceId, true);
			if (context.Device == null || !context.Allowed)
				return Unauthorized();
			if (!ModelState.IsValid)
				return RedirectToAction(nameof(Details), new { id = model.UnitTrackingDeviceId });

			try
			{
				var provisioned = await _unitTrackingService.RotateCredentialAsync(
					context.Device.UnitTrackingDeviceId,
					model.UnitTrackingCredentialId,
					DepartmentId,
					UserId,
					TimeSpan.FromHours(model.OverlapHours),
					cancellationToken);
				var profile = await _catalogService.GetProfileAsync(
					context.Device.ModelKey,
					cancellationToken);
				return OneTimeCredential(context.Unit, context.Device, profile, provisioned);
			}
			catch (InvalidOperationException ex)
			{
				TempData["UnitTrackingError"] = ex.Message;
				return RedirectToAction(nameof(Details), new { id = model.UnitTrackingDeviceId });
			}
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Unit_Update)]
		public async Task<IActionResult> RevokeCredential(
			string id,
			string credentialId,
			CancellationToken cancellationToken)
		{
			var context = await GetAuthorizedDeviceAsync(id, true);
			if (context.Device == null || !context.Allowed)
				return Unauthorized();

			await _unitTrackingService.RevokeCredentialAsync(
				id,
				credentialId,
				DepartmentId,
				UserId,
				cancellationToken);
			TempData["UnitTrackingSuccess"] = _localizer["TrackingCredentialRevokedMessage"].Value;
			return RedirectToAction(nameof(Details), new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Unit_Update)]
		public async Task<IActionResult> Disable(
			string id,
			CancellationToken cancellationToken)
		{
			var context = await GetAuthorizedDeviceAsync(id, true);
			if (context.Device == null || !context.Allowed)
				return Unauthorized();

			await _unitTrackingService.DisableDeviceAsync(
				id,
				DepartmentId,
				UserId,
				cancellationToken);
			TempData["UnitTrackingSuccess"] = _localizer["TrackingBindingDisabledMessage"].Value;
			return RedirectToAction(nameof(Details), new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Unit_Update)]
		public async Task<IActionResult> Delete(
			string id,
			CancellationToken cancellationToken)
		{
			var context = await GetAuthorizedDeviceAsync(id, true);
			if (context.Device == null || !context.Allowed)
				return Unauthorized();

			await _unitTrackingService.DeleteDeviceAsync(
				id,
				DepartmentId,
				UserId,
				cancellationToken);
			TempData["UnitTrackingSuccess"] = _localizer["TrackingBindingDeletedMessage"].Value;
			return RedirectToAction(nameof(Index), new { unitId = context.Device.UnitId });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Unit_Update)]
		public async Task<IActionResult> PreviewJson(
			PreviewUnitTrackingJsonView model,
			CancellationToken cancellationToken)
		{
			if (SystemBehaviorConfig.Environment == SystemEnvironment.Prod ||
			    !ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				return NotFound();

			if (model == null || string.IsNullOrWhiteSpace(model.UnitTrackingDeviceId))
				return BadRequest();

			var context = await GetAuthorizedDeviceAsync(model.UnitTrackingDeviceId, true);
			if (context.Device == null || !context.Allowed)
				return Unauthorized();

			var preview = ValidatePreviewJson(model.JsonPayload);
			TempData["UnitTrackingPreview"] = preview.IsValid
				? _localizer["PreviewJsonSuccess", preview.PositionCount].Value
				: _localizer[preview.ErrorKey].Value;
			return RedirectToAction(
				nameof(Details),
				new { id = model.UnitTrackingDeviceId });
		}

		private IActionResult OneTimeCredential(
			Unit unit,
			UnitTrackingDevice device,
			UnitTrackingCatalogProfile profile,
			UnitTrackingCredentialProvisionResult provisioned)
		{
			Response.Headers.CacheControl = "no-store, no-cache, max-age=0";
			Response.Headers.Pragma = "no-cache";
			return View("Credential", new UnitTrackingCredentialDisplayView
			{
				Unit = unit,
				Device = device,
				Profile = profile,
				Provisioning = provisioned
			});
		}

		private async Task<Unit> GetOwnedUnitAsync(int unitId)
		{
			var unit = await _unitsService.GetUnitByIdAsync(unitId);
			return unit?.DepartmentId == DepartmentId ? unit : null;
		}

		private async Task<(UnitTrackingDevice Device, Unit Unit, bool Allowed, bool CanManage)>
			GetAuthorizedDeviceAsync(string deviceId, bool requireManage)
		{
			var device = await _unitTrackingService.GetDeviceByIdAsync(deviceId, DepartmentId);
			if (device == null)
				return (null, null, false, false);

			var unit = await GetOwnedUnitAsync(device.UnitId);
			if (unit == null)
				return (null, null, false, false);

			var canManage = await _authorizationService.CanUserModifyUnitAsync(UserId, device.UnitId);
			var allowed = requireManage
				? canManage
				: await _authorizationService.CanUserViewUnitAsync(UserId, device.UnitId);
			return (device, unit, allowed, canManage);
		}

		private async Task<IReadOnlyCollection<UnitTrackingDeviceStatusView>> MapDevicesAsync(
			IReadOnlyCollection<UnitTrackingDevice> devices,
			bool exposeIdentifier,
			CancellationToken cancellationToken)
		{
			var mapped = new List<UnitTrackingDeviceStatusView>(devices.Count);
			foreach (var device in devices)
			{
				mapped.Add(new UnitTrackingDeviceStatusView
				{
					Device = device,
					Status = await _statusService.GetEffectiveStatusAsync(
						device,
						cancellationToken: cancellationToken),
					DisplayIdentifier = exposeIdentifier
						? device.DeviceIdentifier
						: _identifierService.Mask(device.DeviceIdentifier)
				});
			}

			return mapped;
		}

		private static (bool IsValid, int PositionCount, string ErrorKey) ValidatePreviewJson(
			string json)
		{
			if (string.IsNullOrWhiteSpace(json))
				return (false, 0, "PreviewJsonRequired");
			if (Encoding.UTF8.GetByteCount(json) > Math.Max(1, UnitTrackingConfig.MaxRequestBytes))
				return (false, 0, "PreviewJsonTooLarge");

			JObject root;
			try
			{
				using var stringReader = new StringReader(json);
				using var jsonReader = new JsonTextReader(stringReader)
				{
					DateParseHandling = DateParseHandling.None,
					FloatParseHandling = FloatParseHandling.Decimal,
					MaxDepth = Math.Max(1, UnitTrackingConfig.MaxJsonDepth)
				};
				var token = JToken.ReadFrom(
					jsonReader,
					new JsonLoadSettings
					{
						DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
						LineInfoHandling = LineInfoHandling.Ignore
					});
				if (token is not JObject parsedRoot || HasTrailingJsonContent(jsonReader))
					return (false, 0, "PreviewJsonMalformed");
				root = parsedRoot;
			}
			catch (Exception ex) when (ex is JsonException || ex is ArgumentException)
			{
				return (false, 0, "PreviewJsonMalformed");
			}

			IReadOnlyCollection<JObject> positions;
			if (root.TryGetValue(
				    "positions",
				    StringComparison.OrdinalIgnoreCase,
				    out var positionsToken))
			{
				if (positionsToken is not JArray positionArray ||
				    positionArray.Count == 0 ||
				    positionArray.Any(item => item is not JObject))
					return (false, 0, "PreviewJsonPositionsRequired");
				if (positionArray.Count > Math.Max(1, UnitTrackingConfig.MaxBatchPositions))
					return (false, 0, "PreviewJsonTooManyPositions");
				positions = positionArray.Cast<JObject>().ToList();
			}
			else
			{
				positions = new[] { root };
			}

			foreach (var position in positions)
			{
				if (!TryGetNonEmptyString(position, "eventId") ||
				    !TryGetCoordinate(position, "latitude", -90m, 90m) ||
				    !TryGetCoordinate(position, "longitude", -180m, 180m))
					return (false, 0, "PreviewJsonPositionInvalid");
			}

			return (true, positions.Count, null);
		}

		private static bool HasTrailingJsonContent(JsonTextReader reader)
		{
			while (reader.Read())
			{
				if (reader.TokenType != JsonToken.Comment)
					return true;
			}

			return false;
		}

		private static bool TryGetNonEmptyString(JObject value, string propertyName) =>
			value.TryGetValue(propertyName, StringComparison.OrdinalIgnoreCase, out var token) &&
			token.Type == JTokenType.String &&
			!string.IsNullOrWhiteSpace(token.Value<string>());

		private static bool TryGetCoordinate(
			JObject value,
			string propertyName,
			decimal minimum,
			decimal maximum)
		{
			if (!value.TryGetValue(propertyName, StringComparison.OrdinalIgnoreCase, out var token) ||
			    token.Type is not (JTokenType.Integer or JTokenType.Float))
				return false;

			var coordinate = token.Value<decimal>();
			return coordinate >= minimum && coordinate <= maximum;
		}

		private async Task<UnitTrackingEditorView> BuildEditorAsync(
			Unit unit,
			UnitTrackingEditorView model,
			CancellationToken cancellationToken)
		{
			model.Unit = unit;
			model.Profiles = (await _catalogService.GetProfilesAsync(cancellationToken))
				.Where(profile => profile.IsSelectable)
				.ToList();
			return model;
		}

		private void ValidateProfile(
			UnitTrackingCatalogProfile profile,
			string deviceIdentifier)
		{
			if (profile?.IsSelectable != true)
				ModelState.AddModelError(
					nameof(UnitTrackingEditorView.ProfileKey),
					_localizer["SelectValidTrackingProfile"].Value);
			else if (profile.IdentifierRequired && string.IsNullOrWhiteSpace(deviceIdentifier))
				ModelState.AddModelError(
					nameof(UnitTrackingEditorView.DeviceIdentifier),
					_localizer["TrackingIdentifierRequired"].Value);
		}

		private static UnitTrackingDevice BuildDevice(
			int unitId,
			UnitTrackingEditorView model,
			UnitTrackingCatalogProfile profile) =>
			new()
			{
				UnitId = unitId,
				DisplayName = model.DisplayName,
				ManufacturerKey = profile.ManufacturerKey,
				ModelKey = profile.Key,
				TransportType = (int)profile.TransportType,
				ProtocolKey = profile.ProtocolKey,
				PayloadAdapterKey = profile.PayloadAdapterKey,
				DeviceIdentifier = model.DeviceIdentifier,
				SecondaryIdentifier = model.SecondaryIdentifier,
				IsEnabled = true,
				SourcePriority = model.SourcePriority,
				AllowedSourceCidrs = model.AllowedSourceCidrs,
				FirmwareVersion = model.FirmwareVersion
			};

		private static void ApplyUpdate(
			UnitTrackingDevice device,
			UnitTrackingEditorView model,
			UnitTrackingCatalogProfile profile)
		{
			device.DisplayName = model.DisplayName;
			device.ManufacturerKey = profile.ManufacturerKey;
			device.ModelKey = profile.Key;
			device.TransportType = (int)profile.TransportType;
			device.ProtocolKey = profile.ProtocolKey;
			device.PayloadAdapterKey = profile.PayloadAdapterKey;
			device.DeviceIdentifier = model.DeviceIdentifier;
			device.SecondaryIdentifier = model.SecondaryIdentifier;
			device.IsEnabled = model.IsEnabled;
			device.SourcePriority = model.SourcePriority;
			device.AllowedSourceCidrs = model.AllowedSourceCidrs;
			device.FirmwareVersion = model.FirmwareVersion;
		}
	}
}
