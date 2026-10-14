using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model
{
	/// <summary>
	/// A setting that matches statuses (notification rules, availability alerts, check-in timers, ...) stores a list of
	/// tokens: a raw status value (a built-in value or one of the department's option ids) matches that status only, and
	/// "b{n}" matches every status whose base type is n, built-in and custom, in any status set. "-1" (or nothing) is any.
	/// Base types are <see cref="ActionBaseTypes"/> for personnel and unit statuses and <see cref="StaffingBaseTypes"/> for
	/// staffing levels.
	/// </summary>
	public static class StatusMatch
	{
		public const string AnyToken = "-1";
		private const string BaseTypePrefix = "b";

		public static string BaseTypeToken(int baseType) => BaseTypePrefix + baseType;

		/// <summary>
		/// For an integer list that holds status values (built-in values and custom option ids, which are any positive
		/// number): a status meaning is stored as -100 - base type, which no status value can be.
		/// </summary>
		public static int MeaningListValue(int baseType) => -100 - baseType;

		/// <summary>The base type an integer list entry names, or null when it is a status value.</summary>
		public static int? MeaningFromListValue(int value) => value <= -100 ? -100 - value : (int?)null;

		public static bool IsAny(string token) => String.IsNullOrWhiteSpace(token) || token.Trim() == AnyToken;

		/// <summary>The base type a token names, or null when it names a specific status.</summary>
		public static int? BaseTypeOf(string token)
		{
			if (String.IsNullOrWhiteSpace(token))
				return null;

			var trimmed = token.Trim();
			return trimmed.StartsWith(BaseTypePrefix, StringComparison.OrdinalIgnoreCase) && int.TryParse(trimmed.Substring(1), out var baseType)
				? baseType
				: (int?)null;
		}

		/// <summary>Splits a stored comma-separated token list.</summary>
		public static List<string> Tokens(string stored)
		{
			return (stored ?? String.Empty).Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
		}

		/// <summary>True when the token matches the status: any, the same raw value, or the status's base type.</summary>
		public static bool Matches(string token, int rawValue, Func<int, int> baseTypeOf)
		{
			if (IsAny(token))
				return true;

			var baseType = BaseTypeOf(token);
			if (baseType.HasValue)
				return baseTypeOf != null && baseTypeOf(rawValue) == baseType.Value;

			return int.TryParse(token.Trim(), out var value) && value == rawValue;
		}

		/// <summary>True when any token in a stored list matches; an empty list matches nothing.</summary>
		public static bool MatchesAny(string storedTokens, int rawValue, Func<int, int> baseTypeOf)
		{
			return Tokens(storedTokens).Any(token => Matches(token, rawValue, baseTypeOf));
		}

		/// <summary>The base type of a stored unit status for one department: its own option's, else the built-in one's.</summary>
		public static Func<int, int> UnitBaseTypes(IEnumerable<CustomState> customStates)
		{
			var map = CallStatusLinkage.BuildBaseTypeMap(customStates, CustomStateTypes.Unit);
			return raw => map.TryGetValue(raw, out var baseType) ? baseType : (int)BuiltInStatusBaseTypes.ForUnitState(raw);
		}

		/// <summary>The base type of a stored personnel status for one department.</summary>
		public static Func<int, int> PersonnelBaseTypes(IEnumerable<CustomState> customStates)
		{
			var map = CallStatusLinkage.BuildBaseTypeMap(customStates, CustomStateTypes.Personnel);
			return raw => map.TryGetValue(raw, out var baseType) ? baseType : (int)BuiltInStatusBaseTypes.ForPersonnelStatus(raw);
		}

		/// <summary>The staffing base type of a stored staffing level for one department.</summary>
		public static Func<int, int> StaffingBaseTypes(IEnumerable<CustomState> customStates)
		{
			var map = CallStatusLinkage.BuildBaseTypeMap(customStates, CustomStateTypes.Staffing);
			return raw => map.TryGetValue(raw, out var baseType) ? baseType : (int)BuiltInStatusBaseTypes.ForStaffing(raw);
		}
	}
}
