using System;
using System.Collections.Generic;
using System.Linq;

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

		/// <summary>Mapbox's own maintained base maps, in the order the settings screen lists them.</summary>
		public static readonly IReadOnlyList<MapStyleTypes> MapboxStyles = new[]
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
		/// The Mapbox gallery's "Community templates" (https://www.mapbox.com/gallery), owner/id as the gallery
		/// publishes them. Verified 2026-10-03 to load (style, raster tiles and static images) on a non-Mapbox
		/// account's public token. Left out: the community entries that duplicate a Mapbox style above, the
		/// deprecated Navigation Guidance pair, and NASA's Black Marble and Water World, which are not public
		/// (404 on another account's token). All are classic styles (no Standard imports), so they render on
		/// mapbox-gl v2, the native SDKs and the website's raster tiles alike. Listed alphabetically.
		/// </summary>
		private static readonly IReadOnlyDictionary<MapStyleTypes, string> CommunityStyleUrls = new Dictionary<MapStyleTypes, string>
		{
			[MapStyleTypes.AmericanMemory] = "mapbox://styles/mapbox-map-design/cl4orrp5e000p14ldwenm7xsf", // Mel Imfeld
			[MapStyleTypes.Basic] = "mapbox://styles/mapbox-map-design/cl4whef7m000714pc44f3qaxs", // Mapbox
			[MapStyleTypes.BasicOvercast] = "mapbox://styles/mapbox-map-design/cl4whev1w002w16s9mgoliotw", // Mapbox
			[MapStyleTypes.Blueprint] = "mapbox://styles/mapbox-map-design/cks97e1e37nsd17nzg7p0308g", // Amy Lee Walton
			[MapStyleTypes.Bubble] = "mapbox://styles/mapbox-map-design/cl4wxue5j000c14r17uqrjpqb", // Mapbox
			[MapStyleTypes.CaliTerrain] = "mapbox://styles/mapbox/cjerxnqt3cgvp2rmyuxbeqme7", // Amy Lee Walton
			[MapStyleTypes.Decimal] = "mapbox://styles/mapbox-map-design/ck4014y110wt61ctt07egsel6", // Tristen Brown
			[MapStyleTypes.FinlandTopo] = "mapbox://styles/mapbox-map-design/cmd3ga8yb065i01sh1oho4h1r", // Nikita Slavin
			[MapStyleTypes.Frank] = "mapbox://styles/mapbox-map-design/ckshxkppe0gge18nz20i0nrwq", // Clare Trainor
			[MapStyleTypes.IceCream] = "mapbox://styles/mapbox/cj7t3i5yj0unt2rmt3y4b5e32", // Maya Gao
			[MapStyleTypes.LeShine] = "mapbox://styles/mapbox/cjcunv5ae262f2sm9tfwg8i0w", // Nat Slaughter
			[MapStyleTypes.StreetsJapan] = "mapbox://styles/mapbox-map-design/ckt20wgoy1awp17ms7pyygigf", // Mapbox
			[MapStyleTypes.Mineral] = "mapbox://styles/mapbox/cjtep62gq54l21frr1whf27ak", // Madison Draper
			[MapStyleTypes.Minimo] = "mapbox://styles/mapbox-map-design/cksjc2nsq1bg117pnekb655h1", // Nat Slaughter
			[MapStyleTypes.Moonlight] = "mapbox://styles/mapbox/cj3kbeqzo00022smj7akz3o1e", // Rasagy Sharma
			[MapStyleTypes.NeonGlow] = "mapbox://styles/mapbox-map-design/cl4gxqwi5001415l381n7qwak", // Taya Lavrinenko
			[MapStyleTypes.NorthStar] = "mapbox://styles/mapbox/cj44mfrt20f082snokim4ungi", // Nat Slaughter
			[MapStyleTypes.Pencil] = "mapbox://styles/mapbox-map-design/cks9iema71es417mlrft4go2k", // Madison Draper
			[MapStyleTypes.StandardOil] = "mapbox://styles/mapbox-map-design/ckr0svm3922ki18qntevm857n", // Mapbox
			[MapStyleTypes.Unicorn] = "mapbox://styles/mapbox-map-design/cl4fotjdi000l15p8cqc6nuts" // Taya Lavrinenko
		};

		/// <summary>The community styles, in the order the settings screen lists them.</summary>
		public static readonly IReadOnlyList<MapStyleTypes> CommunityStyles = new[]
		{
			MapStyleTypes.AmericanMemory,
			MapStyleTypes.Basic,
			MapStyleTypes.BasicOvercast,
			MapStyleTypes.Blueprint,
			MapStyleTypes.Bubble,
			MapStyleTypes.CaliTerrain,
			MapStyleTypes.Decimal,
			MapStyleTypes.FinlandTopo,
			MapStyleTypes.Frank,
			MapStyleTypes.IceCream,
			MapStyleTypes.LeShine,
			MapStyleTypes.Mineral,
			MapStyleTypes.Minimo,
			MapStyleTypes.Moonlight,
			MapStyleTypes.NeonGlow,
			MapStyleTypes.NorthStar,
			MapStyleTypes.Pencil,
			MapStyleTypes.StandardOil,
			MapStyleTypes.StreetsJapan,
			MapStyleTypes.Unicorn
		};

		/// <summary>Every selectable style: the Mapbox styles, then the community styles.</summary>
		public static readonly IReadOnlyList<MapStyleTypes> SelectableStyles = MapboxStyles.Concat(CommunityStyles).ToArray();

		public static bool IsCommunityStyle(MapStyleTypes style)
		{
			return CommunityStyleUrls.ContainsKey(style);
		}

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
		/// dark counterpart (Outdoors, Satellite, Satellite Streets, every community style) stay as they
		/// are, because swapping them for Dark at night would drop the terrain, imagery or look the
		/// department chose them for.
		/// </summary>
		public static MapStyleTypes ResolveNightStyle(MapStyleTypes dayStyle, MapStyleTypes nightStyle)
		{
			var night = Normalize(nightStyle);

			if (night != MapStyleTypes.Automatic)
				return night;

			var day = ResolveDayStyle(dayStyle);

			if (IsCommunityStyle(day))
				return day;

			switch (day)
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
			var resolved = ResolveDayStyle(style);

			if (CommunityStyleUrls.TryGetValue(resolved, out var communityStyleUrl))
				return communityStyleUrl;

			switch (resolved)
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
