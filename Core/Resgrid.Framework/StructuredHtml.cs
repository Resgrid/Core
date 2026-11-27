using System;
using System.Linq;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Vereyon.Web;

namespace Resgrid.Framework
{
	/// <summary>
	/// The allow-list for structured protocol content (Protocols P3): the HTML the block renderer writes, a document that starts with
	/// <see cref="DocumentMarker"/>. It is wider than <see cref="StringHelpers.SanitizeHtmlForDisplay"/> in exactly what the renderer
	/// needs (tables, the block wrappers, an <c>id</c> that is a block anchor and a <c>class</c> made only of <c>rgp-</c> tokens) and
	/// keeps every other rule: no script, style, frame, form or event handler, and links and images only with http(s), mailto, tel or
	/// relative addresses. Used when a block version is stored (so the hashed text is the sanitized text) and again whenever it is shown
	/// (the save path is never trusted, stored-html-rendering).
	/// </summary>
	public static class StructuredHtml
	{
		/// <summary>The first element of every rendered block document.</summary>
		public const string DocumentMarker = "<div class=\"rgp-doc\">";

		private static readonly Regex AnchorId = new Regex(@"^b-[a-z0-9][a-z0-9-]{0,99}$", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
		private static readonly Regex ClassToken = new Regex(@"^rgp-[a-z0-9-]{1,40}$", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
		private static readonly Regex NonAsciiEntity = new Regex(@"&(?:#[0-9]{1,7}|#[xX][0-9a-fA-F]{1,6}|[a-zA-Z][a-zA-Z0-9]{1,31});",
			RegexOptions.Compiled, TimeSpan.FromSeconds(1));

		/// <summary>Every class the block renderer writes (Resgrid.Model.ProtocolBlocks); any other class token is dropped.</summary>
		public static readonly string[] ClassTokens =
		{
			"rgp-doc", "rgp-heading", "rgp-block", "rgp-html", "rgp-paragraph", "rgp-steps", "rgp-caution", "rgp-warning", "rgp-gate", "rgp-table",
			"rgp-image", "rgp-video", "rgp-attachment", "rgp-link", "rgp-medication", "rgp-variants", "rgp-step-list", "rgp-step", "rgp-title",
			"rgp-label", "rgp-contact", "rgp-grid", "rgp-scope", "rgp-scope-label", "rgp-chip", "rgp-caption", "rgp-video-link",
			"rgp-attachment-link", "rgp-link-protocol", "rgp-link-poi", "rgp-link-inventory", "rgp-link-checklist", "rgp-link-training",
			"rgp-link-url", "rgp-med-table", "rgp-med-inventory", "rgp-med-notes", "rgp-med-notice", "rgp-dosing-slot", "rgp-variant",
			"rgp-variant-label", "rgp-graph", "rgp-graph-steps", "rgp-graph-step", "rgp-graph-question", "rgp-graph-instruction", "rgp-graph-outcome",
			"rgp-graph-answers"
		};

		private static readonly string[] BlockTags ={ "div", "p", "h1", "h2", "h3", "h4", "h5", "h6", "ol", "ul", "li", "table", "thead", "tbody", "tr", "th", "td", "caption", "span", "blockquote" };
		private static readonly string[] InlineTags = { "strong", "em", "i", "u", "s", "sub", "sup", "code", "pre", "br" };

		/// <summary>True when the stored text is a rendered block document (its first element is the document marker).</summary>
		public static bool IsStructured(string html) =>
			!string.IsNullOrEmpty(html) && html.TrimStart().StartsWith(DocumentMarker, StringComparison.Ordinal);

		/// <summary>
		/// The structured document made safe to write into a page: unknown tags go with their content, unknown attributes go, an id that
		/// is not a block anchor and a class token outside <c>rgp-</c> go. Empty input gives an empty string. Fails closed: when the
		/// sanitizer throws, the text is returned encoded.
		/// </summary>
		public static string Sanitize(string html)
		{
			if (string.IsNullOrWhiteSpace(html))
				return string.Empty;

			try
			{
				var sanitizer = new HtmlSanitizer();
				// The sanitizer filters class tokens against its own allow-list: only the renderer's rgp-* tokens are kept.
				sanitizer.AllowCss(ClassTokens);
				var ids =new PatternAttributeSanitizer(value => AnchorId.IsMatch(value));
				var classes = new PatternAttributeSanitizer(value =>
				{
					var tokens = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
					return tokens.Length > 0 && tokens.Length <= 6 && tokens.All(t => ClassToken.IsMatch(t));
				});
				var roles = new PatternAttributeSanitizer(value => value == "note" || value == "group");
				var scopes = new PatternAttributeSanitizer(value => value == "row" || value == "col");

				foreach (var tag in BlockTags)
				{
					var rule = sanitizer.Tag(tag);
					rule.SanitizeAttributes("id", ids);
					rule.SanitizeAttributes("class", classes);
					if (tag == "div")
						rule.SanitizeAttributes("role", roles);
					else if (tag == "th")
						rule.SanitizeAttributes("scope", scopes);
				}

				foreach (var tag in InlineTags)
					sanitizer.Tag(tag);

				sanitizer.Tag("img").CheckAttribute("src", HtmlSanitizerCheckType.Url).AllowAttributes("alt").RemoveEmpty();
				sanitizer.Tag("a").SetAttribute("rel", "nofollow").CheckAttribute("href", HtmlSanitizerCheckType.Url)
					.SanitizeAttributes("class", classes).RemoveEmpty();

				var clean = sanitizer.Sanitize(html) ?? string.Empty;
				// The sanitizer writes non-ASCII characters and the apostrophe as entities; put them back as characters, as
				// StringHelpers does, so exports and voice prompts never show the entity. &amp; &lt; &gt; &quot; stay encoded.
				return NonAsciiEntity.Replace(clean, m =>
				{
					var decoded = System.Net.WebUtility.HtmlDecode(m.Value);
					if (decoded.Length == 0 || decoded == m.Value)
						return m.Value;
					// Only characters above U+00A0 (non-ASCII) and the apostrophe: &lt; &gt; &amp; &quot; and &nbsp; must stay encoded.
					return decoded[0] > (char)0xA0 || decoded == "'" ? decoded : m.Value;
				}).Trim();
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Structured protocol content could not be sanitized; encoding it instead.");
				return System.Net.WebUtility.HtmlEncode(html);
			}
		}

		/// <summary>Keeps an attribute whose whole value passes the check; removes it otherwise.</summary>
		private sealed class PatternAttributeSanitizer : IHtmlAttributeSanitizer
		{
			private readonly Func<string, bool> _allowed;

			public PatternAttributeSanitizer(Func<string, bool> allowed)
			{
				_allowed = allowed;
			}

			public SanitizerOperation SanitizeAttribute(HtmlAttribute attribute, HtmlSanitizerTagRule tagRule)
			{
				var value = attribute?.Value?.Trim();
				if (string.IsNullOrEmpty(value))
					return SanitizerOperation.RemoveAttribute;
				try
				{
					return _allowed(value) ? SanitizerOperation.DoNothing : SanitizerOperation.RemoveAttribute;
				}
				catch (RegexMatchTimeoutException)
				{
					return SanitizerOperation.RemoveAttribute;
				}
			}
		}
	}
}
