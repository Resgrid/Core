using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Html;

namespace Resgrid.Web.Helpers
{
	/// <summary>
	/// Marks the searched words and phrases in a search result's title or excerpt. Every piece of the text is HTML-encoded;
	/// only the &lt;mark&gt; tags this helper adds are markup.
	/// </summary>
	public static class SearchHighlighter
	{
		public static IHtmlContent Highlight(string text, IReadOnlyList<string> terms)
		{
			if (string.IsNullOrEmpty(text))
				return HtmlString.Empty;

			var encoder = HtmlEncoder.Default;
			var usable = (terms ?? Array.Empty<string>()).Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
			if (usable.Count == 0)
				return new HtmlString(encoder.Encode(text));

			try
			{
				// Longest first, so a quoted phrase is marked whole rather than word by word.
				var pattern = string.Join("|", usable.OrderByDescending(t => t.Length).Select(Regex.Escape));
				var regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
				var html = new StringBuilder();
				var last = 0;
				foreach (Match match in regex.Matches(text))
				{
					if (match.Length == 0)
						continue;
					html.Append(encoder.Encode(text.Substring(last, match.Index - last)));
					html.Append("<mark>").Append(encoder.Encode(match.Value)).Append("</mark>");
					last = match.Index + match.Length;
				}
				html.Append(encoder.Encode(text.Substring(last)));
				return new HtmlString(html.ToString());
			}
			catch (RegexMatchTimeoutException)
			{
				return new HtmlString(encoder.Encode(text));
			}
		}
	}
}
