using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Resgrid.Model.Search
{
	/// <summary>
	/// Plain-text helpers shared by the unified endpoint and the search page: which words of a query to look for in a hit's
	/// text, and the excerpt around the first one. Matching is case-insensitive and literal; nothing here is HTML.
	/// </summary>
	public static class SearchSnippets
	{
		private static readonly Regex QueryParts = new Regex("\"([^\"]*)\"|(\\S+)", RegexOptions.Compiled, TimeSpan.FromSeconds(1));
		private static readonly Regex Whitespace = new Regex("\\s+", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

		/// <summary>
		/// The quoted phrases and the single words of a query, longest first so a phrase wins over its own words. Words of one
		/// character and bare punctuation are left out; they would light up half of any text.
		/// </summary>
		public static List<string> Terms(string query)
		{
			var terms = new List<string>();
			if (string.IsNullOrWhiteSpace(query))
				return terms;

			foreach (Match match in QueryParts.Matches(query))
			{
				var phrase = match.Groups[1].Success ? Clean(match.Groups[1].Value) : null;
				if (!string.IsNullOrEmpty(phrase))
				{
					terms.Add(phrase);
					terms.AddRange(phrase.Split(' ').Select(Clean));
					continue;
				}

				terms.Add(Clean(match.Groups[2].Value));
			}

			return terms
				.Where(t => !string.IsNullOrEmpty(t) && t.Length > 1 && t.Any(char.IsLetterOrDigit))
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.OrderByDescending(t => t.Length)
				.Take(16)
				.ToList();
		}

		/// <summary>
		/// Up to <paramref name="maxLength"/> characters of <paramref name="text"/> around the first occurrence of the first
		/// term (in the given order, so a phrase from <see cref="Terms"/> wins over its words) that occurs, with an ellipsis
		/// where it was cut. Null when the text is empty or none of the terms occur in it.
		/// </summary>
		public static string Build(string text, IReadOnlyList<string> terms, int maxLength = 240)
		{
			if (string.IsNullOrWhiteSpace(text) || terms == null || terms.Count == 0)
				return null;

			var flat = Whitespace.Replace(text, " ").Trim();
			var index = -1;
			var length = 0;
			foreach (var term in terms)
			{
				if (string.IsNullOrEmpty(term))
					continue;
				var at = flat.IndexOf(term, StringComparison.OrdinalIgnoreCase);
				if (at >= 0)
				{
					index = at;
					length = term.Length;
					break;
				}
			}

			if (index < 0)
				return null;

			maxLength = Math.Max(40, maxLength);
			if (flat.Length <= maxLength)
				return flat;

			var start = Math.Max(0, index - (maxLength - length) / 3);
			if (start + maxLength > flat.Length)
				start = Math.Max(0, flat.Length - maxLength);
			// Start and end on a word boundary when one is close.
			if (start > 0)
			{
				var space = flat.IndexOf(' ', start);
				if (space >= 0 && space < index && space - start < 20)
					start = space + 1;
			}
			var end = Math.Min(flat.Length, start + maxLength);
			if (end < flat.Length)
			{
				var space = flat.LastIndexOf(' ', end - 1);
				if (space > index + length && end - space < 20)
					end = space;
			}

			return (start > 0 ? "…" : string.Empty) + flat.Substring(start, end - start).Trim() + (end < flat.Length ? "…" : string.Empty);
		}

		private static string Clean(string value) => Whitespace.Replace((value ?? string.Empty).Trim().Trim(',', ';', '.', ':', '!', '?', '(', ')', '[', ']'), " ");
	}
}
