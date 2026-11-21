using System;
using System.Globalization;
using Resgrid.Model.Helpers;

namespace Resgrid.Model
{
	/// <summary>
	/// The start and end of a route plan as a call sees them (M0322): the plan's station or explicit point, else its first or last stop.
	/// </summary>
	public class RoutePlanEndpoints
	{
		public RoutePlanEndpoint Start { get; set; }

		public RoutePlanEndpoint End { get; set; }

		/// <summary>True when the start is the plan's first stop rather than a station or explicit point (the run checks in there).</summary>
		public bool StartIsStop { get; set; }

		/// <summary>True when the end is the plan's last stop rather than a station or explicit point.</summary>
		public bool EndIsStop { get; set; }

		public int StopsCount { get; set; }

		/// <summary>
		/// A plan a call can follow has somewhere to start and somewhere to end that are not the same single stop (one stop and nothing
		/// else gives the run nowhere to go).
		/// </summary>
		public bool IsComplete => Start != null && End != null && !(StartIsStop && EndIsStop && StopsCount < 2);
	}

	/// <summary>
	/// Rules for a call's start location and its destination (M0322). The call location (<see cref="Call.Address"/>,
	/// <see cref="Call.GeoLocationData"/>, <see cref="Call.W3W"/>) is the start point, optionally picked from a POI
	/// (<see cref="Call.LocationPoiId"/>). The destination is either a POI (<see cref="Call.DestinationPoiId"/>) or a free location
	/// (<see cref="Call.DestinationAddress"/>, <see cref="Call.DestinationGeoLocationData"/>, <see cref="Call.DestinationW3W"/>), never
	/// both. A plus code is only a way to find a point and is never stored, as for the call location.
	/// </summary>
	public static class CallLocations
	{
		/// <summary>Formats a point as the "lat,lon" string calls store (invariant culture, at most 7 decimals).</summary>
		public static string FormatPoint(double latitude, double longitude)
		{
			return latitude.ToString("0.#######", CultureInfo.InvariantCulture) + "," + longitude.ToString("0.#######", CultureInfo.InvariantCulture);
		}

		/// <summary>Reads a "lat,lon" string; false for blank, malformed, out-of-range or 0,0 values.</summary>
		public static bool TryParsePoint(string value, out double latitude, out double longitude)
		{
			latitude = 0;
			longitude = 0;

			if (String.IsNullOrWhiteSpace(value))
				return false;

			var parts = value.Split(',');
			if (parts.Length != 2)
				return false;

			if (!double.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out latitude) ||
				!double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out longitude))
				return false;

