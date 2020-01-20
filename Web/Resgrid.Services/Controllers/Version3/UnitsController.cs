using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using System.Web.Http;
using System.Web.Http.Cors;
using Resgrid.Model.Helpers;
using Resgrid.Model.Services;
using Resgrid.Services.CoreWeb;
using Resgrid.Web.Services.Controllers.Version3.Models.Units;
using System.Net.Http;
using System.Net;
using Resgrid.Web.Services.Controllers.Version3.Models.Personnel;
using Resgrid.Web.Services.Helpers;

namespace Resgrid.Web.Services.Controllers.Version3
{
	/// <summary>
	/// Operations to perform against units in a department
	/// </summary>
	[EnableCors(origins: "*", headers: "*", methods: "GET,POST,PUT,DELETE,OPTIONS")]
	public class UnitsController : V3AuthenticatedApiControllerbase
	{
		private readonly IUsersService _usersService;
		private readonly IActionLogsService _actionLogsService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IUserProfileService _userProfileService;
		private readonly IUserStateService _userStateService;
		private readonly IUnitsService _unitsService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private IWebEventPublisher _webEventPublisher;

		public UnitsController(
			IUsersService usersService,
			IActionLogsService actionLogsService,
			IDepartmentsService departmentsService,
			IUserProfileService userProfileService,
			IWebEventPublisher webEventPublisher,
			IUserStateService userStateService,
			IUnitsService unitsService,
			IDepartmentGroupsService departmentGroupsService
			)
		{
			_usersService = usersService;
			_actionLogsService = actionLogsService;
			_departmentsService = departmentsService;
			_userProfileService = userProfileService;
			_webEventPublisher = webEventPublisher;
			_userStateService = userStateService;
			_unitsService = unitsService;
			_departmentGroupsService = departmentGroupsService;
		}

		/// <summary>
		/// Get's all the units in a department and their current status information
		/// </summary>
		/// <returns>List of UnitStatusResult objects, with status information for each unit.</returns>
		[AcceptVerbs("GET")]
		public List<UnitStatusResult> GetUnitStatuses()
		{
			var results = new List<UnitStatusResult>();

			var units = _unitsService.GetAllLatestStatusForUnitsByDepartmentId(DepartmentId);
			var department = _departmentsService.GetDepartmentById(DepartmentId, false);

			/*
			Parallel.ForEach(units, u =>
			{
				var unitStatus = new UnitStatusResult();
				unitStatus.Uid = u.UnitId;
				unitStatus.Typ = u.State;
				unitStatus.Tmp = u.Timestamp.TimeConverter(department);

				if (u.DestinationId.HasValue)
					unitStatus.Did = u.DestinationId.Value;

				results.Add(unitStatus);
			});
			*/
			
			foreach (var u in units)
			{
				var unitStatus = new UnitStatusResult();
				unitStatus.Uid = u.UnitId;
				unitStatus.Typ = u.State;
				unitStatus.Tmp = u.Timestamp.TimeConverter(department);

				if (u.DestinationId.HasValue)
					unitStatus.Did = u.DestinationId.Value;

				results.Add(unitStatus);
			}
			
			return results;
		}

		/// <summary>
		/// Get's all the units in a department and their current status information
		/// </summary>
		/// <returns>List of UnitStatusResult objects, with status information for each unit.</returns>
		[AcceptVerbs("GET")]
		public List<UnitStatusResult> GetUnitStatuses(string activeFilter)
		{
			var results = new List<UnitStatusResult>();
			var filter = HttpUtility.UrlDecode(activeFilter);
			var activeFilters = filter.Split(char.Parse("|"));
			var filters = GetFilterOptions();

			var units = _unitsService.GetAllLatestStatusForUnitsByDepartmentId(DepartmentId);
			var department = _departmentsService.GetDepartmentById(DepartmentId, false);

			Parallel.ForEach(units, u =>
			{
				var unitStatus = new UnitStatusResult();
				unitStatus.Uid = u.UnitId;
				unitStatus.Typ = u.State;
				unitStatus.Tmp = u.Timestamp.TimeConverter(department);

				if (u.DestinationId.HasValue)
					unitStatus.Did = u.DestinationId.Value;

				foreach (var afilter in activeFilters)
				{
					var text = GetTextValue(afilter, filters);

					if (afilter.Substring(0, 2) == "G:")
					{
						if (u.Unit != null && u.Unit.StationGroup != null && text == u.Unit.StationGroup.Name)
						{
							results.Add(unitStatus);
							break;
						}
					}
				}
			});

				/*
			foreach (var u in units)
			{
				var unitStatus = new UnitStatusResult();
				unitStatus.Uid = u.UnitId;
				unitStatus.Typ = u.State;
				unitStatus.Tmp = u.Timestamp.TimeConverter(Department);

				if (u.DestinationId.HasValue)
					unitStatus.Did = u.DestinationId.Value;

				foreach (var afilter in activeFilters)
				{
					var text = GetTextValue(afilter, filters);

					if (afilter.Substring(0, 2) == "G:")
					{
						if (u.Unit != null && u.Unit.StationGroup != null && text == u.Unit.StationGroup.Name)
						{
							results.Add(unitStatus);
							break;
						}
					}

				}
			}
				 * */

			return results;
		}

