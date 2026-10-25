using System.Collections.Generic;
using System.Linq;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Mapping
{
	/// <summary>One icon as the web map draws it, from the site's own origin.</summary>
	public class MapLegendIcon
	{
		public string Url { get; set; }

		/// <summary>A department upload (a square drawn at 32 x 32); otherwise a built-in 32 x 37 pin.</summary>
		public bool IsUploaded { get; set; }

		/// <summary>An upload made for this very status or priority.</summary>
		public bool IsVariant { get; set; }

		/// <summary>The status or priority style, or null when it has no meaning.</summary>
		public string Style { get; set; }

		public static MapLegendIcon From(MapMarkerIcon icon)
		{
			if (icon == null)
				return null;

			if (icon.IconKey != null)
				return new MapLegendIcon { Url = TypeIcons.WebIconUrl(icon.IconKey, icon.BadgeStyle), IsUploaded = true, IsVariant = icon.IconIsVariant, Style = icon.Style };

			return new MapLegendIcon
			{
				Url = icon.PinName != null ? TypeIcons.WebPinUrl(icon.PinName) : $"/images/Mapping/{icon.ImagePath}.png",
				Style = icon.Style
			};
		}
	}

	/// <summary>A status or priority style with the meanings it groups.</summary>
	public class MapLegendStyle
	{
		public string Key { get; set; }
		public string Color { get; set; }
		public List<ActionBaseTypes> Meanings { get; set; } = new List<ActionBaseTypes>();
	}

	/// <summary>One status or priority of the department, with the map icon a type shows in it.</summary>
	public class MapLegendState
	{
		/// <summary>The stored status or priority value.</summary>
		public int Value { get; set; }

		/// <summary>The icon slot an upload for it goes in.</summary>
		public string Variant { get; set; }

		public string Name { get; set; }

		/// <summary>The status's or priority's own colour when it is a hex colour, else null.</summary>
		public string Color { get; set; }

		public string Style { get; set; }

		public MapLegendIcon Icon { get; set; }
	}

	/// <summary>A call type, unit type or role (or the "none" row) with its icon in each status or priority.</summary>
	public class MapLegendRow
	{
		/// <summary>The type's or role's id; null for calls with no type, units with no type or members with no role icon.</summary>
		public int? Id { get; set; }

		public string Name { get; set; }

		/// <summary>The unit type's status set name (units only); null for the built-in unit statuses.</summary>
		public string StatusSetName { get; set; }

		public List<MapLegendState> States { get; set; } = new List<MapLegendState>();
	}

	public class MapLegendPoiType
	{
		public string Name { get; set; }
		public string Color { get; set; }
		public string IconClass { get; set; }
		public string IconUrl { get; set; }
	}

	public class MapIconLegendView
	{
		public List<MapLegendStyle> StatusStyles { get; set; } = new List<MapLegendStyle>();
		public List<MapLegendStyle> PriorityStyles { get; set; } = new List<MapLegendStyle>();
		public List<MapLegendRow> CallRows { get; set; } = new List<MapLegendRow>();
		public List<MapLegendRow> UnitRows { get; set; } = new List<MapLegendRow>();
		public List<MapLegendRow> PersonnelRows { get; set; } = new List<MapLegendRow>();
		public List<MapLegendPoiType> PoiTypes { get; set; } = new List<MapLegendPoiType>();
		public bool CanEditTypes { get; set; }
		public bool CanEditRoles { get; set; }
	}

	/// <summary>The status or priority icons page of one call type, unit type or role.</summary>
	public class StatusIconsView
	{
		public int Kind { get; set; }
		public int TypeId { get; set; }
		public string TypeName { get; set; }

		/// <summary>The type's own icon (uploaded or built-in), as it shows with no status or priority.</summary>
		public MapLegendIcon OwnIcon { get; set; }

		public List<StatusIconRow> Rows { get; set; } = new List<StatusIconRow>();
		public string Message { get; set; }
		public string ErrorMessage { get; set; }
	}

	public class StatusIconRow
	{
		public MapLegendState State { get; set; }

		/// <summary>What shows when no icon is uploaded for this status or priority.</summary>
		public MapLegendIcon DefaultIcon { get; set; }

		/// <summary>The icon uploaded for it, or null.</summary>
		public string UploadedUrl { get; set; }
	}

	/// <summary>Builds the legend and icon page rows from a department's <see cref="MapIconResolver"/>.</summary>
	public static class MapIconLegendBuilder
	{
		public static List<MapLegendStyle> StatusStyles() =>
			MapIconStyles.StatusStyles.Select(x => new MapLegendStyle { Key = x, Color = MapIconStyles.ColorOf(x), Meanings = MapIconStyles.BaseTypesOf(x).ToList() }).ToList();

		public static List<MapLegendStyle> PriorityStyles() =>
			MapIconStyles.PriorityStyles.Select(x => new MapLegendStyle { Key = x, Color = MapIconStyles.ColorOf(x) }).ToList();

		/// <summary>The statuses of a unit type: its own status set's active options, else the built-in unit statuses.</summary>
		public static (string SetName, List<CustomStateDetail> Statuses) UnitTypeStatuses(UnitType type, IEnumerable<CustomState> customStates, List<CustomStateDetail> builtInStatuses)
		{
			if (type?.CustomStatesId != null)
			{
				var set = (customStates ?? Enumerable.Empty<CustomState>())
					.FirstOrDefault(x => x != null && x.CustomStateId == type.CustomStatesId.Value && !x.IsDeleted && x.Type == (int)CustomStateTypes.Unit);

				if (set != null)
					return (set.Name, set.GetActiveDetails().ToList());
			}

			return (null, builtInStatuses ?? new List<CustomStateDetail>());
		}

		public static MapLegendRow CallRow(MapIconResolver resolver, CallType type, string noTypeName, IEnumerable<DepartmentCallPriority> priorities) =>
			new MapLegendRow
			{
				Id = type?.CallTypeId,
				Name = type?.Type ?? noTypeName,
				States = (priorities ?? Enumerable.Empty<DepartmentCallPriority>()).Select(p => PriorityState(resolver, p, resolver.ForCallType(type, p.DepartmentCallPriorityId))).ToList()
			};

		public static MapLegendRow UnitRow(MapIconResolver resolver, UnitType type, string noTypeName, IEnumerable<CustomState> customStates, List<CustomStateDetail> builtInStatuses)
		{
			var statuses = UnitTypeStatuses(type, customStates, builtInStatuses);

			return new MapLegendRow
			{
				Id = type?.UnitTypeId,
				Name = type?.Type ?? noTypeName,
				StatusSetName = statuses.SetName,
				States = statuses.Statuses.Select(s => StatusState(s, resolver.ForUnitType(type, s.CustomStateDetailId))).ToList()
			};
		}

		public static MapLegendRow RoleRow(MapIconResolver resolver, PersonnelRole role, string noRoleName, IEnumerable<CustomStateDetail> personnelStatuses) =>
			new MapLegendRow
			{
				Id = role?.PersonnelRoleId,
				Name = role?.Name ?? noRoleName,
				States = (personnelStatuses ?? Enumerable.Empty<CustomStateDetail>()).Select(s => StatusState(s, resolver.ForRole(role, s.CustomStateDetailId))).ToList()
			};

		private static MapLegendState PriorityState(MapIconResolver resolver, DepartmentCallPriority priority, MapMarkerIcon icon) =>
			new MapLegendState
			{
				Value = priority.DepartmentCallPriorityId,
				Variant = icon.Variant,
				Name = priority.Name,
				Color = HexOrNull(priority.Color),
				Style = icon.Style,
				Icon = MapLegendIcon.From(icon)
			};

		private static MapLegendState StatusState(CustomStateDetail status, MapMarkerIcon icon) =>
			new MapLegendState
			{
				Value = status.CustomStateDetailId,
				Variant = icon.Variant,
				Name = status.ButtonText,
				Color = HexOrNull(status.ButtonColor),
				Style = icon.Style,
				Icon = MapLegendIcon.From(icon)
			};

		/// <summary>
		/// The icon page rows: each state's current icon, what shows without an upload for it (the type's own icon with the
		/// state's badge, else the built-in pin in the state's style) and the upload if any.
		/// </summary>
		public static List<StatusIconRow> IconRows(MapIconResolver resolver, TypeIconKinds kind, int typeId, MapLegendRow row, MapMarkerIcon ownIcon)
		{
			return row.States.Select(state =>
			{
				var uploadedKey = resolver.VariantIconKey(kind, typeId, state.Variant);
				var withoutUpload = new MapMarkerIcon
				{
					ImagePath = ownIcon.ImagePath,
					PinBase = ownIcon.PinBase,
					Style = state.Style,
					Variant = state.Variant,
					IconKey = ownIcon.IconIsVariant ? null : ownIcon.IconKey
				};

				return new StatusIconRow
				{
					State = state,
					DefaultIcon = MapLegendIcon.From(withoutUpload),
					UploadedUrl = uploadedKey != null ? TypeIcons.WebIconUrl(uploadedKey) : null
				};
			}).ToList();
		}

		/// <summary>The colour when it is a plain hex colour (safe in a style attribute), else null.</summary>
		public static string HexOrNull(string color) =>
			!string.IsNullOrWhiteSpace(color) && HexColor.IsMatch(color.Trim()) ? color.Trim() : null;

		private static readonly System.Text.RegularExpressions.Regex HexColor =
			new System.Text.RegularExpressions.Regex("^#([0-9a-fA-F]{3}|[0-9a-fA-F]{4}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
	}
}
