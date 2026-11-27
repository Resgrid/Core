using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Resgrid.Framework;

namespace Resgrid.Model
{
	/// <summary>
	/// The block document a protocol version stores in ContentJson (R-S1). P1 has one block type, <c>html</c>; P3 adds more
	/// (step lists, medication tables, decision references) without a migration, because readers render blocks they know and
	/// keep the rest. Shape (schema 1):
	/// <code>
	/// { "schema": 1,
	///   "blocks":  [ { "type": "html", "key": "b1", "html": "&lt;h2&gt;Scene safety&lt;/h2&gt;&lt;p&gt;…&lt;/p&gt;" } ],
	///   "anchors": [ { "id": "scene-safety", "text": "Scene safety", "level": 2, "block": "b1" } ] }
	/// </code>
	/// Block HTML is stored sanitized (StringHelpers.SanitizeHtmlForStorage). <see cref="Render"/> joins the blocks into the
	/// version's ContentHtml, the text the content hash covers and the apps show. Anchors are derived from the headings (h1–h4),
	/// unique within the version, and are what review comments and AI citations point at; they are added to the headings at
	/// display time (<see cref="InjectAnchors"/>), so the stored, hashed HTML stays exactly the sanitized text.
	/// </summary>
	public sealed class ProtocolContentDocument
	{
		public const int CurrentSchema = 1;

		/// <summary>
		/// A P3 block document (<see cref="ProtocolBlocks"/>): rendered with an anchor per block. A schema 1 document (P1's single html
		/// block) keeps rendering exactly as before, so versions stored before P3 keep their HTML and their hash.
		/// </summary>
		public const int BlockSchema = 2;

		public const string HtmlBlockType = "html";

		[JsonProperty("schema")]
		public int Schema { get; set; } = CurrentSchema;

		/// <summary>A P3 block document (schema 2), as opposed to P1's html document.</summary>
		[JsonIgnore]
		public bool IsBlockModel => Schema >= BlockSchema;

		[JsonProperty("blocks")]
		public List<ProtocolContentBlock> Blocks { get; set; } = new List<ProtocolContentBlock>();

		[JsonProperty("anchors")]
		public List<ProtocolContentAnchor> Anchors { get; set; } = new List<ProtocolContentAnchor>();

		/// <summary>
		/// P4: the decision graph of a Dispatch-kind version (<see cref="ProtocolDecisionGraph"/>); null for none. Rendered into the version's
		/// HTML after the blocks (a "Case flow" section), so the content hash covers it and changing it needs a new version.
		/// </summary>
		[JsonProperty("graph", NullValueHandling = NullValueHandling.Ignore)]
		public ProtocolDecisionGraph Graph { get; set; }

		/// <summary>The labels the graph is rendered with (content language); set by the caller before <see cref="Render(ProtocolBlockLabels)"/>.</summary>
		[JsonIgnore]
		public ProtocolGraphLabels GraphLabels { get; set; }

		[JsonIgnore]
		public bool HasGraph => Graph != null && !Graph.IsEmpty;