		/// <summary>
		/// Get's all the units in a department and their basic info
		/// </summary>
		/// <returns>List of UnitResult objects, with basic information for each unit.</returns>
		[AcceptVerbs("GET")]
		public List<UnitResult> GetUnitsForDepartment(int departmentId)
		{
			var results = new List<UnitResult>();

			if (departmentId != DepartmentId && !IsSystem)
				throw HttpStatusCode.Unauthorized.AsException();


			if (departmentId == 0 && IsSystem)
			{
				// Get All
				var departments = _departmentsService.GetAll();

				foreach (var department in departments)
				{
					var units = _unitsService.GetUnitsForDepartment(departmentId);

					foreach (var u in units)
					{
						var unitResult = new UnitResult();
						unitResult.Id = u.UnitId;
						unitResult.DepartmentId = u.DepartmentId;
						unitResult.Name = u.Name;
						unitResult.Type = u.Type;
						unitResult.StationId = u.StationGroupId;
						unitResult.VIN = u.VIN;
						unitResult.PlateNumber = u.PlateNumber;
						unitResult.FourWheel = u.FourWheel;
						unitResult.SpecialPermit = u.SpecialPermit;

						results.Add(unitResult);
					}
				}

				return results;
			}
			else
			{
				var units = _unitsService.GetUnitsForDepartment(departmentId);

				foreach (var u in units)
				{
					var unitResult = new UnitResult();
					unitResult.Id = u.UnitId;
					unitResult.DepartmentId = u.DepartmentId;
					unitResult.Name = u.Name;
					unitResult.Type = u.Type;
					unitResult.StationId = u.StationGroupId;
					unitResult.VIN = u.VIN;
					unitResult.PlateNumber = u.PlateNumber;
					unitResult.FourWheel = u.FourWheel;
					unitResult.SpecialPermit = u.SpecialPermit;

					results.Add(unitResult);
				}
			}

			return results;
		}

		[AcceptVerbs("GET")]
		public List<UnitDetailResult> GetUnitDetail()
		{
			List<UnitDetailResult> result = new List<UnitDetailResult>();
			var units = _unitsService.GetAllLatestStatusForUnitsByDepartmentId(DepartmentId);
			var names = _departmentsService.GetAllPersonnelNamesForDepartment(DepartmentId);

			foreach (var unit in units)
			{
				var unitResult = new UnitDetailResult();
				unitResult.Roles = new List<UnitDetailRoleResult>();
				unitResult.Id = unit.UnitId;
				unitResult.Name = unit.Unit.Name;
				unitResult.Type = unit.Unit.Type;
				unitResult.GroupId = unit.Unit.StationGroupId.GetValueOrDefault();
				unitResult.VIN = unit.Unit.VIN;
				unitResult.PlateNumber = unit.Unit.PlateNumber;
				unitResult.Offroad = unit.Unit.FourWheel.GetValueOrDefault();
				unitResult.SpecialPermit = unit.Unit.SpecialPermit.GetValueOrDefault();
				unitResult.StatusId = unit.UnitStateId;

				if (unit.Roles != null && unit.Roles.Count() > 0)
				{
					foreach (var role in unit.Roles)
					{
						var roleResult = new UnitDetailRoleResult();
						roleResult.RoleName = role.Role;
						roleResult.RoleId = role.UnitStateRoleId;
						roleResult.UserId = role.UserId;

						var name = names.FirstOrDefault(x => x.UserId == role.UserId);

						if (name != null)
							roleResult.Name = name.Name;
						else
							roleResult.Name = "Unknown";

						unitResult.Roles.Add(roleResult);
					}
				}

				result.Add(unitResult);
			}

			return result;
		}

		private string GetTextValue(string filter, List<FilterResult> filters)
		{
			return filters.Where(x => x.Id == filter).Select(y => y.Name).FirstOrDefault();
		}

		private List<FilterResult> GetFilterOptions()
		{
			var result = new List<FilterResult>();

			var stations = _departmentGroupsService.GetAllGroupsForDepartment(DepartmentId);

			Parallel.ForEach(stations, s =>
			{
				var respondingTo = new FilterResult();
				respondingTo.Id = string.Format("G:{0}", s.DepartmentGroupId);
				respondingTo.Type = "Group";
				respondingTo.Name = s.Name;

				result.Add(respondingTo);
			});

			/*
			foreach (var s in stations)
			{
				var respondingTo = new FilterResult();
				respondingTo.Id = string.Format("G:{0}", s.DepartmentGroupId);
				respondingTo.Type = "Group";
				respondingTo.Name = s.Name;

				result.Add(respondingTo);
			}
			*/
			return result;
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
