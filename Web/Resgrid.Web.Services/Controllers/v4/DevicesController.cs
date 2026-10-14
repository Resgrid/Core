using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.Services;
using System.Threading.Tasks;
using Resgrid.Model;
using System;
using System.Web;
using Resgrid.Model.Providers;
using Resgrid.Web.Services.Models.v4.Device;
using Resgrid.Web.Services.Helpers;
using Resgrid.Model.Events;
using Resgrid.Framework;
using Resgrid.Web.Services.Controllers.Version3.Models.Devices;
using System.Threading;
using Resgrid.Web.Services.Filters;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Mobile or Tablet Device specific operations
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class DevicesController : V4AuthenticatedApiControllerbase
	{
		#region Members and Constructors
		private readonly IPushService _pushService;
		private readonly IDepartmentsService _departmentsService;
		private readonly ICqrsProvider _cqrsProvider;
		private readonly IUnitsService _unitsService;

		public DevicesController(IPushService pushService, ICqrsProvider cqrsProvider, IDepartmentsService departmentsService,
			IUnitsService unitsService)
		{
			_pushService = pushService;
			_cqrsProvider = cqrsProvider;
			_departmentsService = departmentsService;
			_unitsService = unitsService;
		}
		#endregion Members and Constructors

		/// <summary>
		/// Register a unit device to receive push notification from the Resgrid system
		/// </summary>
		/// <param name="registrationInput">Input to create the registration for</param>
		/// <returns>Result for the registration</returns>
		[HttpPost("RegisterUnitDevice")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		public async Task<ActionResult<PushRegistrationResult>> RegisterUnitDevice([FromBody] PushRegistrationInput registrationInput)
		{
			var result = new PushRegistrationResult();

			if (this.ModelState.IsValid)
			{
				try
				{
					if (registrationInput == null)
						return BadRequest();

					PushRegisterionEvent pushRegisterionEvent = new PushRegisterionEvent();
					pushRegisterionEvent.PushUriId = 0;
					pushRegisterionEvent.UserId = UserId;
					pushRegisterionEvent.PlatformType = registrationInput.Platform;

					if (!string.IsNullOrWhiteSpace(registrationInput.Prefix))
						pushRegisterionEvent.PushLocation = registrationInput.Prefix;
					else
						pushRegisterionEvent.PushLocation = "";

					pushRegisterionEvent.DepartmentId = DepartmentId;
					pushRegisterionEvent.DeviceId = registrationInput.Token;
					pushRegisterionEvent.Uuid = registrationInput.DeviceUuid;

					if (!String.IsNullOrWhiteSpace(registrationInput.UnitId) && registrationInput.UnitId != "0")
						pushRegisterionEvent.UnitId = int.Parse(registrationInput.UnitId);

					CqrsEvent registerUnitPushEvent = new CqrsEvent();
					registerUnitPushEvent.Type = (int)CqrsEventTypes.UnitPushRegistration;
					registerUnitPushEvent.Data = ObjectSerialization.Serialize(pushRegisterionEvent);

					await _cqrsProvider.EnqueueCqrsEventAsync(registerUnitPushEvent);

					result.Status = ResponseHelper.Queued;
				}
				catch (Exception ex)
				{
					result.Status = ResponseHelper.Failure;
					Framework.Logging.LogException(ex);

					return result;
				}
			}

			result.Id = "";
			result.PageSize = 0;
			ResponseHelper.PopulateV4ResponseData(result);

			return result;
		}

		/// <summary>
		/// Removed a Unit Push Notification support by PushUriId.
		/// </summary>
		/// <param name="deviceUuid">Input to deregister the device for</param>
		/// <returns>Result for the unregistration</returns>
		[HttpDelete("UnRegisterUnitDevice")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		public ActionResult<PushRegistrationResult> UnRegisterUnitDevice(string deviceUuid)
		{
			if (String.IsNullOrWhiteSpace(deviceUuid))
				return BadRequest();

			// Nothing left to remove: this cleared the device from the legacy Azure unit hub, which is gone. Kept so
			// older Unit app builds that still call it get a success rather than a 404.
			var result = new PushRegistrationResult();
			result.Status = ResponseHelper.Success;
			result.Id = "";
			result.PageSize = 0;
			ResponseHelper.PopulateV4ResponseData(result);

			return result;
		}

		/// <summary>
		/// Register a device to receive push notification from the Resgrid system
		/// </summary>
		/// <param name="registrationInput">Input to create the registration for</param>
		/// <returns>Result for the registration</returns>
		[HttpPost("RegisterDevice")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		public async Task<ActionResult<PushRegistrationResult>> RegisterDevice([FromBody] PushRegistrationInput registrationInput)
		{
			if (this.ModelState.IsValid)
			{
				var result = new PushRegistrationResult();

				try
				{
					if (registrationInput == null)
						return BadRequest();

					var push = new PushUri();
					push.UserId = UserId;
					push.PlatformType = registrationInput.Platform;

					if (!string.IsNullOrWhiteSpace(registrationInput.Prefix))
						push.PushLocation = registrationInput.Prefix;
					else
					{
						var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
						push.PushLocation = department.Code;
					}

					push.DeviceId = registrationInput.Token;
					push.Uuid = registrationInput.DeviceUuid;
					push.DepartmentId = DepartmentId;
					push.Source = registrationInput.Source;

					CqrsEvent registerUnitPushEvent = new CqrsEvent();
					registerUnitPushEvent.Type = (int)CqrsEventTypes.PushRegistration;
					registerUnitPushEvent.Data = ObjectSerialization.Serialize(push);

					await _cqrsProvider.EnqueueCqrsEventAsync(registerUnitPushEvent);

					result.Status = ResponseHelper.Queued;
				}
				catch (Exception ex)
				{
					result.Status = ResponseHelper.Failure;
					Framework.Logging.LogException(ex);

					return result;
				}

				return result;
			}

			return BadRequest();
		}

		/// <summary>
		/// Removed a Push Notification support by PushUriId.
		/// </summary>
		/// <param name="deviceUuid">Input to deregister the device for</param>
		/// <returns>Result for the unregistration</returns>
		[HttpDelete("UnRegisterDevice")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		public ActionResult<PushRegistrationResult> UnRegisterDevice(string deviceUuid)
		{
			if (String.IsNullOrWhiteSpace(deviceUuid))
				return BadRequest();

			// Nothing left to remove: this cleared the device from the legacy Azure user hub, which is gone. Kept so
			// older app builds that still call it get a success rather than a 404.
			var result = new PushRegistrationResult();
			result.Status = ResponseHelper.Success;
			result.Id = "";
			result.PageSize = 0;
			ResponseHelper.PopulateV4ResponseData(result);

			return result;
		}

		/// <summary>
		/// Takes a browser or desktop (Electron) push token off the web push channel it was registered on, so
		/// that browser stops receiving this user's (or unit's) pushes. Called when someone signs out of a
		/// browser or turns its notifications off. Runs at once, not through the queue.
		/// </summary>
		/// <param name="input">The token and where it was registered</param>
		/// <returns>Success when the token is no longer on the channel</returns>
		[HttpPost("UnRegisterWebPush")]
		// Part of signing out: a department operation lock must not leave a signed-out browser receiving pushes.
		[AllowDuringDepartmentLock]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		public async Task<ActionResult<PushRegistrationResult>> UnRegisterWebPush([FromBody] WebPushUnRegistrationInput input)
		{
			if (input == null)
				return BadRequest();

			return await UnRegisterPushToken(input.Token, input.Prefix, input.Source, input.UnitId, (int)Platforms.Web,
				_pushService.UnRegisterWebPush, _pushService.UnRegisterUnitWebPush);
		}

		/// <summary>
		/// Takes an iOS or Android push token off the native push channels it was registered on, so that device
		/// stops receiving this user's (or unit's) pushes. Called when someone signs out of the native app. Runs at
		/// once, not through the queue. Only this token is removed: if another device has registered since, its
		/// token stays.
		/// </summary>
		/// <param name="input">The token and where it was registered</param>
		/// <returns>Success when the token is no longer on the channels</returns>
		[HttpPost("UnRegisterNativePush")]
		// Part of signing out: a department operation lock must not leave a signed-out device receiving pushes.
		[AllowDuringDepartmentLock]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		public async Task<ActionResult<PushRegistrationResult>> UnRegisterNativePush([FromBody] NativePushUnRegistrationInput input)
		{
			if (input == null)
				return BadRequest();

			return await UnRegisterPushToken(input.Token, input.Prefix, input.Source, input.UnitId, null,
				_pushService.UnRegisterNativePush, _pushService.UnRegisterUnitNativePush);
		}

		private async Task<ActionResult<PushRegistrationResult>> UnRegisterPushToken(string token, string prefix, string source, string unitIdText,
			int? platform, Func<PushUri, Task<bool>> unRegisterUser, Func<PushUri, Task<bool>> unRegisterUnit)
		{
			if (String.IsNullOrWhiteSpace(token))
				return BadRequest();

			int? unitId = null;
			if (!String.IsNullOrWhiteSpace(unitIdText) && unitIdText != "0")
			{
				if (!int.TryParse(unitIdText, out var parsedUnitId))
					return BadRequest();

				unitId = parsedUnitId;
			}

			var result = new PushRegistrationResult();

			try
			{
				if (String.IsNullOrWhiteSpace(prefix))
				{
					var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
					prefix = department?.Code;
				}

				var push = new PushUri
				{
					UserId = UserId,
					DepartmentId = DepartmentId,
					PushLocation = prefix,
					DeviceId = token,
					Source = source
				};

				if (platform.HasValue)
					push.PlatformType = platform.Value;

				bool removed;
				if (unitId.HasValue)
				{
					// The unit subscriber is department-wide, not the caller's own, so only a unit in the
					// caller's department can be touched.
					var unit = await _unitsService.GetUnitByIdAsync(unitId.Value);
					if (unit == null || unit.DepartmentId != DepartmentId)
						return BadRequest();

					push.UnitId = unitId;
					removed = await unRegisterUnit(push);
				}
				else
				{
					removed = await unRegisterUser(push);
				}

				result.Status = removed ? ResponseHelper.Success : ResponseHelper.Failure;
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
				result.Status = ResponseHelper.Failure;
			}

			result.Id = "";
			result.PageSize = 0;
			ResponseHelper.PopulateV4ResponseData(result);

			return result;
		}
	}
}
