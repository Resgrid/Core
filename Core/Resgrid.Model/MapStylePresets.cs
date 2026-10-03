using System;
using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>
	/// The Mapbox styles behind <see cref="MapStyleTypes"/>, and how a department's day and night choices
	/// resolve. The server owns this table so every client renders the same style version; the apps only
	/// fall back to their own built-in styles until config has loaded.
	/// </summary>
	public static class MapStylePresets
	{
		public const string StreetsStyleUrl = "mapbox://styles/mapbox/streets-v12";
		public const string OutdoorsStyleUrl = "mapbox://styles/mapbox/outdoors-v12";
		public const string LightStyleUrl = "mapbox://styles/mapbox/light-v11";
		public const string DarkStyleUrl = "mapbox://styles/mapbox/dark-v11";
		public const string SatelliteStyleUrl = "mapbox://styles/mapbox/satellite-v9";
		public const string SatelliteStreetsStyleUrl = "mapbox://styles/mapbox/satellite-streets-v12";
		public const string NavigationDayStyleUrl = "mapbox://styles/mapbox/navigation-day-v1";
		public const string NavigationNightStyleUrl = "mapbox://styles/mapbox/navigation-night-v1";

		/// <summary>Every selectable style, in the order the settings screen lists them.</summary>
		public static readonly IReadOnlyList<MapStyleTypes> SelectableStyles = new[]
		{
			MapStyleTypes.Streets,
			MapStyleTypes.Outdoors,
			MapStyleTypes.Light,
			MapStyleTypes.Dark,
			MapStyleTypes.Satellite,
			MapStyleTypes.SatelliteStreets,
			MapStyleTypes.NavigationDay,
			MapStyleTypes.NavigationNight
		};

		/// <summary>
		/// Reads a stored setting value. Anything unparseable or unknown (a value written by a newer
		/// build, a hand-edited row) is Automatic, so a bad row degrades to the default map rather than
		/// to a blank one.
		/// </summary>
		public static MapStyleTypes Parse(string value)
		{
			if (int.TryParse(value, out var style) && Enum.IsDefined(typeof(MapStyleTypes), style))
				return (MapStyleTypes)style;

			return MapStyleTypes.Automatic;
		}

		public static MapStyleTypes Normalize(MapStyleTypes style)
		{
			return Enum.IsDefined(typeof(MapStyleTypes), style) ? style : MapStyleTypes.Automatic;
		}

		/// <summary>The style a day (light theme) choice renders. Automatic is Streets.</summary>
		public static MapStyleTypes ResolveDayStyle(MapStyleTypes dayStyle)
		{
			var day = Normalize(dayStyle);

			return day == MapStyleTypes.Automatic ? MapStyleTypes.Streets : day;
		}

		/// <summary>
		/// The style a night (dark theme) choice renders. An explicit night style wins. Automatic pairs
		/// with the day style: road maps go Dark, Navigation Day goes Navigation Night, and styles with no
		/// dark counterpart (Outdoors, Satellite, Satellite Streets) stay as they are, because swapping
		/// them for Dark at night would drop the terrain or imagery the department chose them for.
		/// </summary>
		public static MapStyleTypes ResolveNightStyle(MapStyleTypes dayStyle, MapStyleTypes nightStyle)
		{
			var night = Normalize(nightStyle);

			if (night != MapStyleTypes.Automatic)
				return night;

			switch (ResolveDayStyle(dayStyle))
			{
				case MapStyleTypes.Outdoors:
					return MapStyleTypes.Outdoors;
				case MapStyleTypes.Satellite:
					return MapStyleTypes.Satellite;
				case MapStyleTypes.SatelliteStreets:
					return MapStyleTypes.SatelliteStreets;
				case MapStyleTypes.NavigationDay:
				case MapStyleTypes.NavigationNight:
					return MapStyleTypes.NavigationNight;
				default:
					return MapStyleTypes.Dark;
			}
		}

		/// <summary>The mapbox:// style url for a concrete style. Automatic resolves as a day style.</summary>
		public static string GetStyleUrl(MapStyleTypes style)
		{
			switch (ResolveDayStyle(style))
			{
				case MapStyleTypes.Outdoors:
					return OutdoorsStyleUrl;
				case MapStyleTypes.Light:
					return LightStyleUrl;
				case MapStyleTypes.Dark:
					return DarkStyleUrl;
				case MapStyleTypes.Satellite:
					return SatelliteStyleUrl;
				case MapStyleTypes.SatelliteStreets:
					return SatelliteStreetsStyleUrl;
				case MapStyleTypes.NavigationDay:
					return NavigationDayStyleUrl;
				case MapStyleTypes.NavigationNight:
					return NavigationNightStyleUrl;
				default:
					return StreetsStyleUrl;
			}
		}
	}
}
