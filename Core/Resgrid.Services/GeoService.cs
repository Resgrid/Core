using System;
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

		public double GetPersonnelEtaInSeconds(ActionLog log)
		{
			if (log == null || String.IsNullOrWhiteSpace(log.GeoLocationData))
				return -1;

			if (log.DestinationId.HasValue)
			{
				RouteInformation route = null;
				if (log.DestinationType.GetValueOrDefault() == 1 || log.ActionTypeId == (int)ActionTypes.RespondingToStation) // Department Group
				{
					var group = _departmentGroupsService.GetGroupById(log.DestinationId.Value, false);

					if (group != null && group.AddressId.HasValue)
					{
						Address address = null;

						if (group.Address != null)
							address = group.Address;
						else
							address = _addressService.GetAddressById(group.AddressId.Value);

						route = _geoLocationProvider.GetRoute(log.GeoLocationData, address.FormatAddress());
					}
					else if (group != null && !String.IsNullOrWhiteSpace(group.Latitude) && !String.IsNullOrWhiteSpace(group.Longitude))
					{
						route = _geoLocationProvider.GetRoute(log.GeoLocationData, string.Format("{0},{1}", group.Latitude, group.Longitude));
					}
				}
				else if (log.DestinationType.GetValueOrDefault() == 2 || log.ActionTypeId == (int)ActionTypes.RespondingToScene) // Call
				{
					var call = _callsService.GetCallById(log.DestinationId.Value, false);

					if (!String.IsNullOrWhiteSpace(call.GeoLocationData))
						route = _geoLocationProvider.GetRoute(log.GeoLocationData, call.GeoLocationData);
					else
						route = _geoLocationProvider.GetRoute(log.GeoLocationData, call.GeoLocationData);
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

		public double GetEtaInSeconds(string start, string destination)
		{
			if (String.IsNullOrWhiteSpace(start) || String.IsNullOrWhiteSpace(destination))
				return -1;

			RouteInformation route = _geoLocationProvider.GetRoute(start, destination);

			if (route != null)
			{
				return route.Seconds;
			}
			
			return -1;
		}
	}
}