using System;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;

namespace Resgrid.Framework
{
	/// <summary>
	/// The baseline embed allow-list for rich training text (T0 item 6, Vector parity): YouTube, Vimeo, Google Docs / Sheets /
	/// Slides / Forms and Microsoft Stream / Teams (SharePoint-hosted Stream included). Every other frame is removed by the
	/// sanitizer as before. An allowed frame is rebuilt from its normalized https URL with fixed, safe attributes, so nothing
	/// the author typed besides the address reaches the page.
	/// </summary>
	public static class EmbedAllowList
	{
		private static readonly Regex IframeElement = new Regex(@"<iframe\b[^>]*>(?:.*?</iframe\s*>)?", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

		private static readonly Regex SrcAttribute = new Regex(@"\bsrc\s*=\s*(?:""(?<v>[^""]*)""|'(?<v>[^']*)'|(?<v>[^\s>]+))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

		private static readonly Regex VideoId = new Regex(@"^[A-Za-z0-9_-]{6,20}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

		private static readonly Regex Digits = new Regex(@"^[0-9]{1,15}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

		/// <summary>
		/// The normalized https address of an allowed embed, or null. Watch / share links of YouTube and Vimeo become their
		/// player addresses.
		/// </summary>
		public static string NormalizeEmbedUrl(string url)
		{
			if (string.IsNullOrWhiteSpace(url))
				return null;

			var decoded = WebUtility.HtmlDecode(url.Trim());
			if (decoded.StartsWith("//", StringComparison.Ordinal))
				decoded = "https:" + decoded;

			if (!Uri.TryCreate(decoded, UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.UserInfo) || !uri.IsDefaultPort)
				return null;

			if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
				return null;

			var host = uri.Host.ToLowerInvariant();
			var path = uri.AbsolutePath;

			switch (host)
			{
				case "www.youtube.com":
				case "youtube.com":
				case "m.youtube.com":
				case "www.youtube-nocookie.com":
				case "youtube-nocookie.com":
					if (path.StartsWith("/embed/", StringComparison.Ordinal))
						return YouTube(path.Substring("/embed/".Length));
					if (path == "/watch")
						return YouTube(Query(uri, "v"));
					if (path.StartsWith("/shorts/", StringComparison.Ordinal))
						return YouTube(path.Substring("/shorts/".Length));
					return null;
				case "youtu.be":
					return YouTube(path.TrimStart('/'));
				case "player.vimeo.com":
					return path.StartsWith("/video/", StringComparison.Ordinal) ? Vimeo(path.Substring("/video/".Length)) : null;
				case "vimeo.com":
				case "www.vimeo.com":
					return Vimeo(path.TrimStart('/'));
				case "docs.google.com":
					return path.StartsWith("/document/", StringComparison.Ordinal) || path.StartsWith("/spreadsheets/", StringComparison.Ordinal) ||
						path.StartsWith("/presentation/", StringComparison.Ordinal) || path.StartsWith("/forms/", StringComparison.Ordinal)
						? Https(uri) : null;
				case "web.microsoftstream.com":
					return path.StartsWith("/embed/", StringComparison.Ordinal) ? Https(uri) : null;
				case "teams.microsoft.com":
					return Https(uri);
			}

			// Stream on SharePoint: https://{tenant}.sharepoint.com/.../_layouts/15/embed.aspx?...
			if (host.EndsWith(".sharepoint.com", StringComparison.Ordinal) && path.EndsWith("/_layouts/15/embed.aspx", StringComparison.OrdinalIgnoreCase))
				return Https(uri);

			return null;
		}

		/// <summary>True when the address is an allowed embed.</summary>
		public static bool IsAllowed(string url) => NormalizeEmbedUrl(url) != null;

		/// <summary>The one markup an allowed embed is rendered with.</summary>
		public static string IframeMarkup(string normalizedUrl)
		{
			return "<iframe class=\"ql-video rg-embed\" src=\"" + WebUtility.HtmlEncode(normalizedUrl) + "\" title=\"Embedded content\" frameborder=\"0\" allowfullscreen=\"true\" " +
				"loading=\"lazy\" referrerpolicy=\"strict-origin-when-cross-origin\" sandbox=\"allow-scripts allow-same-origin allow-presentation allow-popups\"></iframe>";
		}

		/// <summary>
		/// Takes every frame out of <paramref name="html"/>: allowed ones become a token that plain-text sanitizing leaves alone
		/// (restored by <see cref="RestoreEmbeds"/>), any other frame is dropped.
		/// </summary>
		public static string ExtractEmbeds(string html, out List<string> embeds, out string token)
		{
			var found = new List<string>();
			var nonce = Guid.NewGuid().ToString("N");
			token = "rgembed" + nonce;
			var prefix = token;

			if (string.IsNullOrEmpty(html) || html.IndexOf("<iframe", StringComparison.OrdinalIgnoreCase) < 0)
			{
				embeds = found;
				return html;
			}

			var result = IframeElement.Replace(html, m =>
			{
				var src = SrcAttribute.Match(m.Value);
				var normalized = src.Success ? NormalizeEmbedUrl(src.Groups["v"].Value) : null;
				if (normalized == null)
					return string.Empty;

				found.Add(normalized);
				return " " + prefix + "x" + (found.Count - 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + "x ";
			});

			embeds = found;
			return result;
		}

		/// <summary>Puts the allowed frames back after sanitizing.</summary>
		public static string RestoreEmbeds(string html, List<string> embeds, string token)
		{
			if (string.IsNullOrEmpty(html) || embeds == null || embeds.Count == 0)
				return html;

			for (var i = 0; i < embeds.Count; i++)
				html = html.Replace(" " + token + "x" + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + "x ", IframeMarkup(embeds[i]))
					.Replace(token + "x" + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + "x", IframeMarkup(embeds[i]));

			return html;
		}

		private static string YouTube(string id)
		{
			id = (id ?? string.Empty).Split('/', '?', '&')[0];
			return VideoId.IsMatch(id) ? "https://www.youtube-nocookie.com/embed/" + id : null;
		}

		private static string Vimeo(string id)
		{
			id = (id ?? string.Empty).Split('/', '?', '&')[0];
			return Digits.IsMatch(id) ? "https://player.vimeo.com/video/" + id : null;
		}

		private static string Https(Uri uri)
		{
			var builder = new UriBuilder(uri) { Scheme = Uri.UriSchemeHttps, Port = -1 };
			return builder.Uri.AbsoluteUri;
		}

		private static string Query(Uri uri, string name)
		{
			foreach (var part in uri.Query.TrimStart('?').Split('&'))
			{
				var pair = part.Split(new[] { '=' }, 2);
				if (pair.Length == 2 && pair[0] == name)
					return Uri.UnescapeDataString(pair[1]);
			}

			return null;
		}
	}
}
