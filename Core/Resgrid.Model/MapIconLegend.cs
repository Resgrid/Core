using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Resgrid.Model
{
	/// <summary>
	/// Every icon a department's map can show, from its own call types, priorities, unit types, status sets, roles and
	/// uploads (<see cref="MapIconResolver"/>), before it is turned into URLs for a host: the web legend page, the icon
	/// settings pages and the apps' legend (v4 Mapping/GetMapIconLegend) are all built from it, so they all show what the
	/// map shows.
	/// </summary>
	public sealed class MapIconLegend
	{
		/// <summary>The status styles, in legend order, then <see cref="MapIconStyles.Unknown"/>.</summary>
		public List<MapIconLegendStyle> StatusStyles { get; set; } = new List<MapIconLegendStyle>();

		/// <summary>The priority styles, least to most urgent, then <see cref="MapIconStyles.Unknown"/>.</summary>
		public List<MapIconLegendStyle> PriorityStyles { get; set; } = new List<MapIconLegendStyle>();

		/// <summary>Calls with no type first, then each call type by name; a state per active priority.</summary>
		public List<MapIconLegendRow> CallRows { get; set; } = new List<MapIconLegendRow>();

		/// <summary>Units with no type first, then each unit type by name; a state per status of the type's status set.</summary>
		public List<MapIconLegendRow> UnitRows { get; set; } = new List<MapIconLegendRow>();

		/// <summary>Members with no role icon first, then each role with an icon, in role order; a state per personnel status.</summary>
		public List<MapIconLegendRow> PersonnelRows { get; set; } = new List<MapIconLegendRow>();
	}

	/// <summary>A status or priority style, with the meanings it groups and the department's own statuses or priorities in it.</summary>
	public sealed class MapIconLegendStyle
	{
		public string Key { get; set; }

		public string Color { get; set; }

		/// <summary>The status meanings the style groups (status styles only; none for unknown).</summary>
		public List<ActionBaseTypes> Meanings { get; set; } = new List<ActionBaseTypes>();

		/// <summary>The department's unit statuses, from every status set, that show in this style.</summary>
		public List<string> UnitStatusNames { get; set; } = new List<string>();

		/// <summary>The department's personnel statuses that show in this style.</summary>
		public List<string> PersonnelStatusNames { get; set; } = new List<string>();

		/// <summary>The department's priorities that show in this style.</summary>
		public List<string> PriorityNames { get; set; } = new List<string>();
	}

	/// <summary>A call type, unit type or role (or the "none" row) with its icon in each status or priority.</summary>
	public sealed class MapIconLegendRow
	{
		/// <summary>The type's or role's id; null for calls with no type, units with no type or members with no role icon.</summary>
		public int? Id { get; set; }

		public string Name { get; set; }

		/// <summary>The unit type's status set name (units only); null for the built-in unit statuses.</summary>
		public string StatusSetName { get; set; }

		/// <summary>The type's or role's own icon, as it shows with no status or priority.</summary>
		public MapMarkerIcon OwnIcon { get; set; }

		public List<MapIconLegendState> States { get; set; } = new List<MapIconLegendState>();
	}

	/// <summary>One status or priority of the department, with the map icon a type shows in it.</summary>
	public sealed class MapIconLegendState
	{
		/// <summary>The stored status or priority value.</summary>
		public int Value { get; set; }

		/// <summary>The icon slot an upload for it goes in.</summary>
		public string Variant { get; set; }

		/// <summary>The department's name for the status or priority.</summary>
		public string Name { get; set; }

		/// <summary>The status's or priority's own colour when it is a hex colour, else null.</summary>
		public string Color { get; set; }

		/// <summary>Its style; <see cref="MapIconStyles.Unknown"/> when it has no meaning.</summary>
		public string Style { get; set; }

		/// <summary>
		/// The built-in personnel status On Unit: a member in it takes the style of their unit's latest status on the map, so
		/// the gray pin shown here applies only to a member On Unit with no unit status to follow.
		/// </summary>
		public bool FollowsUnitStatus { get; set; }

		public MapMarkerIcon Icon { get; set; }
	}

	/// <summary>Builds a department's <see cref="MapIconLegend"/> from its <see cref="MapIconResolver"/>.</summary>
	public static class MapIconLegendBuilder
	{
		/// <param name="resolver">The department's resolver.</param>
		/// <param name="priorities">The department's active priorities (or the built-ins).</param>
		/// <param name="customStates">Every custom status set of the department.</param>
		/// <param name="builtInUnitStatuses">The built-in unit statuses, for unit types without a status set.</param>
		/// <param name="personnelStatuses">The department's personnel statuses (or the built-ins).</param>
		/// <param name="roles">The department's roles; only those with an uploaded icon of any kind get a row.</param>
		/// <param name="noCallTypeName">The host's name for calls with no type.</param>
		/// <param name="noUnitTypeName">The host's name for units with no type.</param>
		/// <param name="noRoleName">The host's name for members with no role icon.</param>
		public static MapIconLegend Build(MapIconResolver resolver, IEnumerable<DepartmentCallPriority> priorities, IEnumerable<CustomState> customStates,
			List<CustomStateDetail> builtInUnitStatuses, IEnumerable<CustomStateDetail> personnelStatuses, IEnumerable<PersonnelRole> roles,
			string noCallTypeName, string noUnitTypeName, string noRoleName)
		{
			var priorityList = (priorities ?? Enumerable.Empty<DepartmentCallPriority>()).Where(x => x != null).ToList();
			var statesList = (customStates ?? Enumerable.Empty<CustomState>()).Where(x => x != null).ToList();
			var personnelList = (personnelStatuses ?? Enumerable.Empty<CustomStateDetail>()).Where(x => x != null).ToList();
			var legend = new MapIconLegend();

			legend.CallRows.Add(CallRow(resolver, null, noCallTypeName, priorityList));
			foreach (var type in resolver.CallTypes.OrderBy(x => x.Type, StringComparer.CurrentCultureIgnoreCase))
				legend.CallRows.Add(CallRow(resolver, type, null, priorityList));

			legend.UnitRows.Add(UnitRow(resolver, null, noUnitTypeName, statesList, builtInUnitStatuses));
			foreach (var type in resolver.UnitTypes.OrderBy(x => x.Type, StringComparer.CurrentCultureIgnoreCase))
				legend.UnitRows.Add(UnitRow(resolver, type, null, statesList, builtInUnitStatuses));

			// Roles without an icon of any kind look like the "no role icon" row, so only roles with one are listed.
			legend.PersonnelRows.Add(RoleRow(resolver, null, noRoleName, personnelList));
			foreach (var role in TypeIcons.InRoleOrder(roles)
				.Where(x => !string.IsNullOrWhiteSpace(x.IconKey) || resolver.HasVariantIcons(TypeIconKinds.PersonnelRole, x.PersonnelRoleId)))
				legend.PersonnelRows.Add(RoleRow(resolver, role, null, personnelList));

			legend.StatusStyles = StatusStyles(legend.UnitRows, legend.PersonnelRows.FirstOrDefault());
			legend.PriorityStyles = PriorityStyles(legend.CallRows.FirstOrDefault());

			return legend;
		}

		/// <summary>
		/// The status styles, then unknown, each with the meanings it groups and the department's unit statuses (from
		/// <paramref name="unitRows"/>) and personnel statuses (from <paramref name="personnelRow"/>) that land in it.
		/// </summary>
		public static List<MapIconLegendStyle> StatusStyles(IEnumerable<MapIconLegendRow> unitRows = null, MapIconLegendRow personnelRow = null)
		{
			var unitStates = (unitRows ?? Enumerable.Empty<MapIconLegendRow>()).Where(x => x != null).SelectMany(x => x.States).ToList();
			var personnelStates = personnelRow?.States ?? new List<MapIconLegendState>();

			return MapIconStyles.StatusStyles.Concat(new[] { MapIconStyles.Unknown }).Select(style => new MapIconLegendStyle
			{
				Key = style,
				Color = MapIconStyles.ColorOf(style),
				Meanings = style == MapIconStyles.Unknown ? new List<ActionBaseTypes>() : MapIconStyles.BaseTypesOf(style).ToList(),
				UnitStatusNames = NamesIn(unitStates, style),
				PersonnelStatusNames = NamesIn(personnelStates, style)
			}).ToList();
		}

		/// <summary>The priority styles, then unknown, each with the department's priorities (from <paramref name="callRow"/>) in it.</summary>
		public static List<MapIconLegendStyle> PriorityStyles(MapIconLegendRow callRow = null)
		{
			var states = callRow?.States ?? new List<MapIconLegendState>();

			return MapIconStyles.PriorityStyles.Concat(new[] { MapIconStyles.Unknown }).Select(style => new MapIconLegendStyle
			{
				Key = style,
				Color = MapIconStyles.ColorOf(style),
				PriorityNames = NamesIn(states, style)
			}).ToList();
		}

		// On Unit takes its unit's style, so it is never listed under the style of its own pin.
		private static List<string> NamesIn(IEnumerable<MapIconLegendState> states, string style) =>
			states.Where(x => x.Style == style && !x.FollowsUnitStatus && !string.IsNullOrWhiteSpace(x.Name)).Select(x => x.Name.Trim())
				.Distinct(StringComparer.CurrentCultureIgnoreCase).ToList();

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

		public static MapIconLegendRow CallRow(MapIconResolver resolver, CallType type, string noTypeName, IEnumerable<DepartmentCallPriority> priorities) =>
			new MapIconLegendRow
			{
				Id = type?.CallTypeId,
				Name = type?.Type ?? noTypeName,
				OwnIcon = resolver.ForCallType(type, null),
				States = (priorities ?? Enumerable.Empty<DepartmentCallPriority>()).Select(p => PriorityState(p, resolver.ForCallType(type, p.DepartmentCallPriorityId))).ToList()
			};

		public static MapIconLegendRow UnitRow(MapIconResolver resolver, UnitType type, string noTypeName, IEnumerable<CustomState> customStates, List<CustomStateDetail> builtInStatuses)
		{
			var statuses = UnitTypeStatuses(type, customStates, builtInStatuses);

			return new MapIconLegendRow
			{
				Id = type?.UnitTypeId,
				Name = type?.Type ?? noTypeName,
				StatusSetName = statuses.SetName,
				OwnIcon = resolver.ForUnitType(type, null),
				States = statuses.Statuses.Select(s => StatusState(s, resolver.ForUnitType(type, s.CustomStateDetailId))).ToList()
			};
		}

		public static MapIconLegendRow RoleRow(MapIconResolver resolver, PersonnelRole role, string noRoleName, IEnumerable<CustomStateDetail> personnelStatuses) =>
			new MapIconLegendRow
			{
				Id = role?.PersonnelRoleId,
				Name = role?.Name ?? noRoleName,
				OwnIcon = resolver.ForRole(role, null),
				States = (personnelStatuses ?? Enumerable.Empty<CustomStateDetail>()).Select(s => PersonnelState(s, resolver.ForRole(role, s.CustomStateDetailId))).ToList()
			};

		private static MapIconLegendState PersonnelState(CustomStateDetail status, MapMarkerIcon icon)
		{
			var state = StatusState(status, icon);
			state.FollowsUnitStatus = icon.Variant == TypeIcons.BuiltInStatusVariant((int)ActionTypes.OnUnit);
			return state;
		}

		private static MapIconLegendState PriorityState(DepartmentCallPriority priority, MapMarkerIcon icon) =>
			new MapIconLegendState
			{
				Value = priority.DepartmentCallPriorityId,
				Variant = icon.Variant,
				Name = priority.Name,
				Color = HexOrNull(priority.Color),
				Style = icon.Style,
				Icon = icon
			};

		private static MapIconLegendState StatusState(CustomStateDetail status, MapMarkerIcon icon) =>
			new MapIconLegendState
			{
				Value = status.CustomStateDetailId,
				Variant = icon.Variant,
				Name = status.ButtonText,
				Color = HexOrNull(status.ButtonColor),
				Style = icon.Style,
				Icon = icon
			};

		/// <summary>The colour when it is a plain hex colour (safe in a style attribute or a client's colour parser), else null.</summary>
		public static string HexOrNull(string color) =>
			!string.IsNullOrWhiteSpace(color) && HexColor.IsMatch(color.Trim()) ? color.Trim() : null;

		private static readonly Regex HexColor =
			new Regex("^#([0-9a-fA-F]{3}|[0-9a-fA-F]{4}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$", RegexOptions.CultureInvariant);
	}
}
