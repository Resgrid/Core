using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Resgrid.Framework;

namespace Resgrid.Web.Helpers
{
	/// <summary>
	/// Parsing for the pipe delimited DispatchList string clients send when creating or editing a call.
	/// </summary>
	/// <remarks>
	/// "0" means everyone ("All Call"), and it is the only way to ask for everyone. On a new call a blank list, or an
	/// explicit list with no entries ("|"), names nobody (a run card set to auto-dispatch still adds its resources). On an
	/// edit a blank list keeps the recipients, while "|" removes them, except those of a recipient list the department
	/// hides. (Until 2026-11-13 a blank list paged everyone on an immediate call, because older clients sent nothing when
	/// nothing was picked.) Anything else is the explicit list. A new immediate call made with a system API key never
	/// pages everyone on its own: "0" dispatches nobody there unless the call is pending (CallsController.SaveCall).
	/// </remarks>
	public static class DispatchListHelper
	{
		/// <summary>True for "0", the explicit "everyone" (All Call). Blank is not everyone.</summary>
		/// <param name="dispatchList">The DispatchList string as the client sent it.</param>
		/// <returns>True when the list asks for everyone.</returns>
		public static bool IsEveryone(string dispatchList) =>
			dispatchList != null && dispatchList.Trim() == "0";

		/// <summary>
		/// Whether the list names anyone: "0" or at least one non-blank entry. Blank and an entry-less list ("|") do not,
		/// so a department that requires a dispatch list refuses a call sent to nobody.
		/// </summary>
		/// <param name="dispatchList">The DispatchList string as the client sent it.</param>
		/// <returns>True when the list has an entry.</returns>
		public static bool HasEntries(string dispatchList)
		{
			if (string.IsNullOrWhiteSpace(dispatchList))
				return false;

			return dispatchList.Split('|').Any(entry => !string.IsNullOrWhiteSpace(entry));
		}

		/// <summary>
		/// Pulls the ids out of a DispatchList for a single prefix ("G:", "R:" or "U:"). Clients are supposed
		/// to send ids, but some send the display name instead (i.e. "R:PARAMÉDICO"), and one unparsable entry
		/// used to throw out of the int.Parse projection and silently drop every id sharing that prefix. Each
		/// entry is parsed on its own now and falls back to a name lookup before it's discarded.
		/// </summary>
		/// <param name="dispatchList">The already split DispatchList entries.</param>
		/// <param name="prefix">The entry prefix to collect, i.e. "G:", "R:" or "U:".</param>
		/// <param name="resolveByName">Looks up an id for an entry that isn't numeric, null when there's no match.</param>
		/// <returns>The distinct ids found for that prefix, in the order they appeared.</returns>
		public static List<int> ResolveIds(IEnumerable<string> dispatchList, string prefix, Func<string, int?> resolveByName)
		{
			var ids = new List<int>();

			if (dispatchList == null)
				return ids;

			foreach (var entry in dispatchList.Where(x => !string.IsNullOrWhiteSpace(x) && x.StartsWith(prefix)))
			{
				var value = entry.Substring(prefix.Length).Trim();

				if (string.IsNullOrWhiteSpace(value))
					continue;

				if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
				{
					var resolved = resolveByName?.Invoke(value);

					if (!resolved.HasValue)
					{
						Logging.LogWarning($"Discarding dispatch list entry '{entry}', it's neither an id nor a known name.");
						continue;
					}

					id = resolved.Value;
				}

				if (!ids.Contains(id))
					ids.Add(id);
			}

			return ids;
		}
	}
}
