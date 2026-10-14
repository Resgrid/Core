using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Resgrid.Model
{
	/// <summary>
	/// Picks a department call priority for a call created from outside text (email, text message and voice imports).
	/// </summary>
	public static class CallPriorityMatch
	{
		private static readonly Regex Word = new Regex(@"[\p{L}\p{N}]+", RegexOptions.Compiled);

		/// <summary>
		/// The priority an import uses when its text names none: the department's default priority, else its first priority
		/// meaning High, else the built-in High. A department with its own priorities and neither still gets the built-in
		/// High, as before, rather than one of its priorities picked at random.
		/// </summary>
		public static int DefaultForImport(IEnumerable<DepartmentCallPriority> priorities)
		{
			var active = Active(priorities);
			var preferred = active.FirstOrDefault(x => x.IsDefault)
				?? active.Where(x => x.BaseType == (int)CallPriority.High).OrderBy(x => x.Sort).ThenBy(x => x.DepartmentCallPriorityId).FirstOrDefault();

			return preferred?.DepartmentCallPriorityId ?? (int)CallPriority.High;
		}

		/// <summary>
		/// The department priority the text names: one whose name is exactly <paramref name="exactName"/> when given, else one
		/// whose every word appears in <paramref name="text"/> in any order ("Fire Structure Residential" names "Structure
		/// Fire"). Among word matches the longest name wins, then sort order. Null when none matches.
		/// </summary>
		public static DepartmentCallPriority Find(IEnumerable<DepartmentCallPriority> priorities, string text, string exactName = null)
		{
			var active = Active(priorities).Where(x => !String.IsNullOrWhiteSpace(x.Name)).ToList();

			if (!String.IsNullOrWhiteSpace(exactName))
			{
				var exact = active.FirstOrDefault(x => String.Equals(x.Name.Trim(), exactName.Trim(), StringComparison.OrdinalIgnoreCase));
				if (exact != null)
					return exact;
			}

			var textWords = Words(text);
			if (textWords.Count == 0)
				return null;

			return active
				.Select(x => (Priority: x, Words: Words(x.Name)))
				.Where(x => x.Words.Count > 0 && x.Words.All(textWords.Contains))
				.OrderByDescending(x => x.Words.Count)
				.ThenBy(x => x.Priority.Sort)
				.ThenBy(x => x.Priority.DepartmentCallPriorityId)
				.Select(x => x.Priority)
				.FirstOrDefault();
		}

		private static List<DepartmentCallPriority> Active(IEnumerable<DepartmentCallPriority> priorities) =>
			(priorities ?? Enumerable.Empty<DepartmentCallPriority>()).Where(x => x != null && !x.IsDeleted).ToList();

		private static HashSet<string> Words(string text) =>
			String.IsNullOrWhiteSpace(text)
				? new HashSet<string>()
				: new HashSet<string>(Word.Matches(text).Select(m => m.Value.ToLowerInvariant()));
	}
}
