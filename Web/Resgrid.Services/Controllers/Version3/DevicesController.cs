using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;
using System.Web.Http;
using System.Web.Http.Cors;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Controllers.Version3.Models.Devices;
using Resgrid.Web.Services.Helpers;

namespace Resgrid.Web.Services.Controllers.Version3
{
	/// <summary>
	/// Operations to perform against devices for personnel
	/// </summary>
	[EnableCors(origins: "*", headers: "*", methods: "GET,POST,PUT,DELETE,OPTIONS")]
	public class DevicesController : V3AuthenticatedApiControllerbase
	{
		private readonly IUsersService _usersService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IAuthorizationService _authorizationService;
		private readonly IPushUriService _pushUriService;
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
			IPushUriService pushUriService,
			IPushService pushService,
			ICqrsProvider cqrsProvider
			)
		{
			_usersService = usersService;
			_departmentsService = departmentsService;
			_authorizationService = authorizationService;
			_pushUriService = pushUriService;
			_pushService = pushService;
			_cqrsProvider = cqrsProvider;
		}

		/// <summary>
		/// Register a device to receive push notification from the Resgrid system
		/// </summary>
		/// <param name="registrationInput">Input to create the registration for</param>
		/// <returns>Result for the registration</returns>
		[System.Web.Http.AcceptVerbs("POST")]
		public async Task<DeviceRegistrationResult> RegisterDevice([FromBody] DeviceRegistrationInput registrationInput)
		{
			if (this.ModelState.IsValid)
			{
				var result = new DeviceRegistrationResult();

				try
				{
					if (registrationInput == null)
						throw HttpStatusCode.BadRequest.AsException();

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

					//push = _pushUriService.SavePushUri(push);

					//if (registrationInput.Uid != 0)
					//	push.UnitId = registrationInput.Uid;

					//await _pushService.Register(push);

					CqrsEvent registerUnitPushEvent = new CqrsEvent();
					registerUnitPushEvent.Type = (int)CqrsEventTypes.PushRegistration;
					registerUnitPushEvent.Data = ObjectSerialization.Serialize(push);

					_cqrsProvider.EnqueueCqrsEvent(registerUnitPushEvent);

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

			throw HttpStatusCode.BadRequest.AsException();
		}

		/// <summary>
		/// Removed a Push Notification support by PushUriId.
		/// </summary>
		/// <param name="input">Input to deregister the device for</param>
		/// <returns></returns>
		[System.Web.Http.AcceptVerbs("DELETE")]
		public async Task<HttpResponseMessage> UnRegisterDevice([FromUri] DeviceUnRegistrationInput input)
		{
			if (input == null)
				throw HttpStatusCode.BadRequest.AsException();

			if (this.ModelState.IsValid)
			{
				try
				{
					if (!String.IsNullOrWhiteSpace(input.Did))
					{
						var deviceId = HttpUtility.UrlDecode(input.Did);
						var pushUri = _pushUriService.GetPushUriByDeviceId(deviceId);

						// Sometimes the phone sends back info, we don't want the catch block handing this
						if (pushUri == null)
							return Request.CreateResponse(HttpStatusCode.OK);

						if (pushUri.UserId != UserId)
							return Request.CreateResponse(HttpStatusCode.Unauthorized);

						await _pushService.UnRegister(pushUri);
					}

					if (input.Pid > 0)
					{
						var pushUri = _pushUriService.GetPushUriById(input.Pid);

						// Sometimes the phone sends back info, we don't want the catch block handing this
						if (pushUri == null)
							return Request.CreateResponse(HttpStatusCode.OK);

						if (pushUri.UserId != UserId)
							throw HttpStatusCode.Unauthorized.AsException();

						await _pushService.UnRegister(pushUri);
					}

					return Request.CreateResponse(HttpStatusCode.Created);
				}
				catch (Exception ex)
				{
					Framework.Logging.LogException(ex);
					throw HttpStatusCode.InternalServerError.AsException();
				}
			}

			throw HttpStatusCode.BadRequest.AsException();
		}


		/// <summary>
		/// Register a unit device to receive push notificaiton from the Resgrid system
		/// </summary>
		/// <param name="registrationInput">Input to create the registration for</param>
		/// <returns>Result for the registration</returns>
		[System.Web.Http.AcceptVerbs("POST")]
		public async Task<DeviceRegistrationResult> RegisterUnitDevice([FromBody] DeviceRegistrationInput registrationInput)
		{
			if (this.ModelState.IsValid)
			{
				var result = new DeviceRegistrationResult();

				try
				{
					if (registrationInput == null)
						throw HttpStatusCode.BadRequest.AsException();

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

					try
					{
						push = _pushUriService.SavePushUri(push);
					}
					catch { }

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

					return result;
				}
				catch (Exception ex)
				{
					result.Sfl = false;
					Framework.Logging.LogException(ex);

					return result;
				}
			}

			throw HttpStatusCode.BadRequest.AsException();
		}

		/// <summary>
		/// Removed a Unit Push Notification support by PushUriId.
		/// </summary>
		/// <param name="input">Input to deregister the device for</param>
		/// <returns></returns>
		[System.Web.Http.AcceptVerbs("DELETE")]
		public async Task<HttpResponseMessage> UnRegisterUnitDevice([FromUri] DeviceUnRegistrationInput input)
		{
			if (input == null)
				throw HttpStatusCode.BadRequest.AsException();

			if (this.ModelState.IsValid)
			{
				try
				{
					if (!String.IsNullOrWhiteSpace(input.Did))
					{
						var deviceId = HttpUtility.UrlDecode(input.Did);
						var pushUri = _pushUriService.GetPushUriByDeviceId(deviceId);

						// Sometimes the phone sends back info, we don't want the catch block handing this
						if (pushUri == null)
							return Request.CreateResponse(HttpStatusCode.OK);

						if (pushUri.UserId != UserId)
							return Request.CreateResponse(HttpStatusCode.Unauthorized);

						await _pushService.UnRegisterUnit(pushUri);
					}

					if (input.Pid > 0)
					{
						var pushUri = _pushUriService.GetPushUriById(input.Pid);

						// Sometimes the phone sends back info, we don't want the catch block handing this
						if (pushUri == null)
							return Request.CreateResponse(HttpStatusCode.OK);

						if (pushUri.UserId != UserId)
							throw HttpStatusCode.Unauthorized.AsException();

						await _pushService.UnRegisterUnit(pushUri);
					}

					return Request.CreateResponse(HttpStatusCode.Created);
				}
				catch (Exception ex)
				{
					Framework.Logging.LogException(ex);
					throw HttpStatusCode.InternalServerError.AsException();
				}
			}

			throw HttpStatusCode.BadRequest.AsException();
		}

		public HttpResponseMessage Options()
		{
			var response = new HttpResponseMessage();
			response.StatusCode = HttpStatusCode.OK;
			response.Headers.Add("Access-Control-Allow-Origin", "*");
			response.Headers.Add("Access-Control-Request-Headers", "*");
			response.Headers.Add("Access-Control-Allow-Methods", "GET,POST,PUT,DELETE,OPTIONS");

			return response;
		}
	}
}
