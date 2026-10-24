using System;
using System.Collections.Generic;
using System.Text;

namespace Resgrid.Model
{
	/// <summary>
	/// Search keys for numbers a department formats itself (record, call and document numbers): a number with its formatting
	/// set aside, so a search finds it however the number was typed. A number reads as runs of letters and runs of digits; any
	/// other character only separates them, a letter next to a digit starts a new run, letters are lower-cased and leading zeros
	/// dropped. "RUN26-0001", "run-26-1" and "RUN 26 0001" all read "run-26-1", which a word analyzer cannot match: it keeps
	/// "RUN26" whole, and "2026.0001" or "RUN_2026_0001" as one word. A number is indexed under every run of consecutive parts,
	/// so "RUN", "26-1" and "0001" find it as well.
	/// </summary>
	public static class IdentifierSearchKeys
	{
		/// <summary>Longest run of consecutive parts indexed on its own; the whole number is always a key.</summary>
		public const int MaxPartsPerKey = 8;

		/// <summary>The key a search text is looked up by; null when the text has no letters or digits.</summary>
		public static string Canonical(string text)
		{
			var parts = Parts(text);
			return parts.Count == 0 ? null : string.Join("-", parts);
		}

		/// <summary>Every key that finds <paramref name="number"/>: the whole number and each shorter run of consecutive parts.</summary>
		public static IEnumerable<string> For(string number)
		{
			var parts = Parts(number);
			var keys = new HashSet<string>(StringComparer.Ordinal);
			if (parts.Count == 0)
				return keys;

			keys.Add(string.Join("-", parts));
			for (var start = 0; start < parts.Count; start++)
			{
				for (var length = 1; length <= MaxPartsPerKey && start + length <= parts.Count; length++)
					keys.Add(string.Join("-", parts.GetRange(start, length)));
			}

			return keys;
		}

		/// <summary>True when <paramref name="text"/> has an ASCII digit: an identifier rather than a word.</summary>
		public static bool HasDigit(string text)
		{
			if (string.IsNullOrEmpty(text))
				return false;
			foreach (var c in text)
			{
				if (IsDigit(c))
					return true;
			}
			return false;
		}

		/// <summary>Longest list filter search kept: no department number column holds more than 50 characters.</summary>
		public const int MaxFilterLength = 50;

		/// <summary>A list filter's search as the query takes it: trimmed, cut to <see cref="MaxFilterLength"/>, null when it has no letters or digits.</summary>
		public static string FilterSearch(string search)
		{
			if (string.IsNullOrWhiteSpace(search))
				return null;
			search = search.Trim();
			if (search.Length > MaxFilterLength)
				search = search.Substring(0, MaxFilterLength);
			return Parts(search, false).Count == 0 ? null : search;
		}

		/// <summary>
		/// The letter and digit runs of a list filter's search as typed: lower-cased, zero padding kept. Empty when the search has
		/// no letters or digits.
		/// </summary>
		public static IReadOnlyList<string> TypedParts(string search) => Parts(search, false);

		/// <summary>
		/// The list filter match: <paramref name="number"/> holds the search's letter and digit runs in that order, ignoring case.
		/// "wo26-0007", "WO-26-0007", "26 0007" and "0007" all find WO26-0007 whatever separators the department's pattern uses;
		/// "26-7" finds it as well, along with any other number holding a 7 after its 26. A search with no letters or digits finds
		/// nothing. Lists filtered in SQL run the same match as <c>LIKE '%wo%26%0007%'</c>.
		/// </summary>
		public static bool ContainsParts(string number, string search)
		{
			var parts = Parts(search, false);
			if (parts.Count == 0 || string.IsNullOrEmpty(number))
				return false;

			var at = 0;
			foreach (var part in parts)
			{
				var found = number.IndexOf(part, at, StringComparison.OrdinalIgnoreCase);
				if (found < 0)
					return false;
				at = found + part.Length;
			}
			return true;
		}

		private static List<string> Parts(string text, bool dropZeros = true)
		{
			var parts = new List<string>();
			if (string.IsNullOrWhiteSpace(text))
				return parts;

			var part = new StringBuilder();
			var digits = false;
			foreach (var c in text)
			{
				var isDigit = IsDigit(c);
				if (!isDigit && !char.IsLetterOrDigit(c))
				{
					Flush(parts, part, digits, dropZeros);
					continue;
				}
				if (part.Length > 0 && isDigit != digits)
					Flush(parts, part, digits, dropZeros);
				digits = isDigit;
				part.Append(isDigit ? c : char.ToLowerInvariant(c));
			}
			Flush(parts, part, digits, dropZeros);

			return parts;
		}

		private static void Flush(List<string> parts, StringBuilder part, bool digits, bool dropZeros)
		{
			if (part.Length == 0)
				return;

			var text = part.ToString();
			if (digits && dropZeros)
			{
				text = text.TrimStart('0');
				if (text.Length == 0)
					text = "0";
			}
			parts.Add(text);
			part.Clear();
		}

		private static bool IsDigit(char c) => c >= '0' && c <= '9';
	}
}
