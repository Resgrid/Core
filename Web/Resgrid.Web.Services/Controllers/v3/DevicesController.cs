using System;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Controllers.Version3.Models.Devices;

namespace Resgrid.Web.Services.Controllers.Version3
{
	/// <summary>
	/// Operations to perform against devices for personnel
	/// </summary>
	[Route("api/v{version:ApiVersion}/[controller]")]
	[Produces("application/json")]
	[ApiVersion("3.0")]
	[ApiExplorerSettings(GroupName = "v3")]
	public class DevicesController : V3AuthenticatedApiControllerbase
	{
		private readonly IUsersService _usersService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IAuthorizationService _authorizationService;
		private readonly IPushService _pushService;
		private readonly ICqrsProvider _cqrsProvider;

		/// <summary>
		/// Default Constructor
		/// </summary>
		/// <param name="usersService"></param>
		/// <param name="membershipProvider"></param>
		/// <param name="departmentsService"></param>
		/// <param name="authorizationService"></param>
		/// <param name="pushUriService"></param>
		public DevicesController(
			IUsersService usersService,
			IDepartmentsService departmentsService,
			IAuthorizationService authorizationService,
			IPushService pushService,
			ICqrsProvider cqrsProvider
			)
		{
			_usersService = usersService;
			_departmentsService = departmentsService;
			_authorizationService = authorizationService;
			_pushService = pushService;
			_cqrsProvider = cqrsProvider;
		}

		/// <summary>
		/// Register a device to receive push notification from the Resgrid system
		/// </summary>
		/// <param name="registrationInput">Input to create the registration for</param>
		/// <returns>Result for the registration</returns>
		[HttpPost("RegisterDevice")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		public async Task<ActionResult<DeviceRegistrationResult>> RegisterDevice([FromBody] DeviceRegistrationInput registrationInput, CancellationToken cancellationToken)
		{
			if (this.ModelState.IsValid)
			{
				var result = new DeviceRegistrationResult();

				try
				{
					if (registrationInput == null)
						return BadRequest();

					var push = new PushUri();
					push.UserId = UserId;
					push.PlatformType = registrationInput.Plt;

					if (!String.IsNullOrWhiteSpace(registrationInput.Uri))
						push.PushLocation = HttpUtility.UrlDecode(registrationInput.Uri);
					else if (registrationInput.Plt == (int)Platforms.Android)
						push.PushLocation = "Android";
					else if (registrationInput.Plt == (int)Platforms.iPhone || registrationInput.Plt == (int)Platforms.iPad)
						push.PushLocation = "Apple";

					push.DeviceId = registrationInput.Did;
					push.Uuid = registrationInput.Id;
					push.DepartmentId = DepartmentId;

					CqrsEvent registerUnitPushEvent = new CqrsEvent();
					registerUnitPushEvent.Type = (int)CqrsEventTypes.PushRegistration;
					registerUnitPushEvent.Data = ObjectSerialization.Serialize(push);

					await _cqrsProvider.EnqueueCqrsEventAsync(registerUnitPushEvent);

					result.Sfl = true;
					result.Id = push.PushUriId;

					return result;
				}
				catch (Exception ex)
				{
					result.Sfl = false;
					Framework.Logging.LogException(ex);

					return result;
				}
			}

			return BadRequest();
		}

		/// <summary>
		/// Removed a Push Notification support by PushUriId.
		/// </summary>
		/// <param name="input">Input to de-register the device for</param>
		/// <returns></returns>
		[HttpDelete("UnRegisterDevice")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		public async Task<ActionResult> UnRegisterDevice([FromQuery] DeviceUnRegistrationInput input)
		{
			if (input == null)
				return BadRequest();

			if (this.ModelState.IsValid)
			{
				try
				{
					if (!String.IsNullOrWhiteSpace(input.Did))
					{
						var deviceId = HttpUtility.UrlDecode(input.Did);
						await _pushService.UnRegister(new PushUri() {UserId = UserId, DeviceId = deviceId, PushUriId = input.Pid});
					}

					return Ok();
				}
				catch (Exception ex)
				{
					Framework.Logging.LogException(ex);
					return BadRequest();
				}
			}

			return BadRequest();
		}


		/// <summary>
		/// Register a unit device to receive push notification from the Resgrid system
		/// </summary>
		/// <param name="registrationInput">Input to create the registration for</param>
		/// <returns>Result for the registration</returns>
		[HttpPost("RegisterUnitDevice")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		public async Task<ActionResult<DeviceRegistrationResult>> RegisterUnitDevice([FromBody] DeviceRegistrationInput registrationInput)
		{
			if (this.ModelState.IsValid)
			{
				var result = new DeviceRegistrationResult();

				try
				{
					if (registrationInput == null)
						return BadRequest();

					var push = new PushUri();
					push.UserId = UserId;
					push.PlatformType = registrationInput.Plt;
					push.PushLocation = registrationInput.Id;
					push.DepartmentId = DepartmentId;

					if (!String.IsNullOrWhiteSpace(registrationInput.Uri))
						push.PushLocation = HttpUtility.UrlDecode(registrationInput.Uri);

					push.DeviceId = registrationInput.Did;

					if (registrationInput.Uid != 0)
						push.UnitId = registrationInput.Uid;

					PushRegisterionEvent pushRegisterionEvent = new PushRegisterionEvent();
					pushRegisterionEvent.PushUriId = push.PushUriId;
					pushRegisterionEvent.UserId = UserId;
					pushRegisterionEvent.PlatformType = registrationInput.Plt;
					pushRegisterionEvent.PushLocation = registrationInput.Id;
					pushRegisterionEvent.DepartmentId = DepartmentId;
					pushRegisterionEvent.DeviceId = registrationInput.Did;
					pushRegisterionEvent.Uuid = registrationInput.Id;

					if (registrationInput.Uid != 0)
						pushRegisterionEvent.UnitId = registrationInput.Uid;

					CqrsEvent registerUnitPushEvent = new CqrsEvent();
					registerUnitPushEvent.Type = (int)CqrsEventTypes.UnitPushRegistration;
					registerUnitPushEvent.Data = ObjectSerialization.Serialize(pushRegisterionEvent);

					await _cqrsProvider.EnqueueCqrsEventAsync(registerUnitPushEvent);

					//await _pushService.RegisterUnit(push);

					result.Sfl = true;
					result.Id = push.PushUriId;

					return Ok(result);
				}
				catch (Exception ex)
				{
					result.Sfl = false;
					Framework.Logging.LogException(ex);

					return result;
				}
			}

			return BadRequest();
		}

		/// <summary>
		/// Removed a Unit Push Notification support by PushUriId.
		/// </summary>
		/// <param name="input">Input to deregister the device for</param>
		/// <returns></returns>
		[HttpDelete("UnRegisterUnitDevice")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		public async Task<ActionResult> UnRegisterUnitDevice([FromQuery] DeviceUnRegistrationInput input)
		{
			if (input == null)
				return BadRequest();

			if (this.ModelState.IsValid)
			{
				try
				{
					if (!String.IsNullOrWhiteSpace(input.Did))
					{
						var deviceId = HttpUtility.UrlDecode(input.Did);
					
						await _pushService.UnRegisterUnit(new PushUri() {UserId = UserId, DeviceId = deviceId, PushUriId = input.Pid});
					}

					return Ok();
				}
				catch (Exception ex)
				{
					Framework.Logging.LogException(ex);
					return BadRequest();
				}
			}

			return BadRequest();
		}
	}
}
