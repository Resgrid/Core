using System;
using System.Collections.Generic;
using System.Globalization;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Calls
{
	/// <summary>
	/// A department POI (any type) a call's location can be picked from (M0322), with the point and address the form fills in.
	/// </summary>
	public class CallLocationPoiOption
	{
		public int PoiId { get; set; }

		public string Text { get; set; }

		/// <summary>The POI type, the option group.</summary>
		public string Group { get; set; }

		public double Latitude { get; set; }

		public double Longitude { get; set; }

		public string Address { get; set; }

		public bool Selected { get; set; }

		public bool HasPoint => CallLocations.IsValidPoint(Latitude, Longitude);
	}

	/// <summary>A route plan a call can follow (M0322), with where it starts and ends so the form can fill a blank location and destination.</summary>
	public class CallRoutePlanOption
	{
		public string RoutePlanId { get; set; }

		public string Name { get; set; }

		public int StopsCount { get; set; }

		public string StartName { get; set; }

		public double? StartLatitude { get; set; }

		public double? StartLongitude { get; set; }

		public string EndName { get; set; }

		public double? EndLatitude { get; set; }

		public double? EndLongitude { get; set; }

		public bool Selected { get; set; }

		/// <summary>
		/// False for a call's stored route that can no longer be followed (archived, paused, deleted): offered so an edit keeps it
		/// rather than silently dropping it, never offered to a new call.
		/// </summary>
		public bool IsAvailable { get; set; } = true;
	}

	/// <summary>
	/// The location POI, free destination and route inputs the call forms share (New Call, Update Call, Add Archived Call; M0322).
	/// </summary>
	public interface ICallLocationsForm
	{
		Call Call { get; }

		/// <summary>The department's POIs (any type) the location can be picked from; empty hides the picker.</summary>
		List<CallLocationPoiOption> LocationPois { get; }

		/// <summary>The route plans the call can follow; empty hides the route picker.</summary>
		List<CallRoutePlanOption> RoutePlans { get; }

		/// <summary>The destination POIs' points by id, for the destination marker on the call map.</summary>
		Dictionary<string, double[]> DestinationPoiPoints { get; }

		/// <summary>The free destination point as typed, invariant ("50.87"), never model-bound into the call.</summary>
		string DestinationLatitude { get; }

		string DestinationLongitude { get; }

		/// <summary>The member may open Routes (the route preview link).</summary>
		bool CanViewRoutes { get; }

		bool IsFieldVisible(string key);

		bool IsFieldRequired(string key);
	}

	public static class CallLocationsForm
	{
		/// <summary>The location POI picker sits with the address or the pin, and only when the department has POIs.</summary>
		public static bool ShowsLocationPoi(this ICallLocationsForm form) =>
			form?.LocationPois != null && form.LocationPois.Count > 0 &&
			(form.IsFieldVisible(NewCallFieldKeys.Address) || form.IsFieldVisible(NewCallFieldKeys.Geolocation));

		/// <summary>The destination field of the New Call Form Fields policy covers the POI and the free destination.</summary>
		public static bool ShowsDestination(this ICallLocationsForm form) => form != null && form.IsFieldVisible(NewCallFieldKeys.DestinationPoi);

		/// <summary>The route picker has no policy field: it shows whenever there is a route to pick (or the call's own).</summary>
		public static bool ShowsRoute(this ICallLocationsForm form) => form?.RoutePlans != null && form.RoutePlans.Count > 0;

		/// <summary>The free destination point the form posted as "lat,lon" (invariant), or null when none or not a usable point.</summary>
		public static string PostedDestinationPoint(this ICallLocationsForm form) => form == null ? null : PostedPoint(form.DestinationLatitude, form.DestinationLongitude);

		/// <summary>A destination point was typed but is not a real coordinate (half missing, not an invariant number, out of range).</summary>
		public static bool HasInvalidDestinationPoint(this ICallLocationsForm form) =>
			form != null && (!String.IsNullOrWhiteSpace(form.DestinationLatitude) || !String.IsNullOrWhiteSpace(form.DestinationLongitude)) &&
			form.PostedDestinationPoint() == null;

		/// <summary>
		/// A latitude/longitude pair as the "lat,lon" calls store. Parsed with the invariant culture, never the UI culture (a decimal
		/// comma would bind 50,87 as 5087), so a value the form wrote with a dot means the same in every language.
		/// </summary>
		public static string PostedPoint(string latitude, string longitude)
		{
			if (String.IsNullOrWhiteSpace(latitude) || String.IsNullOrWhiteSpace(longitude))
				return null;

			return CallLocations.NormalizePoint(latitude.Trim() + "," + longitude.Trim());
		}

		/// <summary>A stored "lat,lon" as the two invariant strings the form's inputs carry; nulls when it is not a usable point.</summary>
		public static (string Latitude, string Longitude) SplitPoint(string point)
		{
			return CallLocations.TryParsePoint(point, out var latitude, out var longitude)
				? (Invariant(latitude), Invariant(longitude))
				: (null, null);
		}

		public static string Invariant(double value) => value.ToString("0.#######", CultureInfo.InvariantCulture);
	}

	/// <summary>One run of a call's route (M0322): which unit, how far it got, when it ran.</summary>
	public class CallRouteRunRow
	{
		public string RouteInstanceId { get; set; }

		public string RoutePlanName { get; set; }

		public string UnitName { get; set; }

		public int Status { get; set; }

		public int StopsCompleted { get; set; }

		public int StopsTotal { get; set; }

		public string StartedOn { get; set; }

		public string EndedOn { get; set; }
	}

	/// <summary>
	/// What the call page and the call exports show of a call's start location, destination and route (M0322) beyond the
	/// destination POI they already show.
	/// </summary>
	public class CallLocationDetails
	{
		/// <summary>The POI the call location was picked from.</summary>
		public string LocationPoiName { get; set; }

		/// <summary>The free destination (when the destination is not a POI).</summary>
		public string DestinationAddress { get; set; }

		public string DestinationCoordinates { get; set; }

		public string DestinationW3W { get; set; }

		public string RoutePlanId { get; set; }

		/// <summary>The route's name; null when the call names a route that no longer exists.</summary>
		public string RoutePlanName { get; set; }

		/// <summary>The member may open Routes: the route name links to it.</summary>
		public bool CanViewRoutes { get; set; }

		public List<CallRouteRunRow> RouteRuns { get; set; } = new List<CallRouteRunRow>();

		public bool HasFreeDestination =>
			!String.IsNullOrWhiteSpace(DestinationAddress) || !String.IsNullOrWhiteSpace(DestinationCoordinates) || !String.IsNullOrWhiteSpace(DestinationW3W);

		public bool HasRoute => !String.IsNullOrWhiteSpace(RoutePlanId);
	}
}
