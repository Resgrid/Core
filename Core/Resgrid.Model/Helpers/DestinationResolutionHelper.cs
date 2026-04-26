using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Localization;

namespace Resgrid.Model.Helpers
{
	public class ResolvedDestinationData
	{
		public int? DestinationId { get; set; }
		public int? DestinationType { get; set; }
		public string Name { get; set; }
		public string Address { get; set; }
		public string TypeName { get; set; }
	}

	public static class DestinationResolutionHelper
	{
		public static ResolvedDestinationData Resolve(int? destinationId, int? destinationType, int? detailType, IEnumerable<Call> activeCalls, IEnumerable<DepartmentGroup> groups, IEnumerable<Poi> pois, IStringLocalizer localizer, bool allowCrossTypeFallback = false)
		{
			var result = new ResolvedDestinationData
			{
				DestinationId = destinationId,
				DestinationType = destinationType
			};

			if (!destinationId.HasValue || destinationId.Value <= 0)
				return result;

			var effectiveDestinationType = destinationType.ToDestinationEntityType();

			if (effectiveDestinationType != DestinationEntityTypes.None)
				return ResolveByType(destinationId.Value, effectiveDestinationType, activeCalls, groups, pois, localizer);

			if (detailType.HasValue)
			{
				var stateDetailType = (CustomStateDetailTypes)detailType.Value;

				if (stateDetailType.SupportsStations())
				{
					var stationResult = ResolveByType(destinationId.Value, DestinationEntityTypes.Station, activeCalls, groups, pois, localizer);
					if (!string.IsNullOrWhiteSpace(stationResult.Name))
						return stationResult;
				}

				if (stateDetailType.SupportsCalls())
				{
					var callResult = ResolveByType(destinationId.Value, DestinationEntityTypes.Call, activeCalls, groups, pois, localizer);
					if (!string.IsNullOrWhiteSpace(callResult.Name))
						return callResult;
				}

				if (stateDetailType.SupportsPois())
				{
					var poiResult = ResolveByType(destinationId.Value, DestinationEntityTypes.Poi, activeCalls, groups, pois, localizer);
					if (!string.IsNullOrWhiteSpace(poiResult.Name))
						return poiResult;
				}
			}

			if (!allowCrossTypeFallback)
				return result;
				
			var fallbackStation = ResolveByType(destinationId.Value, DestinationEntityTypes.Station, activeCalls, groups, pois);
			if (!string.IsNullOrWhiteSpace(fallbackStation.Name))
				return fallbackStation;

			var fallbackCall = ResolveByType(destinationId.Value, DestinationEntityTypes.Call, activeCalls, groups, pois, localizer);
			if (!string.IsNullOrWhiteSpace(fallbackCall.Name))
				return fallbackCall;

			return ResolveByType(destinationId.Value, DestinationEntityTypes.Poi, activeCalls, groups, pois, localizer);
		}

		private static ResolvedDestinationData ResolveByType(int destinationId, DestinationEntityTypes destinationType, IEnumerable<Call> activeCalls, IEnumerable<DepartmentGroup> groups, IEnumerable<Poi> pois, IStringLocalizer localizer)
		{
			var result = new ResolvedDestinationData
			{
				DestinationId = destinationId,
				DestinationType = (int)destinationType
			};

			switch (destinationType)
			{
				case DestinationEntityTypes.Station:
					var station = groups?.FirstOrDefault(x => x.DepartmentGroupId == destinationId);
					if (station != null)
					{
						result.Name = station.Name;
						result.Address = station.Address?.FormatAddress();
						result.TypeName = destinationType.GetDisplayName(localizer);
					}
					break;
				case DestinationEntityTypes.Call:
					var call = activeCalls?.FirstOrDefault(x => x.CallId == destinationId);
					if (call != null)
					{
						var identifier = call.GetIdentifier();
						result.Name = string.IsNullOrWhiteSpace(call.Name) ? identifier : $"{identifier}: {call.Name}";
						result.Address = call.Address;
						result.TypeName = destinationType.GetDisplayName(localizer);
					}
					break;
				case DestinationEntityTypes.Poi:
					var poi = pois?.FirstOrDefault(x => x.PoiId == destinationId);
					if (poi != null)
					{
						result.Name = PoiDisplayHelper.GetDisplayName(poi);
						result.Address = poi.Address;
						result.TypeName = PoiDisplayHelper.GetTypeName(poi, destinationType.GetDisplayName(localizer), localizer);
					}
					break;
			}

			return result;
		}
	}
}
