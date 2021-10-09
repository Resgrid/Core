using System;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class GeoService : IGeoService
	{
		private readonly IGeoLocationProvider _geoLocationProvider;
		private readonly ICallsService _callsService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IAddressService _addressService;

		public GeoService(IGeoLocationProvider geoLocationProvider, ICallsService callsService, IDepartmentGroupsService departmentGroupsService, IAddressService addressService)
		{
			_geoLocationProvider = geoLocationProvider;
			_callsService = callsService;
			_departmentGroupsService = departmentGroupsService;
			_addressService = addressService;
		}

		public async Task<double> GetPersonnelEtaInSecondsAsync(ActionLog log)
		{
			if (log == null || String.IsNullOrWhiteSpace(log.GeoLocationData))
				return -1;

			if (log.DestinationId.HasValue)
			{
				RouteInformation route = null;
				if (log.DestinationType.GetValueOrDefault() == 1 || log.ActionTypeId == (int)ActionTypes.RespondingToStation) // Department Group
				{
					var group = await _departmentGroupsService.GetGroupByIdAsync(log.DestinationId.Value, false);

					if (group != null && group.AddressId.HasValue)
					{
						Address address = null;

						if (group.Address != null)
							address = group.Address;
						else
							address = await _addressService.GetAddressByIdAsync(group.AddressId.Value);

						route = await _geoLocationProvider.GetRoute(log.GeoLocationData, address.FormatAddress());
					}
					else if (group != null && !String.IsNullOrWhiteSpace(group.Latitude) && !String.IsNullOrWhiteSpace(group.Longitude))
					{
						route = await _geoLocationProvider.GetRoute(log.GeoLocationData, string.Format("{0},{1}", group.Latitude, group.Longitude));
					}
				}
				else if (log.DestinationType.GetValueOrDefault() == 2 || log.ActionTypeId == (int)ActionTypes.RespondingToScene) // Call
				{
					var call = await _callsService.GetCallByIdAsync(log.DestinationId.Value, false);

					if (!String.IsNullOrWhiteSpace(call.GeoLocationData) && call.GeoLocationData.Length > 1)
						route = await _geoLocationProvider.GetRoute(log.GeoLocationData, call.GeoLocationData);
					else
						route = await _geoLocationProvider.GetRoute(log.GeoLocationData, call.GeoLocationData);
				}

				if (route != null)
				{
					var timeDiff = route.ProcessedOn - log.Timestamp;
					var time = route.Seconds - timeDiff.Seconds;

					if (time < 0)
						return 0;

					return time;
				}
			}

			return -1;
		}

		public async Task<double> GetEtaInSecondsAsync(string start, string destination)
		{
			if (String.IsNullOrWhiteSpace(start) || String.IsNullOrWhiteSpace(destination))
				return -1;

			RouteInformation route = await _geoLocationProvider.GetRoute(start, destination);

			if (route != null)
			{
				return route.Seconds;
			}
			
			return -1;
		}
	}
}
