using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model
{
	/// <summary>The icon a call, unit or person shows on the map, before it is turned into URLs for a host.</summary>
	public sealed class MapMarkerIcon
	{
		/// <summary>The bundled icon name clients that predate styled pins show ("fire", "Engine_Responding").</summary>
		public string ImagePath { get; set; }

		/// <summary>The built-in pin artwork the styled pin is drawn from ("fire", "engine", "person").</summary>
		public string PinBase { get; set; }

		/// <summary>
		/// The style of the status or priority (<see cref="MapIconStyles"/>): <see cref="MapIconStyles.Unknown"/> when it has
		/// no meaning or the unit or member has set no status; null only for a call type, unit type or role shown without
		/// any priority or status (the legend's and icon pages' "own icon").
		/// </summary>
		public string Style { get; set; }

		/// <summary>The icon slot of the status or priority (<see cref="TypeIcons.PriorityVariant"/> etc.), or null when there is none.</summary>
		public string Variant { get; set; }

		/// <summary>The department's uploaded icon to show, or null for a built-in pin.</summary>
		public string IconKey { get; set; }

		/// <summary>
		/// True when <see cref="IconKey"/> is the type's icon for this very status or priority; false when it is the type's
		/// own icon standing in for it.
		/// </summary>
		public bool IconIsVariant { get; set; }

		/// <summary>The styled built-in pin, or null when there is no style (the plain pin named by <see cref="ImagePath"/> applies).</summary>
		public string PinName => Style == null || PinBase == null ? null : TypeIcons.PinName(PinBase, Style);

		/// <summary>
		/// The badge an uploaded type icon carries: the style, when the icon is the type's own one and the style is a real
		/// priority or status; otherwise null (an unknown status leaves the upload as it is).
		/// </summary>
		public string BadgeStyle => IconKey != null && !IconIsVariant && Style != MapIconStyles.Unknown ? Style : null;
	}

	/// <summary>
	/// Picks the map icon of a call, unit or member from what it is and what it is doing, the same way for the map, the map
	/// icon legend and the icon settings pages:
	/// <list type="number">
	/// <item>the department's uploaded icon of the call type, unit type or role for the exact priority or status;</item>
	/// <item>else the type's or role's own uploaded icon, with a badge in the style of the priority or status;</item>
	/// <item>else the built-in pin the type picked (or the default call, unit or person pin), recoloured to the style;</item>
	/// <item>else, when the priority or status has no meaning, the plain built-in pin.</item>
	/// </list>
	/// A status's style comes from its meaning (<see cref="ActionBaseTypes"/>, built-in statuses through
	/// <see cref="BuiltInStatusBaseTypes"/>), a priority's from its meaning (<see cref="CallPriority"/>). A department's own
	/// status or priority with no meaning set takes the meaning its name suggests (<see cref="BaseTypeSuggestions"/>, the
	/// suggestions the settings pages offer), for the icon only: departments that set up priorities before meanings existed
	/// still get coloured calls, and nothing else reads this guess. Staffing levels never change an icon.
	/// </summary>
	public sealed class MapIconResolver
	{
		private readonly int _departmentId;
		private readonly List<CallType> _callTypes;
		private readonly List<DepartmentCallPriority> _priorities;
		private readonly List<UnitType> _unitTypes;
		private readonly List<CustomState> _customStates;
		private readonly Dictionary<int, PersonnelRole> _rolesById;
		private readonly Dictionary<string, string> _variantIconKeys;
		private readonly HashSet<string> _typesWithVariants;
		private readonly Dictionary<int, UnitState> _unitStatesById;
		private readonly CustomStateDetail _defaultPersonnelStatus;

		/// <param name="departmentId">The department the lists belong to.</param>
		/// <param name="callTypes">The department's call types.</param>
		/// <param name="priorities">The department's priorities (deleted included, so old calls resolve), or the built-ins.</param>
		/// <param name="unitTypes">The department's unit types.</param>
		/// <param name="customStates">Every custom status set of the department, deleted options included.</param>
		/// <param name="roles">The department's personnel roles.</param>
		/// <param name="variants">The department's uploaded status and priority icons.</param>
		/// <param name="defaultPersonnelStatus">The status a member with no status shows (GetDefaultPersonnelStatusAsync).</param>
		/// <param name="latestUnitStates">Each unit's latest status, so a member On Unit takes the style of the unit they ride.</param>
		public MapIconResolver(int departmentId, IEnumerable<CallType> callTypes, IEnumerable<DepartmentCallPriority> priorities,
			IEnumerable<UnitType> unitTypes, IEnumerable<CustomState> customStates, IEnumerable<PersonnelRole> roles,
			IEnumerable<TypeIconVariant> variants, CustomStateDetail defaultPersonnelStatus = null, IEnumerable<UnitState> latestUnitStates = null)
		{
			_departmentId = departmentId;
			_callTypes = (callTypes ?? Enumerable.Empty<CallType>()).Where(x => x != null).ToList();
			_priorities = (priorities ?? Enumerable.Empty<DepartmentCallPriority>()).Where(x => x != null).ToList();
			_unitTypes = (unitTypes ?? Enumerable.Empty<UnitType>()).Where(x => x != null).ToList();
			_customStates = (customStates ?? Enumerable.Empty<CustomState>()).Where(x => x != null).ToList();
			_rolesById = (roles ?? Enumerable.Empty<PersonnelRole>()).Where(x => x != null)
				.GroupBy(x => x.PersonnelRoleId).ToDictionary(g => g.Key, g => g.First());
			_variantIconKeys = new Dictionary<string, string>(StringComparer.Ordinal);
			_typesWithVariants = new HashSet<string>(StringComparer.Ordinal);
			foreach (var variant in (variants ?? Enumerable.Empty<TypeIconVariant>()).Where(x => x != null && !string.IsNullOrEmpty(x.IconKey) && !string.IsNullOrEmpty(x.Variant)))
			{
				_variantIconKeys[VariantLookupKey((TypeIconKinds)variant.TypeKind, variant.TypeId, variant.Variant)] = variant.IconKey;
				_typesWithVariants.Add(TypeLookupKey((TypeIconKinds)variant.TypeKind, variant.TypeId));
			}
			_unitStatesById = (latestUnitStates ?? Enumerable.Empty<UnitState>()).Where(x => x != null && x.UnitStateId > 0)
				.GroupBy(x => x.UnitStateId).ToDictionary(g => g.Key, g => g.First());
			_defaultPersonnelStatus = defaultPersonnelStatus;
		}

		/// <summary>The department's call types, for the legend.</summary>
		public IReadOnlyList<CallType> CallTypes => _callTypes;

		/// <summary>The department's unit types, for the legend.</summary>
		public IReadOnlyList<UnitType> UnitTypes => _unitTypes;

		/// <summary>The icon of a call: its type (linked by id, else by name) and its priority.</summary>
		public MapMarkerIcon ForCall(Call call)
		{
			if (call == null)
				return ForCallType(null, null);

			var type = string.IsNullOrWhiteSpace(call.Type) && !call.CallTypeId.HasValue ? null : CallTypeLink.Find(call, _callTypes);
			return ForCallType(type, call.Priority);
		}

		/// <summary>The icon of a call of <paramref name="type"/> (null: a call with no type) at <paramref name="priority"/> (null: none).</summary>
		public MapMarkerIcon ForCallType(CallType type, int? priority)
		{
			var builtIn = TypeIcons.BuiltInIconName(type);
			var icon = new MapMarkerIcon
			{
				ImagePath = builtIn ?? TypeIcons.LegacyCallImage,
				PinBase = builtIn ?? TypeIcons.CallPinBase
			};

			if (priority.HasValue)
			{
				icon.Variant = TypeIcons.PriorityVariant(priority.Value);
				var meaning = PriorityMeaning(priority.Value);
				icon.Style = (meaning.HasValue ? MapIconStyles.ForPriorityBaseType(meaning.Value) : null) ?? MapIconStyles.Unknown;
			}

			if (type != null)
				PickUploaded(icon, TypeIconKinds.CallType, type.CallTypeId, type.IconKey);

			return icon;
		}

		/// <summary>The icon of a unit: its type (matched by name) and its latest status; a unit that never set one is unknown.</summary>
		public MapMarkerIcon ForUnit(Unit unit, UnitState state)
		{
			var type = unit == null || string.IsNullOrWhiteSpace(unit.Type) ? null : _unitTypes.FirstOrDefault(x => x.Type == unit.Type);
			var icon = ForUnitType(type, state?.State);
			icon.Style ??= MapIconStyles.Unknown;
			return icon;
		}

		/// <summary>The icon of a unit of <paramref name="type"/> (null: no type) in status <paramref name="state"/> (null: none).</summary>
		public MapMarkerIcon ForUnitType(UnitType type, int? state)
		{
			var builtIn = TypeIcons.BuiltInIconName(type);
			var icon = new MapMarkerIcon
			{
				ImagePath = builtIn ?? TypeIcons.LegacyUnitImage,
				PinBase = builtIn ?? TypeIcons.UnitPinBase
			};

			if (state.HasValue)
			{
				var status = UnitStatus(state.Value);
				icon.Variant = status.Variant;
				icon.Style = StatusStyle(status.Meaning);
			}

			if (type != null)
				PickUploaded(icon, TypeIconKinds.UnitType, type.UnitTypeId, type.IconKey);

			return icon;
		}

		/// <summary>
		/// The icon of a member holding <paramref name="roleIds"/> with latest status <paramref name="state"/> (null: they have
		/// set none, so the department's default status applies). The icon is that of the first role they hold, in the
		/// department's role order, with an uploaded icon for the status or an icon of its own.
		/// </summary>
		public MapMarkerIcon ForPerson(IEnumerable<int> roleIds, ActionLog state)
		{
			var status = state != null ? PersonnelStatus(state) : DefaultPersonnelStatus();
			var icon = new MapMarkerIcon
			{
				ImagePath = TypeIcons.LegacyPersonImage,
				PinBase = TypeIcons.PersonPinBase,
				Variant = status.Variant,
				Style = StatusStyle(status.Meaning)
			};

			var heldRoles = (roleIds ?? Enumerable.Empty<int>()).Where(_rolesById.ContainsKey).Select(id => _rolesById[id]);
			foreach (var role in TypeIcons.InRoleOrder(heldRoles))
			{
				if (PickUploaded(icon, TypeIconKinds.PersonnelRole, role.PersonnelRoleId, role.IconKey))
					break;
			}

			return icon;
		}

		/// <summary>
		/// The icon a member shows when <paramref name="role"/> is the role their icon comes from (null: a member with no role
		/// icon), in personnel status <paramref name="status"/> (null: none), for the legend and the role's icon page.
		/// </summary>
		public MapMarkerIcon ForRole(PersonnelRole role, int? status)
		{
			var resolved = status.HasValue ? PersonnelStatus(new ActionLog { ActionTypeId = status.Value }) : (Variant: null, Meaning: (int?)null);
			var icon = new MapMarkerIcon
			{
				ImagePath = TypeIcons.LegacyPersonImage,
				PinBase = TypeIcons.PersonPinBase,
				Variant = resolved.Variant,
				Style = status.HasValue ? StatusStyle(resolved.Meaning) : null
			};

			if (role != null)
				PickUploaded(icon, TypeIconKinds.PersonnelRole, role.PersonnelRoleId, role.IconKey);

			return icon;
		}

		/// <summary>
		/// The meaning of a stored call priority for its icon: the department's own priority of that id first (the oldest
		/// departments own ids 0 to 3), else the built-in priority of that value. An own priority with no meaning set takes the
		/// one its name suggests; null when there is none.
		/// </summary>
		public int? PriorityMeaning(int priority)
		{
			var own = _priorities.FirstOrDefault(x => x.DepartmentId == _departmentId && _departmentId > 0 && x.DepartmentCallPriorityId == priority);
			if (own != null)
				return own.BaseType >= 0 ? own.BaseType : BaseTypeSuggestions.SuggestPriority(own.Name);

			return Enum.IsDefined(typeof(CallPriority), priority) ? priority : (int?)null;
		}

		/// <summary>The icon slot and meaning of a stored unit status: one of the department's options, else a built-in one.</summary>
		public (string Variant, int? Meaning) UnitStatus(int state)
		{
			var custom = StatusOptionIds.FindCustom(_customStates, CustomStateTypes.Unit, state);
			if (custom != null)
				return (TypeIcons.CustomStatusVariant(custom.CustomStateDetailId), OptionMeaning(custom, CustomStateTypes.Unit));

			if (state < 0 || state > CallStatusLinkage.MaxBuiltInStatusId)
				return (null, null);

			return (TypeIcons.BuiltInStatusVariant(state), (int)BuiltInStatusBaseTypes.ForUnitState(state));
		}

		/// <summary>
		/// The icon slot and meaning of a member's status: one of the department's options, else a built-in one; a built-in
		/// On Unit linked to its unit's latest status takes that unit status's meaning.
		/// </summary>
		public (string Variant, int? Meaning) PersonnelStatus(ActionLog state)
		{
			var custom = StatusOptionIds.FindCustom(_customStates, CustomStateTypes.Personnel, state.ActionTypeId);
			if (custom != null)
				return (TypeIcons.CustomStatusVariant(custom.CustomStateDetailId), OptionMeaning(custom, CustomStateTypes.Personnel));

			if (state.ActionTypeId < 0 || state.ActionTypeId > CallStatusLinkage.MaxBuiltInStatusId)
				return (null, null);

			var variant = TypeIcons.BuiltInStatusVariant(state.ActionTypeId);

			if (PersonnelOnUnit.IsLinkedOnUnit(state, false) && _unitStatesById.TryGetValue(state.UnitStateId.Value, out var unitState))
				return (variant, UnitStatus(unitState.State).Meaning);

			return (variant, (int)BuiltInStatusBaseTypes.ForPersonnelStatus(state.ActionTypeId));
		}

		private (string Variant, int? Meaning) DefaultPersonnelStatus()
		{
			if (_defaultPersonnelStatus == null)
				return (null, null);

			if (_defaultPersonnelStatus.CustomStateDetailId > 0 &&
				StatusOptionIds.FindCustom(_customStates, CustomStateTypes.Personnel, _defaultPersonnelStatus.CustomStateDetailId) != null)
				return (TypeIcons.CustomStatusVariant(_defaultPersonnelStatus.CustomStateDetailId), OptionMeaning(_defaultPersonnelStatus, CustomStateTypes.Personnel));

			return (TypeIcons.BuiltInStatusVariant((int)ActionTypes.StandingBy), (int)BuiltInStatusBaseTypes.ForPersonnelStatus((int)ActionTypes.StandingBy));
		}

		private static string StatusStyle(int? meaning) =>
			(meaning.HasValue ? MapIconStyles.ForStatusBaseType(meaning.Value) : null) ?? MapIconStyles.Unknown;

		/// <summary>A department option's meaning for its icon: the one set on it, else the one its name suggests (null for none).</summary>
		private static int? OptionMeaning(CustomStateDetail option, CustomStateTypes type) =>
			option.BaseType >= 0 ? option.BaseType : BaseTypeSuggestions.SuggestStatus(option.ButtonText, type);

		/// <summary>True when the type or role has an uploaded icon for the icon's slot or one of its own; sets it on the icon.</summary>
		private bool PickUploaded(MapMarkerIcon icon, TypeIconKinds kind, int typeId, string ownIconKey)
		{
			if (icon.Variant != null && _variantIconKeys.TryGetValue(VariantLookupKey(kind, typeId, icon.Variant), out var variantKey))
			{
				icon.IconKey = variantKey;
				icon.IconIsVariant = true;
				return true;
			}

			if (!string.IsNullOrWhiteSpace(ownIconKey))
			{
				icon.IconKey = ownIconKey;
				icon.IconIsVariant = false;
				return true;
			}

			return false;
		}

		/// <summary>The uploaded icon of a type's slot, or null; for the icon settings pages.</summary>
		public string VariantIconKey(TypeIconKinds kind, int typeId, string variant) =>
			variant != null && _variantIconKeys.TryGetValue(VariantLookupKey(kind, typeId, variant), out var key) ? key : null;

		/// <summary>True when the type or role has an uploaded icon for at least one status or priority.</summary>
		public bool HasVariantIcons(TypeIconKinds kind, int typeId) => _typesWithVariants.Contains(TypeLookupKey(kind, typeId));

		private static string VariantLookupKey(TypeIconKinds kind, int typeId, string variant) => $"{TypeLookupKey(kind, typeId)}:{variant}";

		private static string TypeLookupKey(TypeIconKinds kind, int typeId) => $"{(int)kind}:{typeId}";
	}
}
