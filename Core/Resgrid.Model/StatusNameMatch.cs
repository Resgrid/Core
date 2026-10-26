using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model
{
	/// <summary>
	/// Name matching for a status or staffing level saved before meanings existed (a built-in value, or one of the
	/// department's options since deleted) when no option of the department's set has its meaning. Sets made before meanings
	/// mostly have none on any option (the column defaulted to None), so a daily reset saved as "Standing By" or "Unavailable"
	/// finds nothing by meaning. Only options without a meaning are matched by name, so a meaning an administrator chose is
	/// never second-guessed.
	/// </summary>
	public static class StatusNameMatch
	{
		/// <summary>
		/// The first option (in the order given) without a meaning whose name is <paramref name="savedName"/>, ignoring case,
		/// spaces and punctuation; else the first whose name suggests the saved value's meaning (<paramref name="meaning"/>, or
		/// the one <paramref name="savedName"/> suggests when it has none). Null when no option does.
		/// </summary>
		/// <param name="activeOptions">The department set's active options, in order.</param>
		/// <param name="type">The kind of set (personnel status, staffing or unit status).</param>
		/// <param name="savedName">The saved value's name: the built-in status's or the deleted option's text.</param>
		/// <param name="meaning">The saved value's meaning, or null (or None) when it has none.</param>
		public static CustomStateDetail Find(IEnumerable<CustomStateDetail> activeOptions, CustomStateTypes type, string savedName, int? meaning)
		{
			var candidates = (activeOptions ?? Enumerable.Empty<CustomStateDetail>()).Where(x => x != null && x.BaseType < 0).ToList();
			if (candidates.Count == 0)
				return null;

			var name = BaseTypeSuggestions.Normalize(savedName);
			if (name.Length > 0)
			{
				var sameName = candidates.FirstOrDefault(x => BaseTypeSuggestions.Normalize(x.ButtonText) == name);
				if (sameName != null)
					return sameName;
			}

			var savedMeaning = meaning.HasValue && meaning.Value >= 0 ? meaning : BaseTypeSuggestions.SuggestStatus(savedName, type);
			if (!savedMeaning.HasValue || savedMeaning.Value < 0)
				return null;

			return candidates.FirstOrDefault(x => BaseTypeSuggestions.SuggestStatus(x.ButtonText, type) == savedMeaning.Value);
		}
	}
}
