using System.Collections.Generic;
using System.IO;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Framework;
using Resgrid.Web.Services.Controllers.Version3.Models.CallPriorities;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Resgrid.Web.Services.Controllers.Version3
{
	/// <summary>
	/// Operations to be performed against calls
	/// </summary>
	[Produces("application/json")]
	[Route("api/v{version:ApiVersion}/[controller]")]
	[ApiVersion("3.0")]
	[ApiExplorerSettings(GroupName = "v3")]
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
		[HttpGet("GetAllCallPriorites")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<List<CallPriorityResult>>> GetAllCallPriorites()
		{
			var result = new List<CallPriorityResult>();
			var priorities = await _callsService.GetCallPrioritiesForDepartmentAsync(DepartmentId);

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
		[HttpGet("GetAllCallPrioritesForDepartment")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		public async Task<ActionResult<List<CallPriorityResult>>> GetAllCallPrioritesForDepartment(int departmentId)
		{
			var result = new List<CallPriorityResult>();

			if (departmentId != DepartmentId && !IsSystem)
				Unauthorized();

			var priorities = await _callsService.GetCallPrioritiesForDepartmentAsync(departmentId);

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

			return Ok(result);
		}

		/// <summary>
		/// Return the audio file for push notifications for a specific call priority
		/// </summary>
		/// <returns>File download result for push dispatch audio for a call priority</returns>
		[HttpGet("GetPushAudioForPriority")]
		public async Task<ActionResult> GetPushAudioForPriority(int priorityId)
		{
			var priority = await _callsService.GetCallPrioritiesByIdAsync(DepartmentId, priorityId, true);

			if (priority == null || priority.PushNotificationSound == null)
				return BadRequest();

			return File(new MemoryStream(priority.PushNotificationSound), "audio/wav", $"CallPAudio_{priorityId}.wav");
		}

		/// <summary>
		/// Return the ios audio file for push notifications for a specific call priority
		/// </summary>
		/// <returns>File download result for push dispatch audio for a call priority</returns>
		[HttpGet("GetIOSPushAudioForPriority")]
		public async Task<ActionResult> GetIOSPushAudioForPriority(int priorityId)
		{
			var priority = await _callsService.GetCallPrioritiesByIdAsync(DepartmentId, priorityId, true);

			if (priority == null || priority.IOSPushNotificationSound == null)
				return BadRequest();

			return File(new MemoryStream(priority.PushNotificationSound), "audio/x-caf", $"CallPAudio_{priorityId}.caf");
		}
	}
}
