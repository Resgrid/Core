using System;

namespace Resgrid.Model
{
	/// <summary>Where an event layer's event is relative to now.</summary>
	public enum MapLayerEventPhases
	{
		/// <summary>The layer has no event dates.</summary>
		None = 0,
		Upcoming = 1,
		Active = 2,
		Ended = 3
	}

	/// <summary>
	/// Event layers: map layers (usually imported from KML, KMZ or GeoJSON) for a marathon route, a festival site, a parade
	/// and the like, with the event's dates and contacts. While the event runs the layer is shown on every map by default,
	/// whatever its "on by default" setting, so crews see it without anyone remembering to switch it on and off.
	/// </summary>
	public static class MapLayerEvents
	{
		public static bool HasEventDates(MapLayer layer) =>
			layer != null && (layer.EventStartsOn.HasValue || layer.EventEndsOn.HasValue);

		/// <summary>
		/// The event's phase at <paramref name="utcNow"/>. A missing start means "already started", a missing end "still on".
		/// </summary>
		public static MapLayerEventPhases GetPhase(MapLayer layer, DateTime utcNow)
		{
			if (!HasEventDates(layer))
				return MapLayerEventPhases.None;

			if (layer.EventStartsOn.HasValue && utcNow < AsUtc(layer.EventStartsOn.Value))
				return MapLayerEventPhases.Upcoming;

			if (layer.EventEndsOn.HasValue && utcNow > AsUtc(layer.EventEndsOn.Value))
				return MapLayerEventPhases.Ended;

			return MapLayerEventPhases.Active;
		}

		/// <summary>
		/// Whether maps start with the layer switched on: the department's "on by default", or the layer's event running now.
		/// Before and after the event the layer follows "on by default", so it can be reviewed beforehand and switched off
		/// for good afterwards.
		/// </summary>
		public static bool IsShownByDefault(MapLayer layer, DateTime utcNow) =>
			layer != null && (layer.IsOnByDefault || GetPhase(layer, utcNow) == MapLayerEventPhases.Active);

		private static DateTime AsUtc(DateTime value) =>
			value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc);
	}
}
