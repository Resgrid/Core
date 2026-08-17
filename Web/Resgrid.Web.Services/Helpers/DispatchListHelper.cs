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
	public static class DispatchListHelper
	{
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