			return IsValidPoint(latitude, longitude);
		}

		public static bool IsValidPoint(double latitude, double longitude)
		{
			return !double.IsNaN(latitude) && !double.IsNaN(longitude) && latitude >= -90 && latitude <= 90 && longitude >= -180 && longitude <= 180 &&
				   !(latitude == 0 && longitude == 0);
		}

		/// <summary>Normalizes a posted "lat,lon" (trimmed, invariant); null when blank or not a usable point.</summary>
		public static string NormalizePoint(string value)
		{
			return TryParsePoint(value, out var latitude, out var longitude) ? FormatPoint(latitude, longitude) : null;
		}

		/// <summary>True when the call has a free destination (a point, an address or what3words), whatever its POI.</summary>
		public static bool HasFreeDestination(Call call)
		{
			return call != null && (!String.IsNullOrWhiteSpace(call.DestinationGeoLocationData) || !String.IsNullOrWhiteSpace(call.DestinationAddress) ||
									!String.IsNullOrWhiteSpace(call.DestinationW3W));
		}

		/// <summary>True when the call has a destination: a POI or a free location.</summary>
		public static bool HasDestination(Call call)
		{
			return call != null && ((call.DestinationPoiId.HasValue && call.DestinationPoiId.Value > 0) || HasFreeDestination(call));
		}

		/// <summary>True when the call has a location at all (point or address).</summary>
		public static bool HasLocation(Call call)
		{
			return call != null && (!String.IsNullOrWhiteSpace(call.GeoLocationData) || !String.IsNullOrWhiteSpace(call.Address));
		}

		/// <summary>
		/// Keeps one destination: a destination POI clears the free destination; blank free fields become null and the point is
		/// normalized (an unusable point is dropped).
		/// </summary>
		public static void NormalizeDestination(Call call)
		{
			if (call == null)
				return;

			if (call.LocationPoiId.HasValue && call.LocationPoiId.Value <= 0)
				call.LocationPoiId = null;

			if (String.IsNullOrWhiteSpace(call.RoutePlanId))
				call.RoutePlanId = null;
			else
				call.RoutePlanId = call.RoutePlanId.Trim();

			if (call.DestinationPoiId.HasValue && call.DestinationPoiId.Value <= 0)
				call.DestinationPoiId = null;

			if (call.DestinationPoiId.HasValue)
			{
				call.DestinationAddress = null;
				call.DestinationGeoLocationData = null;
				call.DestinationW3W = null;
				return;
			}

			call.DestinationAddress = Blank(call.DestinationAddress);
			call.DestinationGeoLocationData = NormalizePoint(call.DestinationGeoLocationData);
			call.DestinationW3W = Blank(call.DestinationW3W);
		}

		/// <summary>As <see cref="NormalizeDestination(Call)"/> for a recurring schedule.</summary>
		public static void NormalizeDestination(RecurringCall series)
		{
			if (series == null)
				return;

			if (series.DestinationPoiId.HasValue && series.DestinationPoiId.Value <= 0)
				series.DestinationPoiId = null;

			if (series.DestinationPoiId.HasValue)
			{
				series.DestinationAddress = null;
				series.DestinationGeoLocationData = null;
			}
			else
			{
				series.DestinationAddress = Blank(series.DestinationAddress);
				series.DestinationGeoLocationData = NormalizePoint(series.DestinationGeoLocationData);
			}

			if (String.IsNullOrWhiteSpace(series.RoutePlanId))
				series.RoutePlanId = null;
		}

		/// <summary>Fills a blank call address and point from the POI the location was picked from.</summary>
		public static void ApplyLocationPoi(Call call, Poi poi)
		{
			if (call == null || poi == null)
				return;

			if (String.IsNullOrWhiteSpace(call.Address))
				call.Address = !String.IsNullOrWhiteSpace(poi.Address) ? poi.Address : PoiDisplayHelper.GetDisplayName(poi);

			if (!TryParsePoint(call.GeoLocationData, out _, out _) && IsValidPoint(poi.Latitude, poi.Longitude))
				call.GeoLocationData = FormatPoint(poi.Latitude, poi.Longitude);
		}

		/// <summary>
		/// Route defaults: a call with no location starts at the route start; a call with no destination ends at the route end. Never
		/// overwrites what the call already has.
		/// </summary>
		public static void ApplyRouteEndpoints(Call call, RoutePlanEndpoints endpoints)
		{
			if (call == null || endpoints == null)
				return;

			if (endpoints.Start != null && !HasLocation(call) && !call.LocationPoiId.HasValue)
			{
				call.GeoLocationData = FormatPoint(endpoints.Start.Latitude, endpoints.Start.Longitude);
				if (!String.IsNullOrWhiteSpace(endpoints.Start.Name))
					call.Address = endpoints.Start.Name;
			}

			if (endpoints.End != null && !HasDestination(call))
			{
				call.DestinationGeoLocationData = FormatPoint(endpoints.End.Latitude, endpoints.End.Longitude);
				if (!String.IsNullOrWhiteSpace(endpoints.End.Name))
					call.DestinationAddress = endpoints.End.Name;
			}
		}

		/// <summary>The call's free destination point, or null.</summary>
		public static (double Latitude, double Longitude)? GetFreeDestinationPoint(Call call)
		{
			if (call != null && TryParsePoint(call.DestinationGeoLocationData, out var latitude, out var longitude))
				return (latitude, longitude);

			return null;
		}

		private static string Blank(string value)
		{
			return String.IsNullOrWhiteSpace(value) ? null : value.Trim();
		}
	}
}
