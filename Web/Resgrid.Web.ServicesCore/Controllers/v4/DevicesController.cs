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
		private readonly ICqrsProvider _cqrsProvider;

		public DevicesController(IPushService pushService, ICqrsProvider cqrsProvider)
		{
			_pushService = pushService;
			_cqrsProvider = cqrsProvider;
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
		/// <returns></returns>
		[HttpDelete("UnRegisterUnitDevice")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		public async Task<ActionResult<PushRegistrationResult>> UnRegisterUnitDevice(string deviceUuid)
		{
			if (String.IsNullOrWhiteSpace(deviceUuid))
				return BadRequest();

			var result = new PushRegistrationResult();

			try
			{
				var deviceId = HttpUtility.UrlDecode(deviceUuid);

				await _pushService.UnRegisterUnit(new PushUri() { UserId = UserId, DeviceId = deviceId });
				result.Status = ResponseHelper.Success;
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
