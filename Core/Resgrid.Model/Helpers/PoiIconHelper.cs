using System;
using System.Collections.Generic;

namespace Resgrid.Model.Helpers
{
	/// <summary>
	/// POI types store their icon as a web-font class name ("map-icon-hospital") because that is what
	/// the web POI editor offers. The mobile and board apps ship PNG assets keyed by short names
	/// ("hospital"), and previously received no icon at all for POIs -- so every POI fell through to
	/// the client's default marker, which is the call icon (a flame). Resolving here means one
	/// implementation for every client instead of four, and shipped clients pick up the fix without a
	/// release because they already render whatever <c>ImagePath</c> they are handed.
	/// </summary>
	public static class PoiIconHelper
	{
		/// <summary>
		/// Neutral marker used whenever a POI class has no dedicated asset. Deliberately not "call" --
		/// showing a hospital as a structure fire is worse than showing it as a generic pin.
		/// </summary>
		public const string DefaultIconName = "flag";

		private static readonly Dictionary<string, string> IconsByPoiClass = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			// Medical
			{ "hospital", "hospital" },
			{ "health", "firstaid" },
			{ "doctor", "firstaid" },
			{ "dentist", "firstaid" },
			{ "pharmacy", "firstaid" },
			{ "physiotherapist", "firstaid" },
			{ "veterinary-care", "firstaid" },

			// Emergency services
			{ "fire-station", "station" },
			{ "police", "flag" },

			// Transport
			{ "airport", "aircraft" },
			{ "bus-station", "bus" },
			{ "car-dealer", "car" },
			{ "car-rental", "car" },
			{ "car-repair", "car" },
			{ "car-wash", "car" },
			{ "gas-station", "car" },
			{ "taxi-stand", "car" },

			// Temporary accommodation / staging
			{ "campground", "camper" },
			{ "rv-park", "camper" },

			// Trades and works
			{ "electrician", "tools" },
			{ "general-contractor", "tools" },
			{ "locksmith", "tools" },
			{ "moving-company", "tools" },
			{ "painter", "tools" },
			{ "plumber", "tools" },
			{ "roofing-contractor", "tools" },
			{ "storage", "tools" }
		};

		/// <summary>
		/// Maps a POI type's stored icon value onto an icon name the apps can resolve.
		/// </summary>
		/// <param name="poiTypeImage">
		/// The stored value, normally a "map-icon-*" class name. Bare names ("hospital") and empty
		/// values are both accepted.
		/// </param>
		public static string ResolveIconName(string poiTypeImage)
		{
			if (string.IsNullOrWhiteSpace(poiTypeImage))
				return DefaultIconName;

			var poiClass = poiTypeImage.Trim();

			if (poiClass.StartsWith("map-icon-", StringComparison.OrdinalIgnoreCase))
				poiClass = poiClass.Substring("map-icon-".Length);

			if (string.IsNullOrWhiteSpace(poiClass))
				return DefaultIconName;

			if (IconsByPoiClass.TryGetValue(poiClass, out var iconName))
				return iconName;

			return DefaultIconName;
		}
	}
}
