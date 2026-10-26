using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Mapping
{
	/// <summary>
	/// Every icon the department's map can show, named with the department's own call types, priorities, unit types, status
	/// sets, statuses, roles and POI types, and drawn from the same icons as the map markers (GetMapDataAndMarkers): a
	/// department's uploads, the built-in pin a type picked and the status and priority colours all show as they do on the map.
	/// </summary>
	public class GetMapIconLegendResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public GetMapIconLegendResultData Data { get; set; }

		/// <summary>
		/// Default constructor
		/// </summary>
		public GetMapIconLegendResult()
		{
			Data = new GetMapIconLegendResultData();
		}
	}

	/// <summary>
	/// The legend. Each row's states use the marker icon fields with the marker precedence: IconUrl, else PinIconUrl, else the
	/// bundled ImagePath.
	/// </summary>
	public class GetMapIconLegendResultData
	{
		/// <summary>
		/// The unit and personnel status styles in legend order, then "unknown" (no meaning, or no status), with the
		/// department's statuses that show in each.
		/// </summary>
		public List<MapLegendStyleData> StatusStyles { get; set; } = new List<MapLegendStyleData>();

		/// <summary>The call priority styles, least to most urgent, then "unknown", with the department's priorities in each.</summary>
		public List<MapLegendStyleData> PriorityStyles { get; set; } = new List<MapLegendStyleData>();

		/// <summary>
		/// Calls with no type first (Id null), then each call type by name; one state per active priority of the department,
		/// in the department's priority order.
		/// </summary>
		public List<MapLegendRowData> Calls { get; set; } = new List<MapLegendRowData>();

		/// <summary>
		/// Units with no type first (Id null), then each unit type by name; one state per status of the type's status set (the
		/// built-in unit statuses when it has none).
		/// </summary>
		public List<MapLegendRowData> Units { get; set; } = new List<MapLegendRowData>();

		/// <summary>
		/// Members with no role icon first (Id null), then each role with an uploaded icon, in the department's role order (a
		/// member shows the icon of the first role they hold that has one); one state per personnel status of the department.
		/// </summary>
		public List<MapLegendRowData> Personnel { get; set; } = new List<MapLegendRowData>();

		/// <summary>The department's POI types by name, with the icon their markers show.</summary>
		public List<MapLegendPoiTypeData> PoiTypes { get; set; } = new List<MapLegendPoiTypeData>();
	}

	/// <summary>A status or priority style.</summary>
	public class MapLegendStyleData
	{
		/// <summary>The style key, as map markers carry it in IconStyle (available, responding, low, emergency, unknown ...).</summary>
		public string Key { get; set; }

		/// <summary>The style's colour, "#RRGGBB".</summary>
		public string Color { get; set; }

		/// <summary>The status meanings (ActionBaseTypes values) the style groups; status styles only, empty for unknown.</summary>
		public List<int> Meanings { get; set; } = new List<int>();

		/// <summary>Status styles: the department's unit statuses, from every status set, that show in this style.</summary>
		public List<string> UnitStatuses { get; set; } = new List<string>();

		/// <summary>Status styles: the department's personnel statuses that show in this style.</summary>
		public List<string> PersonnelStatuses { get; set; } = new List<string>();

		/// <summary>Priority styles: the department's priorities that show in this style.</summary>
		public List<string> Priorities { get; set; } = new List<string>();

		/// <summary>Status styles: the default unit pin in this style (32 x 37 PNG, tip at the bottom).</summary>
		public string UnitPinIconUrl { get; set; }

		/// <summary>Status styles: the default person pin in this style.</summary>
		public string PersonPinIconUrl { get; set; }

		/// <summary>Priority styles: the default call pin in this style.</summary>
		public string CallPinIconUrl { get; set; }
	}

	/// <summary>A call type, unit type or role (or the "none" row) with its icon in each priority or status.</summary>
	public class MapLegendRowData
	{
		/// <summary>The call type, unit type or role id; null for the "none" row.</summary>
		public int? Id { get; set; }

		/// <summary>The department's name for the type or role; for the "none" row a generic label in the server's language, which clients should replace with their own.</summary>
		public string Name { get; set; }

		/// <summary>Units: the name of the unit type's status set; null when it uses the built-in unit statuses.</summary>
		public string StatusSetName { get; set; }

		/// <summary>The type's or role's own uploaded icon (as it shows with no priority or status), or null.</summary>
		public string IconUrl { get; set; }

		/// <summary>Always null here (an own icon has no style); present so a row reads like a marker.</summary>
		public string PinIconUrl { get; set; }

		/// <summary>The bundled icon the type picked, or the default call, unit or person icon.</summary>
		public string ImagePath { get; set; }

		/// <summary>The icon in each of the department's priorities (calls) or statuses (units, personnel).</summary>
		public List<MapLegendStateData> States { get; set; } = new List<MapLegendStateData>();
	}

	/// <summary>One priority or status and the icon a marker of the row shows in it.</summary>
	public class MapLegendStateData
	{
		/// <summary>The stored priority id (calls) or status id (units, personnel): the value a call's Priority or a unit's or person's status holds.</summary>
		public int Id { get; set; }

		/// <summary>The department's name for the priority or status.</summary>
		public string Name { get; set; }

		/// <summary>The priority's or status's own colour when it is a hex colour, else null.</summary>
		public string Color { get; set; }

		/// <summary>The style of the priority or status (see MapLegendStyleData.Key); "unknown" when it has no meaning.</summary>
		public string IconStyle { get; set; }

		/// <summary>The department's uploaded icon for it (for this very priority or status, else the type's own with a badge), or null.</summary>
		public string IconUrl { get; set; }

		/// <summary>The built-in pin the row's type picked (or the default pin), recoloured to the style.</summary>
		public string PinIconUrl { get; set; }

		/// <summary>The bundled icon, for clients without styled pins.</summary>
		public string ImagePath { get; set; }

		/// <summary>True when IconUrl was uploaded for this very priority or status rather than the type's own icon.</summary>
		public bool IsVariant { get; set; }

		/// <summary>
		/// Personnel only: true for the built-in On Unit status. A member On Unit takes the style of their unit's latest status
		/// on the map; the icon given here (gray) shows only when there is no unit status to follow.
		/// </summary>
		public bool FollowsUnitStatus { get; set; }
	}

	/// <summary>A POI type and the icon its markers show.</summary>
	public class MapLegendPoiTypeData
	{
		public int PoiTypeId { get; set; }
		public string Name { get; set; }
		public string Color { get; set; }

		/// <summary>The POI type's icon name (as PoiLayerData.PoiImage).</summary>
		public string PoiImage { get; set; }

		/// <summary>The bundled icon name resolved for clients that ship PNGs (as PoiLayerData.ImagePath).</summary>
		public string ImagePath { get; set; }

		public string Marker { get; set; }

		/// <summary>The department's uploaded icon for the POI type, or null.</summary>
		public string IconUrl { get; set; }
	}
}
