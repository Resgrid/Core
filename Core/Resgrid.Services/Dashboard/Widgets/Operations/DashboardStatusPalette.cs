using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Dashboard.Widgets.Operations;
using Resgrid.Model.Reporting;
using Resgrid.Model.Services;

namespace Resgrid.Services.Dashboard.Widgets
{
	/// <summary>
	/// Turns stored status values into <see cref="DashboardStatusChip"/>s with the department's colours, by the same rules as
	/// ICustomStateService.GetCustomUnitStateAsync / GetCustomPersonnelStatusAsync / GetCustomPersonnelStaffingAsync: the
	/// department's own option first (deleted options included, so history still reads), then the built-in value. Built-in
	/// colours come from the service's default option lists; a built-in value outside them gets its meaning's map colour.
	/// One instance per provider call, built from the cached custom-state read.
	/// </summary>
	public sealed class DashboardStatusPalette
	{
		private const string LightText = "#ffffff";
		private const string UnknownBackground = "#d1dade";
		private const string UnknownText = "#5E5E5E";

		private readonly IReadOnlyList<CustomState> _customStates;
		private readonly ICustomStateService _customStateService;
		private List<CustomStateDetail> _unitDefaults;
		private List<CustomStateDetail> _statusDefaults;
		private List<CustomStateDetail> _staffingDefaults;

		public DashboardStatusPalette(IEnumerable<CustomState> customStates, ICustomStateService customStateService)
		{
			_customStates = (customStates ?? Enumerable.Empty<CustomState>()).Where(x => x != null).ToList();
			_customStateService = customStateService;
		}

		public static async Task<DashboardStatusPalette> LoadAsync(ICustomStateService customStateService, int departmentId)
		{
			var states = await customStateService.GetAllCustomStatesForDepartmentAsync(departmentId);
			return new DashboardStatusPalette((states ?? new List<CustomState>()).Where(x => x.DepartmentId == departmentId), customStateService);
		}

		/// <summary>The department's custom option for a unit status value, or null when the value is built-in.</summary>
		public CustomStateDetail UnitOption(int value) => StatusOptionIds.FindCustom(_customStates, CustomStateTypes.Unit, value);

		public CustomStateDetail PersonnelStatusOption(int value) => StatusOptionIds.FindCustom(_customStates, CustomStateTypes.Personnel, value);

		public CustomStateDetail StaffingOption(int value) => StatusOptionIds.FindCustom(_customStates, CustomStateTypes.Staffing, value);

		public DashboardStatusChip Unit(int value)
		{
			var custom = UnitOption(value);
			if (custom != null)
				return FromCustomStatus(custom);

			if (value > CallStatusLinkage.MaxBuiltInStatusId)
				return Unknown(value);

			var builtIn = (_unitDefaults ??= Defaults(() => _customStateService?.GetDefaultUnitStatuses())).FirstOrDefault(x => x.CustomStateDetailId == value);
			var baseType = (int)BuiltInStatusBaseTypes.ForUnitState(value);

			return BuiltIn(value, builtIn?.ButtonText ?? new UnitState { State = value }.GetStatusText(), builtIn, StatusColor(baseType), baseType,
				AvailabilityMatrix.ForUnitStateType(value));
		}

		public DashboardStatusChip PersonnelStatus(int value)
		{
			var custom = PersonnelStatusOption(value);
			if (custom != null)
				return FromCustomStatus(custom);

			if (value > CallStatusLinkage.MaxBuiltInStatusId)
				return Unknown(value);

			var builtIn = (_statusDefaults ??= Defaults(() => _customStateService?.GetDefaultPersonStatuses())).FirstOrDefault(x => x.CustomStateDetailId == value);
			var baseType = (int)BuiltInStatusBaseTypes.ForPersonnelStatus(value);

			return BuiltIn(value, builtIn?.ButtonText ?? new ActionLog { ActionTypeId = value }.GetActionText(), builtIn, StatusColor(baseType), baseType,
				AvailabilityMatrix.ForBuiltInPersonnelActionType(value));
		}

