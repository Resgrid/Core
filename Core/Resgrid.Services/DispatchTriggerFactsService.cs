using System;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// <see cref="IDispatchTriggerFactsService"/>: the P4 facts the shared trigger engine reads. Every lookup is best effort: a fact that
	/// cannot be resolved stays null, and a trigger criterion that needs it then does not match (never a failed call save).
	/// </summary>
	public class DispatchTriggerFactsService : IDispatchTriggerFactsService
	{
		private readonly Lazy<IDepartmentsService> _departments;
		private readonly Lazy<IDepartmentGroupsService> _groups;
		private readonly Lazy<IMappingService> _mapping;

		public DispatchTriggerFactsService(Lazy<IDepartmentsService> departments, Lazy<IDepartmentGroupsService> groups, Lazy<IMappingService> mapping)
		{
			_departments = departments;
			_groups = groups;
			_mapping = mapping;
		}

		public async Task FillAsync(int departmentId, DispatchTriggerFacts facts, DispatchTriggerCallInput input)
		{
			if (facts == null || departmentId <= 0)
				return;

			input ??= new DispatchTriggerCallInput();
			facts.Nature = input.Nature;
			facts.Notes = input.Notes;
			facts.CallSource = input.CallSource;

			try
			{
				var department = await _departments.Value.GetDepartmentByIdAsync(departmentId);
				if (department != null)
					facts.LocalTime = DateTime.SpecifyKind(facts.AtUtc, DateTimeKind.Utc).TimeConverter(department);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Trigger facts: the local time of department {departmentId} could not be worked out.");
			}

			var point = input.Latitude.HasValue && input.Longitude.HasValue
				? new GeoMath.GeoPoint(input.Latitude.Value, input.Longitude.Value)
				: GeoMath.ParseLatLonString(input.GeoLocationData);

			if (point.HasValue)
			{
				try
				{
					var groups = await _groups.Value.GetAllGroupsForDepartmentUnlimitedAsync(departmentId);
					facts.GeofenceGroupIds = (groups ?? Enumerable.Empty<DepartmentGroup>())
						.Where(g => g != null && g.DepartmentId == departmentId && !string.IsNullOrWhiteSpace(g.Geofence)
									&& GeoMath.IsPointInPolygon(point.Value.Latitude, point.Value.Longitude, GeoMath.ParseGeofence(g.Geofence)))
						.Select(g => g.DepartmentGroupId)
						.ToHashSet();
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Trigger facts: the geofences of department {departmentId} could not be read.");
				}
			}

			if (input.LocationPoiId.HasValue && input.LocationPoiId.Value > 0)
			{
				try
				{
					var poi = await _mapping.Value.GetPOIByIdAsync(input.LocationPoiId.Value);
					var types = poi == null ? null : await _mapping.Value.GetPOITypesForDepartmentAsync(departmentId);
					// The POI must be one of the department's own (its type belongs to the department).
					if (poi != null && types != null && types.Any(t => t.PoiTypeId == poi.PoiTypeId && t.DepartmentId == departmentId))
						facts.LocationPoiTypeId = poi.PoiTypeId;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Trigger facts: POI {input.LocationPoiId} could not be read.");
				}
			}
		}
	}
}
