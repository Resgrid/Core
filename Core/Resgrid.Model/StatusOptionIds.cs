using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model
{
	/// <summary>
	/// Tells a stored status, staffing or unit status value apart: it is either a built-in enum value
	/// (<see cref="ActionTypes"/>, <see cref="UserStateTypes"/>, <see cref="UnitStateTypes"/>) or the id of one of the
	/// department's custom options (<see cref="CustomStateDetail.CustomStateDetailId"/>). The value is custom when it is one
	/// of the department's custom option ids of that kind, deleted options included so history still resolves; otherwise it
	/// is built-in. There is no numeric cut-off: on a fresh install custom ids start at 1.
	/// </summary>
	public static class StatusOptionIds
	{
		/// <summary>Every custom option id of one kind for a department, deleted options and deleted sets included.</summary>
		public static HashSet<int> CustomIds(IEnumerable<CustomState> customStates, CustomStateTypes type)
		{
			var ids = new HashSet<int>();

			if (customStates == null)
				return ids;

			foreach (var state in customStates.Where(x => x != null && x.Type == (int)type && x.Details != null))
			{
				foreach (var detail in state.Details.Where(x => x != null && x.CustomStateDetailId > 0))
					ids.Add(detail.CustomStateDetailId);
			}

			return ids;
		}

		/// <summary>
		/// The department's custom option for a stored value, deleted options included, or null when the value is built-in.
		/// </summary>
		public static CustomStateDetail FindCustom(IEnumerable<CustomState> customStates, CustomStateTypes type, int rawValue)
		{
			if (customStates == null || rawValue <= 0)
				return null;

			return customStates
				.Where(x => x != null && x.Type == (int)type && x.Details != null)
				.SelectMany(x => x.Details)
				.FirstOrDefault(x => x != null && x.CustomStateDetailId == rawValue);
		}

		/// <summary>True when the stored value is one of the department's custom options (by a base-type map or id set).</summary>
		public static bool IsCustom<T>(int rawValue, IReadOnlyDictionary<int, T> customOptions) =>
			customOptions != null && customOptions.ContainsKey(rawValue);
	}
}
