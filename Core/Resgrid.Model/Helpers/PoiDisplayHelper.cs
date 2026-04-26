using System;
using System.Collections.Generic;

namespace Resgrid.Model.Helpers
{
	public static class PoiDisplayHelper
	{
		public static string GetDisplayName(Poi poi, string fallbackTypeName = null)
		{
			if (poi == null)
				return GetTypeName(null, fallbackTypeName);

			if (!string.IsNullOrWhiteSpace(poi.Name))
				return poi.Name;

			if (!string.IsNullOrWhiteSpace(poi.Address))
				return poi.Address;

			if (!string.IsNullOrWhiteSpace(poi.Note))
				return poi.Note;

			if (!string.IsNullOrWhiteSpace(poi.Type?.Name))
				return poi.Type.Name;

			return GetTypeName(poi, fallbackTypeName);
		}

		public static string GetSelectionLabel(Poi poi, string fallbackTypeName = null)
		{
			if (poi == null)
				return GetTypeName(null, fallbackTypeName);

			if (!string.IsNullOrWhiteSpace(poi.Name) && !string.IsNullOrWhiteSpace(poi.Address) && !string.Equals(poi.Name.Trim(), poi.Address.Trim(), StringComparison.OrdinalIgnoreCase))
				return $"{poi.Name} - {poi.Address}";

			var displayName = GetDisplayName(poi, fallbackTypeName);
			if (!string.IsNullOrWhiteSpace(displayName) && string.IsNullOrWhiteSpace(poi.Name) && string.IsNullOrWhiteSpace(poi.Address) && string.IsNullOrWhiteSpace(poi.Note) && poi.PoiId > 0)
				return $"{displayName} #{poi.PoiId}";

			return displayName;
		}

		public static string GetTypeName(Poi poi, string fallbackTypeName = null)
		{
			if (!string.IsNullOrWhiteSpace(poi?.Type?.Name))
				return poi.Type.Name;

			if (!string.IsNullOrWhiteSpace(fallbackTypeName))
				return fallbackTypeName;

			return DestinationEntityTypes.Poi.GetDisplayName();
		}

		public static List<string> GetDisplayRows(Poi poi, string fallbackTypeName = null)
		{
			var rows = new List<string>();
			if (poi == null)
				return rows;

			var title = GetDisplayName(poi, fallbackTypeName);
			if (!string.IsNullOrWhiteSpace(title))
				rows.Add(title);

			var typeName = GetTypeName(poi, fallbackTypeName);
			if (!string.IsNullOrWhiteSpace(typeName) && !string.Equals(title, typeName, StringComparison.OrdinalIgnoreCase))
				rows.Add(typeName);

			if (!string.IsNullOrWhiteSpace(poi.Address) && !string.Equals(title, poi.Address, StringComparison.OrdinalIgnoreCase))
				rows.Add(poi.Address);

			if (!string.IsNullOrWhiteSpace(poi.Note) && !string.Equals(title, poi.Note, StringComparison.OrdinalIgnoreCase))
				rows.Add(poi.Note);

			return rows;
		}
	}
}
