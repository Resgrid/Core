using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.Units;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Web.Services.Models.v4.Personnel;
using System.Collections.Generic;
using System;
using System.Web;
using SharpKml.Dom;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Information regarding Units
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class UnitsController : V4AuthenticatedApiControllerbase
	{
		#region Members and Constructors
		private readonly IUnitsService _unitsService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly Model.Services.IAuthorizationService _authorizationService;
		private readonly ICustomStateService _customStateService;
		private readonly IDepartmentsService _departmentsService;

		public UnitsController(IUnitsService unitsService, IDepartmentGroupsService departmentGroupsService,
			ICustomStateService customStateService, Model.Services.IAuthorizationService authorizationService,
			IDepartmentsService departmentsService)
		{
			_unitsService = unitsService;
			_departmentGroupsService = departmentGroupsService;
			_customStateService = customStateService;
			_authorizationService = authorizationService;
			_departmentsService = departmentsService;
		}
		#endregion Members and Constructors

		/// <summary>
		/// Gets all the Units for a Department
		/// </summary>
		/// <returns></returns>
		[HttpGet("GetAllUnits")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Unit_View)]
		public async Task<ActionResult<UnitsResult>> GetAllUnits()
		{
			var result = new UnitsResult();
			var units = await _unitsService.GetUnitsForDepartmentAsync(DepartmentId);
			var types = await _unitsService.GetUnitTypesForDepartmentAsync(DepartmentId);

			if (units != null && units.Any())
			{
				var unitStatuses = await _unitsService.GetAllLatestStatusForUnitsByDepartmentIdAsync(DepartmentId);

				foreach (var unit in units)
				{
					if (!await _authorizationService.CanUserViewUnitViaMatrixAsync(unit.UnitId, UserId, DepartmentId))
						continue;

					UnitType type = null;

					if (types != null && types.Any())
						type = types.FirstOrDefault(x => x.Type == unit.Type);

					result.Data.Add(ConvertUnitsData(unit, unitStatuses.FirstOrDefault(x => x.UnitId == unit.UnitId), type, TimeZone));
				}

				result.PageSize = result.Data.Count;
				result.Status = ResponseHelper.Success;
			}
			else
			{
				result.PageSize = 0;
				result.Status = ResponseHelper.NotFound;
			}

			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		/// <summary>
		/// Gets all the Units for a Department
		/// </summary>
		/// <returns></returns>
		[HttpGet("GetAllUnitsInfos")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Unit_View)]
		public async Task<ActionResult<UnitsInfoResult>> GetAllUnitsInfos(string activeFilter)
		{
			var result = new UnitsInfoResult();
			var units = await _unitsService.GetUnitsForDepartmentAsync(DepartmentId);
			var types = await _unitsService.GetUnitTypesForDepartmentAsync(DepartmentId);
			var personnelNames = await _departmentsService.GetAllPersonnelNamesForDepartmentAsync(DepartmentId);

			string filter = null;
			string[] activeFilters = null;
			if (!String.IsNullOrWhiteSpace(activeFilter))
			{
				filter = HttpUtility.UrlDecode(activeFilter);
				activeFilters = filter.Split(char.Parse("|"));
			}
			var filters = await GetFilterOptions();

			if (units != null && units.Any())
			{
				var unitStatuses = await _unitsService.GetAllLatestStatusForUnitsByDepartmentIdAsync(DepartmentId);

				foreach (var unit in units)
				{
					if (!await _authorizationService.CanUserViewUnitViaMatrixAsync(unit.UnitId, UserId, DepartmentId))
						continue;

					UnitType type = null;

					if (types != null && types.Any())
						type = types.FirstOrDefault(x => x.Type == unit.Type);

					var canViewLocation = await _authorizationService.CanUserViewUnitLocationAsync(UserId, unit.UnitId, DepartmentId);

					var unitState = unitStatuses.FirstOrDefault(x => x.UnitId == unit.UnitId);
					var customState = await _customStateService.GetCustomUnitStateAsync(unitState);

					if (activeFilters != null)
					{
						foreach (var afilter in activeFilters)
						{
							//var text = GetTextValue(afilter, filters);

							if (afilter.Substring(0, 2) == "G:")
							{
								if (unit != null && unit.StationGroupId != null && afilter.Replace("G:", "").Trim() == unit.StationGroupId.Value.ToString())
								{
									var roles = await _unitsService.GetActiveRolesForUnitAsync(unit.UnitId);
									result.Data.Add(ConvertUnitsInfoResultData(unit, unitState, customState, type, TimeZone, canViewLocation, roles, personnelNames));
									break;
								}
							}
						}
					}
					else
					{
						var roles = await _unitsService.GetActiveRolesForUnitAsync(unit.UnitId);
						result.Data.Add(ConvertUnitsInfoResultData(unit, unitState, customState, type, TimeZone, canViewLocation, roles, personnelNames));
					}
				}

				result.PageSize = result.Data.Count;
				result.Status = ResponseHelper.Success;
			}
			else
			{
				result.PageSize = 0;
				result.Status = ResponseHelper.NotFound;
			}

			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		/// <summary>
		/// Gets all the options available to filter units against compatible Resgrid APIs
		/// </summary>
		/// <returns>GetUnitsFilterOptionsResult with information pertaining to each filter option</returns>
		[HttpGet("GetUnitsFilterOptions")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Personnel_View)]
		public async Task<ActionResult<GetUnitsFilterOptionsResult>> GetUnitsFilterOptions()
		{
			var result = new GetUnitsFilterOptionsResult();
			result.Data = new List<FilterResult>();

			var groups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId);

			var everyone = new FilterResult()
			{
				Id = "0",
				Type = "",
				Name = "All Units"
			};
			result.Data.Add(everyone);

			if (groups != null && groups.Any())
			{
				foreach (var group in groups)
				{
					result.Data.Add(new FilterResult()
					{
						Id = $"G:{group.DepartmentGroupId}",
						Type = "Groups",
						Name = group.Name
					});
				}
			}

			result.PageSize = result.Data.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		public static UnitResultData ConvertUnitsData(Model.Unit unit, UnitState state, UnitType type, string timeZone)
		{
			var data = new UnitResultData();
			data.UnitId = unit.UnitId.ToString();
			data.DepartmentId = unit.DepartmentId.ToString();
			data.Name = unit.Name;
			data.Type = unit.Type;
			data.Vin = unit.VIN;

			if (unit.FourWheel.HasValue)
				data.FourWheelDrive = unit.FourWheel.Value;

			if (unit.SpecialPermit.HasValue)
				data.SpecialPermit = unit.SpecialPermit.Value;

			if (state != null)
			{
				data.CurrentStatusId = state.State.ToString();

				data.CurrentStatusTimestamp = state.Timestamp.TimeConverter(new Department() { TimeZone = timeZone });
				data.Note = state.Note;

				if (state.DestinationId.HasValue)
					data.CurrentDestinationId = state.DestinationId.Value.ToString();

				if (state.Latitude.HasValue)
					data.Latitude = state.Latitude.Value.ToString();

				if (state.Longitude.HasValue)
					data.Longitude = state.Longitude.Value.ToString();
			}

			if (type != null)
			{
				data.CustomStatusSetId = type.CustomStatesId.GetValueOrDefault().ToString();
				data.TypeId = type.UnitTypeId;
			}

			if (unit.StationGroup != null)
			{
				data.GroupId = unit.StationGroup.DepartmentGroupId.ToString();
				data.GroupName = unit.StationGroup.Name;
			}

			return data;
		}

		public static UnitsInfoResultData ConvertUnitsInfoResultData(Model.Unit unit, UnitState state, CustomStateDetail customState, UnitType type, string timeZone,
			bool canViewLocation, List<UnitActiveRole> activeRoles, List<PersonName> names)
		{
			var data = new UnitsInfoResultData();
			data.UnitId = unit.UnitId.ToString();
			data.DepartmentId = unit.DepartmentId.ToString();
			data.Name = unit.Name;
			data.Type = unit.Type;
			data.Vin = unit.VIN;

			if (unit.FourWheel.HasValue)
				data.FourWheelDrive = unit.FourWheel.Value;

			if (unit.SpecialPermit.HasValue)
				data.SpecialPermit = unit.SpecialPermit.Value;

			if (state != null)
			{
				data.CurrentStatusId = state.State.ToString();

				data.CurrentStatusTimestamp = state.Timestamp.TimeConverter(new Department() { TimeZone = timeZone });
				data.CurrentStatusTimestampUtc = state.Timestamp;
				data.Note = state.Note;

				if (state.DestinationId.HasValue)
					data.CurrentDestinationId = state.DestinationId.Value.ToString();

				if (canViewLocation)
				{
					if (state.Latitude.HasValue)
						data.Latitude = state.Latitude.Value.ToString();

					if (state.Longitude.HasValue)
						data.Longitude = state.Longitude.Value.ToString();
				}
			}

			if (type != null)
			{
				data.CustomStatusSetId = type.CustomStatesId.GetValueOrDefault().ToString();
				data.TypeId = type.UnitTypeId;
			}

			if (unit.StationGroup != null)
			{
				data.GroupId = unit.StationGroup.DepartmentGroupId.ToString();
				data.GroupName = unit.StationGroup.Name;
			}

			data.Roles = new List<UnitRoleData>();

			if (unit.Roles != null && unit.Roles.Any())
			{
				foreach (var role in unit.Roles)
				{
					var unitRoleData = new UnitRoleData();
					unitRoleData.RoleId = role.UnitRoleId.ToString();
					unitRoleData.RoleName = role.Name;

					if (activeRoles != null && activeRoles.Any())
					{
						var activeRole = activeRoles.FirstOrDefault(x => x.Role == role.Name);

						if (activeRole != null)
						{
							unitRoleData.UserId = activeRole.UserId;

							if (names != null && names.Any())
							{
								var personName = names.FirstOrDefault(x => x.UserId == activeRole.UserId);

								if (personName != null)
									unitRoleData.Name = $"{personName.FirstName} {personName.LastName}";
							}
						}
					}

					data.Roles.Add(unitRoleData);
				}
			}

			if (customState != null)
			{
				data.CurrentStatus = customState.ButtonText;
				data.CurrentStatusColor = customState.ButtonColor;
			}

			return data;
		}

		private string GetTextValue(string filter, List<FilterResult> filters)
		{
			return filters.Where(x => x.Id == filter).Select(y => y.Name).FirstOrDefault();
		}

		private string GetIdValue(string filter, List<FilterResult> filters)
		{
			return filters.Where(x => x.Id == filter).Select(y => y.Name).FirstOrDefault();
		}

		private async Task<List<FilterResult>> GetFilterOptions()
		{
			var result = new List<FilterResult>();

			var stations = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId);

			Parallel.ForEach(stations, s =>
			{
				var respondingTo = new FilterResult();
				respondingTo.Id = $"G:{s.DepartmentGroupId}";
				respondingTo.Type = "Group";
				respondingTo.Name = s.Name;

				result.Add(respondingTo);
			});

			return result;
		}
	}
}
