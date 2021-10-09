using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using System.Threading.Tasks;
using IAuthorizationService = Resgrid.Model.Services.IAuthorizationService;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.CallTypes;
using System.Linq;
using Resgrid.Model;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Call Priorities, for example Low, Medium, High. Call Priorities can be system provided ones or custom for a department
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class CallTypesController : V4AuthenticatedApiControllerbase
	{
		#region Members and Constructors
		private readonly ICallsService _callsService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IUserProfileService _userProfileService;
		private readonly IGeoLocationProvider _geoLocationProvider;
		private readonly IAuthorizationService _authorizationService;
		private readonly IQueueService _queueService;
		private readonly IUsersService _usersService;
		private readonly IUnitsService _unitsService;
		private readonly IActionLogsService _actionLogsService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IPersonnelRolesService _personnelRolesService;
		private readonly IProtocolsService _protocolsService;
		private readonly IEventAggregator _eventAggregator;

		public CallTypesController(
			ICallsService callsService,
			IDepartmentsService departmentsService,
			IUserProfileService userProfileService,
			IGeoLocationProvider geoLocationProvider,
			IAuthorizationService authorizationService,
			IQueueService queueService,
			IUsersService usersService,
			IUnitsService unitsService,
			IActionLogsService actionLogsService,
			IDepartmentGroupsService departmentGroupsService,
			IPersonnelRolesService personnelRolesService,
			IProtocolsService protocolsService,
			IEventAggregator eventAggregator
			)
		{
			_callsService = callsService;
			_departmentsService = departmentsService;
			_userProfileService = userProfileService;
			_geoLocationProvider = geoLocationProvider;
			_authorizationService = authorizationService;
			_queueService = queueService;
			_usersService = usersService;
			_unitsService = unitsService;
			_actionLogsService = actionLogsService;
			_departmentGroupsService = departmentGroupsService;
			_personnelRolesService = personnelRolesService;
			_protocolsService = protocolsService;
			_eventAggregator = eventAggregator;
		}
		#endregion Members and Constructors

		/// <summary>
		/// Gets all the call priorities in a department
		/// </summary>
		/// <returns></returns>
		[HttpGet("GetAllCallTypes")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<CallTypesResult>> GetAllCallTypes()
		{
			var result = new CallTypesResult();

			var callTypes = await _callsService.GetCallTypesForDepartmentAsync(DepartmentId);

			if (callTypes != null && callTypes.Any())
			{
				result.Data.Add(new CallTypeResultData() { Id = "0", Name = "No Type" });

				foreach (var callType in callTypes)
				{
					result.Data.Add(ConvertTypeData(callType));
				}

				result.PageSize = result.Data.Count;
				result.Status = ResponseHelper.Success;
			}
			else
			{
				result.Data.Add(new CallTypeResultData() { Id = "0", Name = "No Type" });
				result.PageSize = 1;
				result.Status = ResponseHelper.NotFound;
			}

			ResponseHelper.PopulateV4ResponseData(result);

			return result;
		}

		public static CallTypeResultData ConvertTypeData(CallType callType)
		{
			var type = new CallTypeResultData();
			type.Id = callType.CallTypeId.ToString();
			type.Name = callType.Type;

			return type;
		}
	}
}