		public DashboardStatusChip Staffing(int value)
		{
			var custom = StaffingOption(value);
			if (custom != null)
			{
				return new DashboardStatusChip
				{
					Id = custom.CustomStateDetailId,
					Text = custom.ButtonText,
					Color = Hex(custom.ButtonColor) ?? StaffingColor(custom.BaseType),
					TextColor = Hex(custom.TextColor) ?? LightText,
					BaseType = custom.BaseType,
					Availability = (int)AvailabilityMatrix.ForStaffingBaseType(custom.BaseType),
					IsCustom = true
				};
			}

			if (value > CallStatusLinkage.MaxBuiltInStatusId)
				return Unknown(value);

			var builtIn = (_staffingDefaults ??= Defaults(() => _customStateService?.GetDefaultPersonStaffings())).FirstOrDefault(x => x.CustomStateDetailId == value);
			var baseType = (int)BuiltInStatusBaseTypes.ForStaffing(value);

			return BuiltIn(value, builtIn?.ButtonText ?? new UserState { State = value }.GetStaffingText(), builtIn, StaffingColor(baseType), baseType,
				AvailabilityMatrix.ForStaffingBaseType(baseType));
		}

		/// <summary>A chip for a detail the caller already holds (a department option or a built-in default list entry).</summary>
		public static DashboardStatusChip FromCustomStatus(CustomStateDetail detail)
		{
			return new DashboardStatusChip
			{
				Id = detail.CustomStateDetailId,
				Text = detail.ButtonText,
				Color = Hex(detail.ButtonColor) ?? StatusColor(detail.BaseType),
				TextColor = Hex(detail.TextColor) ?? LightText,
				BaseType = detail.BaseType,
				Availability = (int)AvailabilityMatrix.ForCustomBaseType(detail.BaseType),
				IsCustom = true
			};
		}

		private static DashboardStatusChip BuiltIn(int value, string text, CustomStateDetail builtIn, string fallbackColor, int baseType, AvailabilityClass availability)
		{
			return new DashboardStatusChip
			{
				Id = value,
				Text = text,
				Color = Hex(builtIn?.ButtonColor) ?? fallbackColor,
				TextColor = Hex(builtIn?.TextColor) ?? LightText,
				BaseType = baseType,
				Availability = (int)availability,
				IsCustom = false
			};
		}

		private static DashboardStatusChip Unknown(int value)
		{
			return new DashboardStatusChip
			{
				Id = value,
				Text = "Unknown",
				Color = UnknownBackground,
				TextColor = UnknownText,
				BaseType = (int)ActionBaseTypes.None,
				Availability = (int)AvailabilityClass.Unknown,
				IsCustom = false
			};
		}

		private static List<CustomStateDetail> Defaults(Func<List<CustomStateDetail>> read) =>
			(read() ?? new List<CustomStateDetail>()).Where(x => x != null).ToList();

		/// <summary>The map style colour of a status meaning (gray when it has none).</summary>
		private static string StatusColor(int baseType) =>
			MapIconStyles.ColorOf(MapIconStyles.ForStatusBaseType(baseType) ?? MapIconStyles.Unknown) ?? UnknownBackground;

		private static string StaffingColor(int baseType)
		{
			switch ((StaffingBaseTypes)baseType)
			{
				case StaffingBaseTypes.Available:
				case StaffingBaseTypes.OnShift:
				case StaffingBaseTypes.OnCall:
					return MapIconStyles.ColorOf(MapIconStyles.Available);
				case StaffingBaseTypes.OffDuty:
					return MapIconStyles.ColorOf(MapIconStyles.Unavailable);
				case StaffingBaseTypes.Delayed:
					return MapIconStyles.ColorOf(MapIconStyles.Delayed);
				case StaffingBaseTypes.Committed:
					return MapIconStyles.ColorOf(MapIconStyles.Dispatched);
				case StaffingBaseTypes.Unavailable:
					return MapIconStyles.ColorOf(MapIconStyles.Unavailable);
				default:
					return MapIconStyles.ColorOf(MapIconStyles.Unknown);
			}
		}

		/// <summary>A "#rgb"/"#rrggbb" colour as stored, or null for an empty value or a CSS class name.</summary>
		internal static string Hex(string color)
		{
			if (string.IsNullOrWhiteSpace(color))
				return null;

			var trimmed = color.Trim();
			if (trimmed[0] != '#' || (trimmed.Length != 4 && trimmed.Length != 7 && trimmed.Length != 9))
				return null;

			for (var i = 1; i < trimmed.Length; i++)
			{
				if (!Uri.IsHexDigit(trimmed[i]))
					return null;
			}

			return trimmed;
		}
	}
}
