using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Resgrid.Model
{
	/// <summary>
	/// A unit status's button and text colours as the department set them up, for the boards that list units with their
	/// status (the nearest-unit board, run card recommendations), so a status reads the same there as everywhere else.
	/// </summary>
	public static class UnitStatusColors
	{
		private static readonly Regex HexColor = new Regex("^#([0-9a-fA-F]{3}|[0-9a-fA-F]{4}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$", RegexOptions.Compiled);

		/// <summary>The built-in unit status options (ICustomStateService.GetDefaultUnitStatuses) by state id.</summary>
		public static Dictionary<int, CustomStateDetail> BuildDefaultMap(IEnumerable<CustomStateDetail> defaultStatuses)
		{
			return (defaultStatuses ?? Enumerable.Empty<CustomStateDetail>())
				.Where(d => d != null)
				.GroupBy(d => d.CustomStateDetailId)
				.ToDictionary(g => g.Key, g => g.First());
		}

		/// <summary>
		/// The colours of the option the unit's state was set from: the department's own option, else the built-in one.
		/// Each is a hex colour or null: an old built-in state that no longer has an option has neither, and an older
		/// option can hold a CSS class name instead of a colour, which is passed on only when it maps to one.
		/// </summary>
		public static (string Color, string TextColor) Resolve(int stateId, IReadOnlyDictionary<int, CustomStateDetail> customDetails,
			IReadOnlyDictionary<int, CustomStateDetail> defaultDetails)
		{
			CustomStateDetail detail = null;

			if (customDetails == null || !customDetails.TryGetValue(stateId, out detail) || detail == null)
			{
				if (defaultDetails == null || !defaultDetails.TryGetValue(stateId, out detail))
					detail = null;
			}

			if (detail == null)
				return (null, null);

			return (ToHexColor(detail.ButtonClassToColor()), ToHexColor(detail.TextColor));
		}

		/// <summary>The colour as "#rgb", "#rgba", "#rrggbb" or "#rrggbbaa", else null.</summary>
		public static string ToHexColor(string color)
		{
			if (string.IsNullOrWhiteSpace(color))
				return null;

			color = color.Trim();

			return HexColor.IsMatch(color) ? color : null;
		}
	}
}
