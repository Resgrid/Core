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

		public UnitsController(IUnitsService unitsService)
		{
			_unitsService = unitsService;
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

		public static UnitResultData ConvertUnitsData(Unit unit, UnitState state, UnitType type, string timeZone)
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
	}
}