		private static readonly Regex Heading = new Regex(@"<h([1-4])(\s[^>]*)?>([\s\S]*?)</h\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(2));
		private static readonly Regex NonSlug = new Regex(@"[^a-z0-9]+", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

		/// <summary>A one-block document from (already sanitized or not) HTML; the HTML is sanitized for storage here.</summary>
		public static ProtocolContentDocument FromHtml(string html)
		{
			var sanitized = StringHelpers.SanitizeHtmlForStorage(html ?? string.Empty) ?? string.Empty;
			var document = new ProtocolContentDocument();
			document.Blocks.Add(new ProtocolContentBlock { Type = HtmlBlockType, Key = "b1", Html = sanitized });
			document.Anchors = Outline(sanitized, "b1");
			return document;
		}

		/// <summary>The document a ContentJson holds; a version stored without JSON (or with unreadable JSON) is its HTML.</summary>
		public static ProtocolContentDocument Parse(string contentJson, string fallbackHtml = null)
		{
			if (!string.IsNullOrWhiteSpace(contentJson))
			{
				try
				{
					var document = JsonConvert.DeserializeObject<ProtocolContentDocument>(contentJson);
					if (document?.Blocks != null)
					{
						document.Anchors ??= new List<ProtocolContentAnchor>();
						return document;
					}
				}
				catch (JsonException)
				{
					// Fall through to the stored HTML.
				}
			}

			return FromStoredHtml(fallbackHtml);
		}

		/// <summary>A one-block document over HTML that is already stored (no re-sanitizing: the hash must stay that of the stored text).</summary>
		public static ProtocolContentDocument FromStoredHtml(string storedHtml)
		{
			var document = new ProtocolContentDocument();
			document.Blocks.Add(new ProtocolContentBlock { Type = HtmlBlockType, Key = "b1", Html = storedHtml ?? string.Empty });
			document.Anchors = Outline(storedHtml, "b1");
			return document;
		}

		public string ToJson() => JsonConvert.SerializeObject(this, Formatting.None);

		/// <summary>The version's HTML: the blocks this reader knows, in order. Unknown block types render nothing here.</summary>
		public string Render() => Render(null);

		/// <summary>
		/// The version's HTML. A block document (P3) is rendered by <see cref="ProtocolBlocks.Render"/> with the labels of the content
		/// language; a P1 html document is its html blocks joined, unchanged.
		/// </summary>
		public string Render(ProtocolBlockLabels labels)
		{
			if (IsBlockModel)
				return ProtocolBlocks.Render(this, labels);

			var html = new StringBuilder();
			foreach (var block in Blocks ?? Enumerable.Empty<ProtocolContentBlock>())
			{
				if (block == null || !string.Equals(block.Type, HtmlBlockType, StringComparison.OrdinalIgnoreCase))
					continue;
				html.Append(block.Html ?? string.Empty);
			}

			// P4: the case flow follows the text (encoded by the renderer; nothing is appended without a graph, so older hashes hold).
			if (HasGraph)
				html.Append(ProtocolDecisionGraphs.RenderHtml(Graph, GraphLabels));

			return html.ToString();
		}

		/// <summary>The headings (h1–h4) of the HTML as unique anchors, in order.</summary>
		public static List<ProtocolContentAnchor> Outline(string html, string blockKey = null)
		{
			var anchors = new List<ProtocolContentAnchor>();
			if (string.IsNullOrWhiteSpace(html))
				return anchors;

			var used = new HashSet<string>(StringComparer.Ordinal);
			try
			{
				foreach (Match match in Heading.Matches(html))
				{
					var text = WebUtility.HtmlDecode(StringHelpers.HtmlToPlainText(match.Groups[3].Value) ?? string.Empty).Trim();
					if (text.Length == 0)
						continue;

					anchors.Add(new ProtocolContentAnchor
					{
						Id = UniqueSlug(text, used),
						Text = text.Length > 200 ? text.Substring(0, 200) : text,
						Level = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
						Block = blockKey
					});
				}
			}
			catch (RegexMatchTimeoutException)
			{
				// No outline rather than a failed page.
			}

			return anchors;
		}

		/// <summary>
		/// The HTML with an id on each heading (the same ids <see cref="Outline"/> returns), for display after sanitizing. Ids are
		/// built from letters, digits and dashes only, so they can never break out of the attribute.
		/// </summary>
		public static string InjectAnchors(string sanitizedHtml)
		{
			if (string.IsNullOrWhiteSpace(sanitizedHtml))
				return sanitizedHtml ?? string.Empty;

			var used = new HashSet<string>(StringComparer.Ordinal);
			try
			{
				return Heading.Replace(sanitizedHtml, match =>
				{
					var text = WebUtility.HtmlDecode(StringHelpers.HtmlToPlainText(match.Groups[3].Value) ?? string.Empty).Trim();
					if (text.Length == 0)
						return match.Value;
					var id = UniqueSlug(text, used);
					return $"<h{match.Groups[1].Value} id=\"{id}\" tabindex=\"-1\">{match.Groups[3].Value}</h{match.Groups[1].Value}>";
				});
			}
			catch (RegexMatchTimeoutException)
			{
				return sanitizedHtml;
			}
		}

		/// <summary>
		/// Stored protocol text as it is written into a page, an export or a PDF: a block document (P3) through the structured allow-list,
		/// which keeps its block anchors and tables; anything else through the display sanitizer with heading anchors injected (P1).
		/// </summary>
		public static string DisplayHtml(string storedHtml)
		{
			if (string.IsNullOrWhiteSpace(storedHtml))
				return string.Empty;
			if (StructuredHtml.IsStructured(storedHtml))
				return StructuredHtml.Sanitize(storedHtml);
			var clean = StringHelpers.SanitizeHtmlForDisplay(storedHtml);
			return clean.Length == 0 ? string.Empty : InjectAnchors(clean);
		}

		/// <summary>Stored protocol text sanitized for output without heading anchors (JSON for apps; v4 keeps its P1 shape).</summary>
		public static string SanitizeForOutput(string storedHtml)
		{
			if (string.IsNullOrWhiteSpace(storedHtml))
				return string.Empty;
			return StructuredHtml.IsStructured(storedHtml) ? StructuredHtml.Sanitize(storedHtml) : StringHelpers.SanitizeHtmlForDisplay(storedHtml);
		}

		/// <summary>
		/// The outline (TOC, review comment anchors, citation targets) of a version: a block document's heading blocks (ids b-{key}),
		/// or the headings of P1 html (ids p-{slug}, as <see cref="InjectAnchors"/> writes them).
		/// </summary>
		public static List<ProtocolContentAnchor> OutlineFor(string contentJson, string contentHtml)
		{
			if (!string.IsNullOrWhiteSpace(contentJson))
			{
				var document = Parse(contentJson, contentHtml);
				if (document.IsBlockModel)
					return ProtocolBlocks.BlockOutline(document.Blocks);
			}

			return StructuredHtml.IsStructured(contentHtml) ? new List<ProtocolContentAnchor>() : Outline(contentHtml);
		}

		private const string DosingSlot = "<span class=\"rgp-dosing-slot\"></span>";

		/// <summary>
		/// Fills each medication block's dosing-tool slot (display time only, never stored) with the department's dosing-tool link (K1
		/// setting 139), or leaves it empty when the department has none. The address must be https; the label is encoded here.
		/// </summary>
		public static string InjectDosingLinks(string displayHtml, string dosingToolUrl, string label)
		{
			if (string.IsNullOrEmpty(displayHtml) || displayHtml.IndexOf(DosingSlot, StringComparison.Ordinal) < 0)
				return displayHtml ?? string.Empty;

			var url = ProtocolBlocks.SafeUrl(dosingToolUrl);
			if (url == null || !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
				return displayHtml;

			var link = "<p class=\"rgp-dosing-tool\"><a href=\"" + WebUtility.HtmlEncode(url) + "\" target=\"_blank\" rel=\"noopener noreferrer nofollow\">" +
				WebUtility.HtmlEncode(label ?? "Dosing tool") + "</a></p>";
			return displayHtml.Replace(DosingSlot, link);
		}

		/// <summary>A copy of this P1 html document as blocks ("Convert to blocks"); a block document is returned normalized.</summary>
		public ProtocolContentDocument ConvertToBlocks()
		{
			if (IsBlockModel)
				return ProtocolBlocks.Normalize(this);

			// The html blocks alone: the P4 case flow stays a graph, never becomes text blocks.
			var converted = ProtocolBlocks.ConvertFromHtml(new ProtocolContentDocument { Schema = Schema, Blocks = Blocks, Anchors = Anchors }.Render());
			converted.Graph = Graph;
			converted.GraphLabels = GraphLabels;
			return converted;
		}

		public static string Slug(string text)
		{
			var lowered = (text ?? string.Empty).Normalize(NormalizationForm.FormD);
			var ascii = new StringBuilder(lowered.Length);
			foreach (var c in lowered)
			{
				if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
					continue;
				ascii.Append(char.ToLowerInvariant(c));
			}

			var slug = NonSlug.Replace(ascii.ToString(), "-").Trim('-');
			if (slug.Length > 60)
				slug = slug.Substring(0, 60).Trim('-');
			return slug.Length == 0 ? "section" : slug;
		}

		private static string UniqueSlug(string text, HashSet<string> used)
		{
			var slug = "p-" + Slug(text);
			var candidate = slug;
			for (var i = 2; !used.Add(candidate); i++)
				candidate = slug + "-" + i.ToString(CultureInfo.InvariantCulture);
			return candidate;
		}
	}

	public sealed class ProtocolContentBlock
	{
		[JsonProperty("type")]
		public string Type { get; set; }

		[JsonProperty("key")]
		public string Key { get; set; }

		[JsonProperty("html", NullValueHandling = NullValueHandling.Ignore)]
		public string Html { get; set; }

		// ---- P3 block fields (each block type uses some; see ProtocolBlocks) ------------------------------------------------------

		/// <summary>Plain text: a heading's text.</summary>
		[JsonProperty("text", NullValueHandling = NullValueHandling.Ignore)]
		public string Text { get; set; }

		/// <summary>A heading's level, 2 to 4.</summary>
		[JsonProperty("level", NullValueHandling = NullValueHandling.Ignore)]
		public int? Level { get; set; }

		/// <summary>Plain text: a steps title, a caution title, a table caption or an image caption.</summary>
		[JsonProperty("title", NullValueHandling = NullValueHandling.Ignore)]
		public string Title { get; set; }

		[JsonProperty("items", NullValueHandling = NullValueHandling.Ignore)]
		public List<ProtocolStepItem> Items { get; set; }

		[JsonProperty("rows", NullValueHandling = NullValueHandling.Ignore)]
		public List<List<string>> Rows { get; set; }

		[JsonProperty("headerRow", NullValueHandling = NullValueHandling.Ignore)]
		public bool? HeaderRow { get; set; }

		/// <summary>An image, video or web link address (http/https only).</summary>
		[JsonProperty("url", NullValueHandling = NullValueHandling.Ignore)]
		public string Url { get; set; }

		/// <summary>A protocol attachment (DispatchProtocolAttachments) an image or attachment block shows.</summary>
		[JsonProperty("attachmentId", NullValueHandling = NullValueHandling.Ignore)]
		public int? AttachmentId { get; set; }

		[JsonProperty("alt", NullValueHandling = NullValueHandling.Ignore)]
		public string Alt { get; set; }

		/// <summary><see cref="ProtocolLinkKinds"/>.</summary>
		[JsonProperty("linkKind", NullValueHandling = NullValueHandling.Ignore)]
		public string LinkKind { get; set; }

		/// <summary>The linked item's id (a protocol, POI or training number; an inventory item or checklist id).</summary>
		[JsonProperty("refId", NullValueHandling = NullValueHandling.Ignore)]
		public string RefId { get; set; }

		[JsonProperty("label", NullValueHandling = NullValueHandling.Ignore)]
		public string Label { get; set; }

		/// <summary>A "contact medical control" gate's who and how (a channel, a phone number).</summary>
		[JsonProperty("contact", NullValueHandling = NullValueHandling.Ignore)]
		public string Contact { get; set; }

		[JsonProperty("medication", NullValueHandling = NullValueHandling.Ignore)]
		public ProtocolMedication Medication { get; set; }

		[JsonProperty("variants", NullValueHandling = NullValueHandling.Ignore)]
		public List<ProtocolVariant> Variants { get; set; }

		/// <summary>Scope-of-practice tags (department certification types) shown on the block.</summary>
		[JsonProperty("scopes", NullValueHandling = NullValueHandling.Ignore)]
		public List<ProtocolScopeTag> Scopes { get; set; }
	}

	public sealed class ProtocolContentAnchor
	{
		[JsonProperty("id")]
		public string Id { get; set; }

		[JsonProperty("text")]
		public string Text { get; set; }

		[JsonProperty("level")]
		public int Level { get; set; }

		[JsonProperty("block", NullValueHandling = NullValueHandling.Ignore)]
		public string Block { get; set; }
	}
}
