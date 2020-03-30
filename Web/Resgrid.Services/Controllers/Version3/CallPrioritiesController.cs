using System.Collections.Generic;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using System.Web.Http.Cors;
using Resgrid.Framework;
using Resgrid.Web.Services.Controllers.Version3.Models.CallPriorities;
using System.Net.Http;
using System.Net;
using System.Net.Http.Headers;

namespace Resgrid.Web.Services.Controllers.Version3
{
	/// <summary>
	/// Operations to be performed against calls
	/// </summary>
	public class CallPrioritiesController : V3AuthenticatedApiControllerbase
	{
		#region Members and Constructors
		private ICallsService _callsService;
		private IDepartmentsService _departmentsService;
		private IUserProfileService _userProfileService;
		private IGeoLocationProvider _geoLocationProvider;
		private readonly IAuthorizationService _authorizationService;
		private readonly IQueueService _queueService;
		private readonly IUsersService _usersService;

		public CallPrioritiesController(
			ICallsService callsService,
			IDepartmentsService departmentsService,
			IUserProfileService userProfileService,
			IGeoLocationProvider geoLocationProvider,
			IAuthorizationService authorizationService,
			IQueueService queueService,
			IUsersService usersService
			)
		{
			_callsService = callsService;
			_departmentsService = departmentsService;
			_userProfileService = userProfileService;
			_geoLocationProvider = geoLocationProvider;
			_authorizationService = authorizationService;
			_queueService = queueService;
			_usersService = usersService;
		}
		#endregion Members and Constructors

		/// <summary>
		/// Returns all the call priorities (including deleted ones) for a department
		/// </summary>
		/// <returns>Array of CallPriorityResult objects for each call priority in the department</returns>
		[System.Web.Http.AcceptVerbs("GET")]
		public List<CallPriorityResult> GetAllCallPriorites()
		{
			var result = new List<CallPriorityResult>();
			var priorities = _callsService.GetCallPrioritesForDepartment(DepartmentId);

			foreach (var p in priorities)
			{
				var priority = new CallPriorityResult();

				priority.Id = p.DepartmentCallPriorityId;
				priority.DepartmentId = p.DepartmentId;
				priority.Name = StringHelpers.SanitizeHtmlInString(p.Name);
				priority.Color = p.Color;
				priority.Sort = p.Sort;
				priority.IsDeleted = p.IsDeleted;
				priority.IsDefault = p.IsDefault;
				priority.Tone = p.Tone;

				result.Add(priority);
			}

			return result;
		}

		/// <summary>
		/// Returns all the call priorities (including deleted ones) for a selected department
		/// </summary>
		/// <returns>Array of CallPriorityResult objects for each call priority in the department</returns>
		[System.Web.Http.AcceptVerbs("GET")]
		public List<CallPriorityResult> GetAllCallPrioritesForDepartment(int departmentId)
		{
			var result = new List<CallPriorityResult>();

			if (departmentId != DepartmentId && !IsSystem)
				Unauthorized();

			var priorities = _callsService.GetCallPrioritesForDepartment(departmentId);

			foreach (var p in priorities)
			{
				var priority = new CallPriorityResult();

				priority.Id = p.DepartmentCallPriorityId;
				priority.DepartmentId = p.DepartmentId;
				priority.Name = StringHelpers.SanitizeHtmlInString(p.Name);
				priority.Color = p.Color;
				priority.Sort = p.Sort;
				priority.IsDeleted = p.IsDeleted;
				priority.IsDefault = p.IsDefault;
				priority.Tone = p.Tone;

				result.Add(priority);
			}

			return result;
		}

		/// <summary>
		/// Return the audio file for push notifications for a specific call priority
		/// </summary>
		/// <returns>File download result for push dispatch audio for a call priority</returns>
		[System.Web.Http.AcceptVerbs("GET")]
		public HttpResponseMessage GetPushAudioForPriority(int priorityId)
		{
			var priority = _callsService.GetCallPrioritesById(DepartmentId, priorityId, true);

			if (priority == null || priority.PushNotificationSound == null)
				return new HttpResponseMessage(HttpStatusCode.BadRequest);

			HttpResponseMessage response = new HttpResponseMessage(HttpStatusCode.OK);
			response.Content = new ByteArrayContent(priority.PushNotificationSound);
			response.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment");
			response.Content.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
			response.Content.Headers.ContentDisposition.FileName = $"CallPAudio_{priorityId}.wav";

			return response;
		}
		/// <summary>
		/// Return the ios audio file for push notifications for a specific call priority
		/// </summary>
		/// <returns>File download result for push dispatch audio for a call priority</returns>
		[System.Web.Http.AcceptVerbs("GET")]
		public HttpResponseMessage GetIOSPushAudioForPriority(int priorityId)
		{
			var priority = _callsService.GetCallPrioritesById(DepartmentId, priorityId, true);

			if (priority == null || priority.IOSPushNotificationSound == null)
				return new HttpResponseMessage(HttpStatusCode.BadRequest);

			HttpResponseMessage response = new HttpResponseMessage(HttpStatusCode.OK);
			response.Content = new ByteArrayContent(priority.IOSPushNotificationSound);
			response.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment");
			response.Content.Headers.ContentType = new MediaTypeHeaderValue("audio/x-caf");
			response.Content.Headers.ContentDisposition.FileName = $"CallPAudio_{priorityId}.caf";

			return response;
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
