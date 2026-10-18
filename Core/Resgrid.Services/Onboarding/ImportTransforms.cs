using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Resgrid.Services.Onboarding
{
	/// <summary>
	/// The fixed transform catalog (wizard plan section 8.3): an allowlist, never an expression language. Each transform
	/// takes the cell text and returns text; date formats, phone regions, boolean tokens and code lookups are handled by the
	/// field type and value maps, not here.
	/// </summary>
	public static class ImportTransforms
	{
		private static readonly Regex Whitespace = new Regex(@"\s+", RegexOptions.CultureInvariant);

		public static readonly IReadOnlyDictionary<string, Func<string, string>> Catalog = new Dictionary<string, Func<string, string>>(StringComparer.OrdinalIgnoreCase)
		{
			["trim"] = v => v?.Trim(),
			["collapse-whitespace"] = v => v == null ? null : Whitespace.Replace(v.Trim(), " "),
			["upper"] = v => v?.ToUpperInvariant(),
			["lower"] = v => v?.ToLowerInvariant(),
			["title"] = v => v == null ? null : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(v.Trim().ToLowerInvariant()),
			["digits-only"] = v => v == null ? null : new string(v.Where(char.IsDigit).ToArray()),
			// "Rivera, Alex" or "Alex Rivera" → first / last name parts (the name-split transform).
			["name-first"] = v => SplitName(v).First,
			["name-last"] = v => SplitName(v).Last,
			["first-word"] = v => v?.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(),
			["last-word"] = v => v?.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()
		};

		public static string Apply(IEnumerable<string> transforms, string value)
		{
			if (transforms == null)
				return value;

			foreach (var key in transforms)
				if (key != null && Catalog.TryGetValue(key, out var transform))
					value = transform(value);

			return value;
		}

		public static bool IsKnown(string key) => key != null && Catalog.ContainsKey(key);

		internal static (string First, string Last) SplitName(string value)
		{
			if (string.IsNullOrWhiteSpace(value))
				return (null, null);

			var text = Whitespace.Replace(value.Trim(), " ");
			var comma = text.IndexOf(',');
			if (comma > 0)
				return (text.Substring(comma + 1).Trim(), text.Substring(0, comma).Trim());

			var space = text.LastIndexOf(' ');
			return space > 0 ? (text.Substring(0, space), text.Substring(space + 1)) : (text, null);
		}
	}
}
