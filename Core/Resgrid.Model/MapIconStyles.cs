using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model
{
	/// <summary>
	/// The look a map marker takes for what its unit or person is doing, or how urgent its call is. A style is a coarse group
	/// of meanings (<see cref="ActionBaseTypes"/> for statuses, <see cref="CallPriority"/> for priorities), so every
	/// department's statuses and priorities, built-in or custom, land on the same small map language whatever they are
	/// named or coloured. The built-in pins are recoloured to the style's colour; an uploaded icon without an icon of its own
	/// for the status or priority carries a badge of that colour. The keys are wire values (map marker <c>IconStyle</c>,
	/// pin names, the <c>s</c> parameter of the icon endpoints): never rename one.
	/// </summary>
	public static class MapIconStyles
	{
		/// <summary>
		/// Bumped whenever a colour or the pin artwork changes, so pin and badge URLs change with it and clients holding the
		/// old images under an immutable cache header load the new ones.
		/// </summary>
		public const int Version = 1;

		// Unit and personnel status styles.
		public const string Available = "available";
		public const string Dispatched = "dispatched";
		public const string Responding = "responding";
		public const string OnScene = "onscene";
		public const string Transporting = "transporting";
		public const string Returning = "returning";
		public const string Staging = "staging";
		public const string Delayed = "delayed";
		public const string Unavailable = "unavailable";

		/// <summary>
		/// A call, unit or member whose priority or status has no meaning (and a name that suggests none), or a unit or
		/// member that has set no status: a gray pin, so it never passes for one of the coloured styles.
		/// </summary>
		public const string Unknown = "unknown";

		// Call priority styles.
		public const string PriorityLow = "low";
		public const string PriorityMedium = "medium";
		public const string PriorityHigh = "high";
		public const string PriorityEmergency = "emergency";

		/// <summary>The status styles, in the order a legend lists them.</summary>
		public static readonly IReadOnlyList<string> StatusStyles = new[]
		{
			Available, Dispatched, Responding, OnScene, Transporting, Returning, Staging, Delayed, Unavailable
		};

		/// <summary>The priority styles, least to most urgent.</summary>
		public static readonly IReadOnlyList<string> PriorityStyles = new[] { PriorityLow, PriorityMedium, PriorityHigh, PriorityEmergency };

		private static readonly Dictionary<string, string> Colors = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			// Dark enough that the white glyph of a built-in pin stays readable on every one.
			{ Available, "#2E9E44" },
			{ Dispatched, "#B7791F" },
			{ Responding, "#D93A1E" },
			{ OnScene, "#1D5FD1" },
			{ Transporting, "#7A3EBE" },
			{ Returning, "#0C8C99" },
			{ Staging, "#8A6A3A" },
			{ Delayed, "#607D8B" },
			{ Unavailable, "#3D3D3D" },
			{ Unknown, "#8A8A8A" },
			// The built-in priorities' own colours (green, yellow, orange, red), darkened for the same reason.
			{ PriorityLow, "#2E8B3E" },
			{ PriorityMedium, "#B59A00" },
			{ PriorityHigh, "#E07800" },
			{ PriorityEmergency, "#C8102E" }
		};

		/// <summary>True for a known style key.</summary>
		public static bool IsStyle(string style) => style != null && Colors.ContainsKey(style);

		/// <summary>True for a status style (units and personnel).</summary>
		public static bool IsStatusStyle(string style) => style != null && StatusStyles.Contains(style);

		/// <summary>True for a call priority style.</summary>
		public static bool IsPriorityStyle(string style) => style != null && PriorityStyles.Contains(style);

		/// <summary>The style's colour as "#RRGGBB", or null for an unknown style.</summary>
		public static string ColorOf(string style) => style != null && Colors.TryGetValue(style, out var color) ? color : null;

		/// <summary>
		/// The style for a status meaning (<see cref="ActionBaseTypes"/>), or null for a status without one (the resolver then
		/// uses <see cref="Unknown"/>).
		/// </summary>
		public static string ForStatusBaseType(int baseType)
		{
			switch ((ActionBaseTypes)baseType)
			{
				case ActionBaseTypes.Available:
				case ActionBaseTypes.Cleared:
				case ActionBaseTypes.OnPatrol:
				case ActionBaseTypes.Completed:
				case ActionBaseTypes.InQuarters:
					return Available;
				case ActionBaseTypes.Dispatched:
					return Dispatched;
				case ActionBaseTypes.Responding:
				case ActionBaseTypes.Enroute:
					return Responding;
				case ActionBaseTypes.OnScene:
				case ActionBaseTypes.MadeContact:
				case ActionBaseTypes.Investigating:
				case ActionBaseTypes.AtPatient:
				case ActionBaseTypes.Searching:
					return OnScene;
				case ActionBaseTypes.Transporting:
				case ActionBaseTypes.Delivering:
				case ActionBaseTypes.Loading:
				case ActionBaseTypes.AtHospital:
					return Transporting;
				case ActionBaseTypes.Returning:
					return Returning;
				case ActionBaseTypes.Staging:
				case ActionBaseTypes.Standby:
					return Staging;
				case ActionBaseTypes.OnBreak:
					return Delayed;
				case ActionBaseTypes.NotResponding:
				case ActionBaseTypes.Unavailable:
				case ActionBaseTypes.Maintenance:
					return Unavailable;
				default:
					return null;
			}
		}

		/// <summary>The status meanings a status style groups, for the legend.</summary>
		public static IEnumerable<ActionBaseTypes> BaseTypesOf(string style) =>
			Enum.GetValues(typeof(ActionBaseTypes)).Cast<ActionBaseTypes>().Where(x => ForStatusBaseType((int)x) == style);

		/// <summary>The style for a priority meaning (a <see cref="CallPriority"/> value), or null for none.</summary>
		public static string ForPriorityBaseType(int baseType)
		{
			switch ((CallPriority)baseType)
			{
				case CallPriority.Low:
					return PriorityLow;
				case CallPriority.Medium:
					return PriorityMedium;
				case CallPriority.High:
					return PriorityHigh;
				case CallPriority.Emergency:
					return PriorityEmergency;
				default:
					return null;
			}
		}
	}
}
