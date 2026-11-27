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
	/// The structured block types of a protocol version (P3), stored in ContentJson next to P1's <c>html</c> block. Persisted as the
	/// string: append only, never rename.
	/// </summary>
	public static class ProtocolBlockTypes
	{
		/// <summary>P1's sanitized rich text block (a version stored before P3 is one of these).</summary>
		public const string Html = "html";
		public const string Heading = "heading";
		public const string Paragraph = "paragraph";
		/// <summary>Ordered steps; each item has its own id and is a step-through step.</summary>
		public const string Steps = "steps";
		public const string Caution = "caution";
		public const string Warning = "warning";
		/// <summary>"Contact medical control" before going further; a step-through step of its own.</summary>
		public const string Gate = "gate";
		public const string Table = "table";
		public const string Image = "image";
		public const string Video = "video";
		public const string Attachment = "attachment";
		/// <summary>A link to another protocol, a POI, an inventory item, a checklist, a training or an https page.</summary>
		public const string Link = "link";
		/// <summary>A medication: doses shown exactly as authored, never computed (decision 3).</summary>
		public const string Medication = "medication";
		/// <summary>An adult / pediatric (or other) variant group: each variant holds blocks of its own.</summary>
		public const string Variants = "variants";

		public static readonly IReadOnlyList<string> All = new[]
		{
			Heading, Paragraph, Steps, Caution, Warning, Gate, Table, Image, Video, Attachment, Link, Medication, Variants, Html
		};

		/// <summary>The types a variant may hold (no headings, so the outline stays flat, and no nested variant groups).</summary>
		public static readonly IReadOnlyList<string> InsideVariant = new[]
		{
			Paragraph, Steps, Caution, Warning, Gate, Table, Image, Video, Attachment, Link, Medication
		};

		public static bool IsKnown(string type) => type != null && All.Contains(type, StringComparer.Ordinal);
	}

	/// <summary>What a link block points at. Persisted as the string.</summary>
	public static class ProtocolLinkKinds
	{
		public const string Protocol = "protocol";
		public const string Poi = "poi";
		public const string InventoryItem = "inventory";
		public const string Checklist = "checklist";
		public const string Training = "training";
		public const string Url = "url";

		public static readonly IReadOnlyList<string> All = new[] { Protocol, Poi, InventoryItem, Checklist, Training, Url };

		public static bool IsKnown(string kind) => kind != null && All.Contains(kind, StringComparer.Ordinal);
	}

	/// <summary>One step of a steps block.</summary>
	public sealed class ProtocolStepItem
	{
		[JsonProperty("key")]
		public string Key { get; set; }

		/// <summary>Sanitized rich text.</summary>
		[JsonProperty("html")]
		public string Html { get; set; }
	}

	/// <summary>
	/// A medication as the medical director wrote it (P3 decision 3). Every value is text and is shown exactly as authored: nothing in
	/// Resgrid multiplies, divides or otherwise computes a dose from these fields (guarded by a source-scan test).
	/// </summary>
	public sealed class ProtocolMedication
	{
		public const int FieldMaxLength = 200;

		[JsonProperty("drug", NullValueHandling = NullValueHandling.Ignore)]
		public string Drug { get; set; }

		[JsonProperty("indication", NullValueHandling = NullValueHandling.Ignore)]
		public string Indication { get; set; }

		[JsonProperty("concentration", NullValueHandling = NullValueHandling.Ignore)]
		public string Concentration { get; set; }

		[JsonProperty("dosePerKg", NullValueHandling = NullValueHandling.Ignore)]
		public string DosePerKg { get; set; }

		[JsonProperty("maxSingleDose", NullValueHandling = NullValueHandling.Ignore)]
		public string MaxSingleDose { get; set; }

		[JsonProperty("maxTotalDose", NullValueHandling = NullValueHandling.Ignore)]
		public string MaxTotalDose { get; set; }

		[JsonProperty("route", NullValueHandling = NullValueHandling.Ignore)]
		public string Route { get; set; }

		[JsonProperty("repeatInterval", NullValueHandling = NullValueHandling.Ignore)]
		public string RepeatInterval { get; set; }

		/// <summary>The inventory item (catalog id) the drug is stocked as; a link, never a stock check.</summary>
		[JsonProperty("inventoryItemId", NullValueHandling = NullValueHandling.Ignore)]
		public string InventoryItemId { get; set; }

		[JsonProperty("inventoryItemName", NullValueHandling = NullValueHandling.Ignore)]
		public string InventoryItemName { get; set; }

		/// <summary>Sanitized rich text.</summary>
		[JsonProperty("notes", NullValueHandling = NullValueHandling.Ignore)]
		public string Notes { get; set; }
	}

	/// <summary>One variant of a variant group (Adult, Pediatric...), with blocks of its own.</summary>
	public sealed class ProtocolVariant
	{
		[JsonProperty("key")]
		public string Key { get; set; }

		[JsonProperty("label")]
		public string Label { get; set; }

		[JsonProperty("blocks")]
		public List<ProtocolContentBlock> Blocks { get; set; } = new List<ProtocolContentBlock>();
	}

	/// <summary>A scope-of-practice tag: a department certification type (EMR, EMT, AEMT, Paramedic...), with its name as authored.</summary>
	public sealed class ProtocolScopeTag
	{
		[JsonProperty("id")]
		public int Id { get; set; }

		[JsonProperty("name")]
		public string Name { get; set; }
	}

	/// <summary>
	/// The words the renderer writes around the authored text (the caution label, the medication field names...). They are written into
	/// the version's HTML in the version's language, so the approved, hashed text is exactly what members read. English by default; the
	/// services fill it from the Protocols resources for the content language.
	/// </summary>
	public sealed class ProtocolBlockLabels
	{
		public static ProtocolBlockLabels Default { get; } = new ProtocolBlockLabels();

		public string Caution { get; set; } = "Caution";
		public string Warning { get; set; } = "Warning";
		public string ContactMedicalControl { get; set; } = "Contact medical control";
		public string Contact { get; set; } = "Contact";
		public string ScopeOfPractice { get; set; } = "Scope of practice";
		public string Medication { get; set; } = "Medication";
		public string Indication { get; set; } = "Indication";
		public string Concentration { get; set; } = "Concentration";
		public string DosePerKg { get; set; } = "Dose per kg";
		public string MaxSingleDose { get; set; } = "Maximum single dose";
		public string MaxTotalDose { get; set; } = "Maximum total dose";
		public string Route { get; set; } = "Route of administration";
		public string RepeatInterval { get; set; } = "Repeat interval";
		public string InventoryItem { get; set; } = "Inventory item";
		public string DoseAsAuthored { get; set; } = "Doses are shown as authored. Nothing is calculated.";
		public string Video { get; set; } = "Video link";
		public string Attachment { get; set; } = "Attached file";
		public string LinkProtocol { get; set; } = "Related protocol";
		public string LinkPoi { get; set; } = "Point of interest";
		public string LinkInventory { get; set; } = "Linked inventory item";
		public string LinkChecklist { get; set; } = "Related checklist";
		public string LinkTraining { get; set; } = "Related training";
		public string LinkUrl { get; set; } = "Web page";

		public string ForLinkKind(string kind)
		{
			switch (kind)
			{
				case ProtocolLinkKinds.Protocol: return LinkProtocol;
				case ProtocolLinkKinds.Poi: return LinkPoi;
				case ProtocolLinkKinds.InventoryItem: return LinkInventory;
				case ProtocolLinkKinds.Checklist: return LinkChecklist;
				case ProtocolLinkKinds.Training: return LinkTraining;
				default: return LinkUrl;
			}
		}
	}

	/// <summary>
	/// The P3 block rules: limits, key stability, sanitizing each field, rendering a block document to HTML with an anchor per block
	/// (<c>id="b-{key}"</c>), and converting a P1 html document to blocks when the author asks for it.
	/// </summary>
	public static class ProtocolBlocks
	{
		public const int MaxBlocks = 500;
		public const int MaxStepItems = 100;
		public const int MaxTableRows = 60;
		public const int MaxTableColumns = 10;
		public const int MaxVariants = 4;
		public const int MaxScopes = 10;
		public const int TextMaxLength = 500;
		public const int RichMaxLength = 20000;
		public const int KeyMaxLength = 24;
		public const string AnchorPrefix = "b-";

		private static readonly Regex KeyPattern = new Regex(@"^[a-z0-9]{1," + KeyMaxLength + "}$", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
		private static readonly Regex RefPattern = new Regex(@"^[A-Za-z0-9_-]{1,64}$", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
		private static readonly Regex Heading = new Regex(@"<h([1-4])(\s[^>]*)?>([\s\S]*?)</h\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(2));
		private static readonly Regex OrderedList = new Regex(@"<ol(\s[^>]*)?>([\s\S]*?)</ol\s*>", RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(2));
		private static readonly Regex ListItem = new Regex(@"<li(\s[^>]*)?>([\s\S]*?)</li\s*>", RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(2));

		/// <summary>The anchor id of a block (or a step: <c>{blockKey}-{itemKey}</c>).</summary>
		public static string Anchor(string key) => AnchorPrefix + key;

		public static bool IsValidKey(string key) => !string.IsNullOrEmpty(key) && KeyPattern.IsMatch(key);

		/// <summary>A fresh block key (8 lower-case letters and digits).</summary>
		public static string NewKey() => Guid.NewGuid().ToString("N").Substring(0, 8);

		#region Normalizing

		/// <summary>
		/// The block document as it will be stored: unknown types dropped, limits applied, every rich field sanitized, plain fields trimmed,
		/// keys kept when valid and unique (that is what keeps anchors stable across edits) and issued otherwise, and the heading outline
		/// rebuilt. Marks the document as a block document.
		/// </summary>
		public static ProtocolContentDocument Normalize(ProtocolContentDocument document)
		{
			document ??= new ProtocolContentDocument();
			var used = new HashSet<string>(StringComparer.Ordinal);
			document.Schema = ProtocolContentDocument.BlockSchema;
			document.Blocks = NormalizeList(document.Blocks, used, false);
			document.Anchors = BlockOutline(document.Blocks);
			return document;
		}

		private static List<ProtocolContentBlock> NormalizeList(IEnumerable<ProtocolContentBlock> blocks, HashSet<string> used, bool insideVariant)
		{
			var list = new List<ProtocolContentBlock>();
			foreach (var block in blocks ?? Enumerable.Empty<ProtocolContentBlock>())
			{
				if (block == null || !ProtocolBlockTypes.IsKnown(block.Type))
					continue;
				if (insideVariant && !ProtocolBlockTypes.InsideVariant.Contains(block.Type, StringComparer.Ordinal))
					continue;
				if (list.Count >= MaxBlocks)
					break;

				var clean = NormalizeBlock(block, used);
				if (clean != null)
					list.Add(clean);
			}

			return list;
		}

		private static string TakeKey(string key, HashSet<string> used)
		{
			var candidate = (key ?? string.Empty).Trim().ToLowerInvariant();
			if (!IsValidKey(candidate) || used.Contains(candidate))
			{
				do
				{
					candidate = NewKey();
				} while (!used.Add(candidate));
				return candidate;
			}

			used.Add(candidate);
			return candidate;
		}

		private static ProtocolContentBlock NormalizeBlock(ProtocolContentBlock block, HashSet<string> used)
		{
			var clean = new ProtocolContentBlock { Type = block.Type, Key = TakeKey(block.Key, used), Scopes = NormalizeScopes(block.Scopes) };
			switch (block.Type)
			{
				case ProtocolBlockTypes.Heading:
					clean.Text = Plain(block.Text, 200);
					if (string.IsNullOrEmpty(clean.Text))
						return null;
					clean.Level = Math.Clamp(block.Level ?? 2, 2, 4);
					break;
				case ProtocolBlockTypes.Paragraph:
				case ProtocolBlockTypes.Html:
					clean.Html = Rich(block.Html, RichMaxLength);
					break;
				case ProtocolBlockTypes.Steps:
					clean.Title = Plain(block.Title, 200);
					clean.Items = new List<ProtocolStepItem>();
					foreach (var item in (block.Items ?? new List<ProtocolStepItem>()).Where(i => i != null).Take(MaxStepItems))
					{
						var html = Rich(item.Html, 4000);
						if (string.IsNullOrWhiteSpace(StringHelpers.HtmlToPlainText(html)))
							continue;
						clean.Items.Add(new ProtocolStepItem { Key = TakeKey(item.Key, used), Html = html });
					}
					break;
				case ProtocolBlockTypes.Caution:
				case ProtocolBlockTypes.Warning:
					clean.Title = Plain(block.Title, 200);
					clean.Html = Rich(block.Html, 4000);
					break;
				case ProtocolBlockTypes.Gate:
					clean.Html = Rich(block.Html, 4000);
					clean.Contact = Plain(block.Contact, 200);
					break;
				case ProtocolBlockTypes.Table:
					clean.Title = Plain(block.Title, 200);
					clean.HeaderRow = block.HeaderRow;
					var rows = (block.Rows ?? new List<List<string>>()).Where(r => r != null).Take(MaxTableRows).ToList();
					var columns = Math.Min(MaxTableColumns, rows.Count == 0 ? 0 : rows.Max(r => r.Count));
					clean.Rows = rows.Select(r => Enumerable.Range(0, columns).Select(i => i < r.Count ? Plain(r[i], TextMaxLength) ?? string.Empty : string.Empty).ToList()).ToList();
					break;
				case ProtocolBlockTypes.Image:
					clean.Url = SafeUrl(block.Url);
					clean.AttachmentId = block.AttachmentId > 0 ? block.AttachmentId : null;
					if (clean.Url == null && clean.AttachmentId == null)
						return null;
					clean.Alt = Plain(block.Alt, 300);
					clean.Title = Plain(block.Title, 300);
					break;
				case ProtocolBlockTypes.Video:
					clean.Url = SafeUrl(block.Url);
					if (clean.Url == null)
						return null;
					clean.Label = Plain(block.Label, 200);
					break;
				case ProtocolBlockTypes.Attachment:
					clean.AttachmentId = block.AttachmentId > 0 ? block.AttachmentId : null;
					if (clean.AttachmentId == null)
						return null;
					clean.Label = Plain(block.Label, 200);
					break;
				case ProtocolBlockTypes.Link:
					clean.LinkKind = ProtocolLinkKinds.IsKnown(block.LinkKind) ? block.LinkKind : ProtocolLinkKinds.Url;
					clean.Label = Plain(block.Label, 200);
					if (clean.LinkKind == ProtocolLinkKinds.Url)
					{
						clean.Url = SafeUrl(block.Url);
						if (clean.Url == null)
							return null;
					}
					else
					{
						var reference = (block.RefId ?? string.Empty).Trim();
						if (!RefPattern.IsMatch(reference))
							return null;
						clean.RefId = reference;
					}
					break;
				case ProtocolBlockTypes.Medication:
					var med = block.Medication ?? new ProtocolMedication();
					clean.Medication = new ProtocolMedication
					{
						Drug = Plain(med.Drug, ProtocolMedication.FieldMaxLength),
						Indication = Plain(med.Indication, ProtocolMedication.FieldMaxLength),
						Concentration = Plain(med.Concentration, ProtocolMedication.FieldMaxLength),
						DosePerKg = Plain(med.DosePerKg, ProtocolMedication.FieldMaxLength),
						MaxSingleDose = Plain(med.MaxSingleDose, ProtocolMedication.FieldMaxLength),
						MaxTotalDose = Plain(med.MaxTotalDose, ProtocolMedication.FieldMaxLength),
						Route = Plain(med.Route, ProtocolMedication.FieldMaxLength),
						RepeatInterval = Plain(med.RepeatInterval, ProtocolMedication.FieldMaxLength),
						InventoryItemId = RefPattern.IsMatch((med.InventoryItemId ?? string.Empty).Trim()) ? med.InventoryItemId.Trim() : null,
						InventoryItemName = Plain(med.InventoryItemName, ProtocolMedication.FieldMaxLength),
						Notes = Rich(med.Notes, 4000)
					};
					if (string.IsNullOrEmpty(clean.Medication.Drug))
						return null;
					if (clean.Medication.InventoryItemId == null)
						clean.Medication.InventoryItemName = null;
					break;
				case ProtocolBlockTypes.Variants:
					clean.Variants = new List<ProtocolVariant>();
					foreach (var variant in (block.Variants ?? new List<ProtocolVariant>()).Where(v => v != null).Take(MaxVariants))
					{
						clean.Variants.Add(new ProtocolVariant
						{
							Key = TakeKey(variant.Key, used),
							Label = Plain(variant.Label, 100) ?? string.Empty,
							Blocks = NormalizeList(variant.Blocks, used, true)
						});
					}
					if (clean.Variants.Count == 0)
						return null;
					break;
			}

			return clean;
		}

		private static List<ProtocolScopeTag> NormalizeScopes(IEnumerable<ProtocolScopeTag> scopes)
		{
			var list = (scopes ?? Enumerable.Empty<ProtocolScopeTag>())
				.Where(s => s != null && s.Id > 0)
				.GroupBy(s => s.Id).Select(g => new ProtocolScopeTag { Id = g.Key, Name = Plain(g.First().Name, 100) ?? string.Empty })
				.Take(MaxScopes).ToList();
			return list.Count == 0 ? null : list;
		}

		/// <summary>A plain text field: trimmed, capped, null when blank. Encoded when rendered.</summary>
		public static string Plain(string value, int max)
		{
			if (string.IsNullOrWhiteSpace(value))
				return null;
			var text = value.Trim();
			return text.Length > max ? text.Substring(0, max) : text;
		}

		/// <summary>
		/// A rich text field: sanitized markup (the storage allow-list), and angle brackets in text that holds no markup encoded, so
		/// "pt &lt;unconscious&gt;" stays readable instead of being dropped as an unknown tag.
		/// </summary>
		public static string Rich(string value, int max)
		{
			if (string.IsNullOrWhiteSpace(value))
				return string.Empty;
			var text = value.Trim();
			if (text.Length > max)
				text = text.Substring(0, max);
			if (text.IndexOf('<') < 0)
				return text;
			if (!StringHelpers.LooksLikeHtml(text))
				return text.Replace("<", "&lt;").Replace(">", "&gt;");
			return StringHelpers.SanitizeHtmlForStorage(text) ?? string.Empty;
		}

		/// <summary>An absolute https (or http) address; null for anything else (no script, data or relative address).</summary>
		public static string SafeUrl(string value)
		{
			var text = (value ?? string.Empty).Trim();
			if (text.Length == 0 || text.Length > 1000)
				return null;
			return Uri.TryCreate(text, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
				? uri.AbsoluteUri
				: null;
		}

		/// <summary>The heading blocks as the version's outline (TOC, review comments, AI citations): ids are the block anchors.</summary>
		public static List<ProtocolContentAnchor> BlockOutline(IEnumerable<ProtocolContentBlock> blocks) =>
			(blocks ?? Enumerable.Empty<ProtocolContentBlock>())
				.Where(b => b != null && b.Type == ProtocolBlockTypes.Heading && !string.IsNullOrEmpty(b.Text) && IsValidKey(b.Key))
				.Select(b => new ProtocolContentAnchor { Id = Anchor(b.Key), Text = b.Text, Level = b.Level ?? 2, Block = b.Key })
				.ToList();

		#endregion Normalizing

		#region Rendering

		/// <summary>
		/// The block document as HTML: a <c>div.rgp-doc</c> holding one element per block, each with <c>id="b-{key}"</c> (steps carry
		/// <c>id="b-{block}-{item}"</c>), plain fields encoded, rich fields as sanitized, then the whole passed through
		/// <see cref="StructuredHtml.Sanitize"/>. Deterministic for the same document and labels.
		/// </summary>
		public static string Render(ProtocolContentDocument document, ProtocolBlockLabels labels = null)
		{
			labels ??= ProtocolBlockLabels.Default;
			var html = new StringBuilder(StructuredHtml.DocumentMarker);
			foreach (var block in document?.Blocks ?? new List<ProtocolContentBlock>())
				RenderBlock(html, block, labels);
			// P4: a Dispatch version's case flow, after the blocks (nothing without a graph, so older hashes hold).
			if (document != null && document.HasGraph)
				html.Append(ProtocolDecisionGraphs.RenderHtml(document.Graph, document.GraphLabels));
			html.Append("</div>");
			return StructuredHtml.Sanitize(html.ToString());
		}

		private static string E(string value) => WebUtility.HtmlEncode(value ?? string.Empty);

		private static void RenderScopes(StringBuilder html, ProtocolContentBlock block, ProtocolBlockLabels labels)
		{
			if (block.Scopes == null || block.Scopes.Count == 0)
				return;
			html.Append("<p class=\"rgp-scope\"><span class=\"rgp-scope-label\">").Append(E(labels.ScopeOfPractice)).Append(":</span>");
			foreach (var scope in block.Scopes)
				html.Append(" <span class=\"rgp-chip\">").Append(E(scope.Name)).Append("</span>");
			html.Append("</p>");
		}

		private static void Open(StringBuilder html, ProtocolContentBlock block, string role = null)
		{
			html.Append("<div id=\"").Append(Anchor(block.Key)).Append("\" class=\"rgp-block rgp-").Append(block.Type).Append('"');
			if (role != null)
				html.Append(" role=\"").Append(role).Append('"');
			html.Append('>');
		}

		private static void RenderBlock(StringBuilder html, ProtocolContentBlock block, ProtocolBlockLabels labels)
		{
			if (block == null || !IsValidKey(block.Key))
				return;

			switch (block.Type)
			{
				case ProtocolBlockTypes.Heading:
					var level = Math.Clamp(block.Level ?? 2, 2, 4).ToString(CultureInfo.InvariantCulture);
					html.Append("<h").Append(level).Append(" id=\"").Append(Anchor(block.Key)).Append("\" class=\"rgp-heading\">")
						.Append(E(block.Text)).Append("</h").Append(level).Append('>');
					RenderScopes(html, block, labels);
					return;
				case ProtocolBlockTypes.Paragraph:
				case ProtocolBlockTypes.Html:
					Open(html, block);
					RenderScopes(html, block, labels);
					html.Append(block.Html ?? string.Empty);
					break;
				case ProtocolBlockTypes.Steps:
					Open(html, block);
					RenderScopes(html, block, labels);
					if (!string.IsNullOrEmpty(block.Title))
						html.Append("<p class=\"rgp-title\"><strong>").Append(E(block.Title)).Append("</strong></p>");
					html.Append("<ol class=\"rgp-step-list\">");
					foreach (var item in block.Items ?? new List<ProtocolStepItem>())
					{
						if (item == null || !IsValidKey(item.Key))
							continue;
						html.Append("<li id=\"").Append(Anchor(block.Key + "-" + item.Key)).Append("\" class=\"rgp-step\">").Append(item.Html ?? string.Empty).Append("</li>");
					}
					html.Append("</ol>");
					break;
				case ProtocolBlockTypes.Caution:
				case ProtocolBlockTypes.Warning:
					Open(html, block, "note");
					html.Append("<p class=\"rgp-label\"><strong>").Append(E(block.Type == ProtocolBlockTypes.Warning ? labels.Warning : labels.Caution));
					if (!string.IsNullOrEmpty(block.Title))
						html.Append(": ").Append(E(block.Title));
					html.Append("</strong></p>");
					RenderScopes(html, block, labels);
					html.Append(block.Html ?? string.Empty);
					break;
				case ProtocolBlockTypes.Gate:
					Open(html, block, "note");
					html.Append("<p class=\"rgp-label\"><strong>").Append(E(labels.ContactMedicalControl)).Append("</strong></p>");
					RenderScopes(html, block, labels);
					html.Append(block.Html ?? string.Empty);
					if (!string.IsNullOrEmpty(block.Contact))
						html.Append("<p class=\"rgp-contact\"><strong>").Append(E(labels.Contact)).Append(":</strong> ").Append(E(block.Contact)).Append("</p>");
					break;
				case ProtocolBlockTypes.Table:
					Open(html, block);
					RenderScopes(html, block, labels);
					html.Append("<table class=\"rgp-grid\">");
					if (!string.IsNullOrEmpty(block.Title))
						html.Append("<caption>").Append(E(block.Title)).Append("</caption>");
					var rows = block.Rows ?? new List<List<string>>();
					var first = 0;
					if (block.HeaderRow == true && rows.Count > 0)
					{
						html.Append("<thead><tr>");
						foreach (var cell in rows[0])
							html.Append("<th scope=\"col\">").Append(E(cell)).Append("</th>");
						html.Append("</tr></thead>");
						first = 1;
					}
					html.Append("<tbody>");
					for (var r = first; r < rows.Count; r++)
					{
						html.Append("<tr>");
						foreach (var cell in rows[r])
							html.Append("<td>").Append(E(cell)).Append("</td>");
						html.Append("</tr>");
					}
					html.Append("</tbody></table>");
					break;
				case ProtocolBlockTypes.Image:
					Open(html, block);
					RenderScopes(html, block, labels);
					var src = block.AttachmentId.HasValue ? AttachmentUrl(block.AttachmentId.Value) : block.Url;
					html.Append("<p><img src=\"").Append(E(src)).Append("\" alt=\"").Append(E(block.Alt ?? block.Title)).Append("\" /></p>");
					if (!string.IsNullOrEmpty(block.Title))
						html.Append("<p class=\"rgp-caption\">").Append(E(block.Title)).Append("</p>");
					break;
				case ProtocolBlockTypes.Video:
					Open(html, block);
					RenderScopes(html, block, labels);
					html.Append("<p><a class=\"rgp-video-link\" href=\"").Append(E(block.Url)).Append("\">").Append(E(labels.Video)).Append(": ")
						.Append(E(block.Label ?? block.Url)).Append("</a></p>");
					break;
				case ProtocolBlockTypes.Attachment:
					Open(html, block);
					RenderScopes(html, block, labels);
					html.Append("<p><a class=\"rgp-attachment-link\" href=\"").Append(E(AttachmentUrl(block.AttachmentId ?? 0))).Append("\">")
						.Append(E(labels.Attachment)).Append(": ").Append(E(block.Label ?? labels.Attachment)).Append("</a></p>");
					break;
				case ProtocolBlockTypes.Link:
					Open(html, block);
					RenderScopes(html, block, labels);
					html.Append("<p><a class=\"rgp-link-").Append(block.LinkKind ?? ProtocolLinkKinds.Url).Append("\" href=\"").Append(E(LinkUrl(block))).Append("\">")
						.Append(E(labels.ForLinkKind(block.LinkKind))).Append(": ").Append(E(block.Label ?? block.RefId ?? block.Url)).Append("</a></p>");
					break;
				case ProtocolBlockTypes.Medication:
					RenderMedication(html, block, labels);
					break;
				case ProtocolBlockTypes.Variants:
					Open(html, block, "group");
					RenderScopes(html, block, labels);
					foreach (var variant in block.Variants ?? new List<ProtocolVariant>())
					{
						if (variant == null || !IsValidKey(variant.Key))
							continue;
						html.Append("<div id=\"").Append(Anchor(variant.Key)).Append("\" class=\"rgp-variant\">");
						html.Append("<p class=\"rgp-variant-label\"><strong>").Append(E(variant.Label)).Append("</strong></p>");
						foreach (var child in variant.Blocks ?? new List<ProtocolContentBlock>())
						{
							if (child != null && ProtocolBlockTypes.InsideVariant.Contains(child.Type, StringComparer.Ordinal))
								RenderBlock(html, child, labels);
						}
						html.Append("</div>");
					}
					break;
				default:
					return;
			}

			html.Append("</div>");
		}

		/// <summary>
		/// A medication block: its fields exactly as authored, in a two-column table, with the "shown as authored" notice and an empty
		/// slot the reading view fills with the department's dosing-tool link (the link is not part of the hashed text, so changing
		/// the department setting never changes an approved version).
		/// </summary>
		private static void RenderMedication(StringBuilder html, ProtocolContentBlock block, ProtocolBlockLabels labels)
		{
			var med = block.Medication ?? new ProtocolMedication();
			Open(html, block);
			html.Append("<p class=\"rgp-label\"><strong>").Append(E(labels.Medication)).Append(": ").Append(E(med.Drug)).Append("</strong></p>");
			RenderScopes(html, block, labels);
			html.Append("<table class=\"rgp-med-table\"><tbody>");
			void Row(string label, string value)
			{
				if (!string.IsNullOrEmpty(value))
					html.Append("<tr><th scope=\"row\">").Append(E(label)).Append("</th><td>").Append(E(value)).Append("</td></tr>");
			}
			Row(labels.Indication, med.Indication);
			Row(labels.Concentration, med.Concentration);
			Row(labels.DosePerKg, med.DosePerKg);
			Row(labels.MaxSingleDose, med.MaxSingleDose);
			Row(labels.MaxTotalDose, med.MaxTotalDose);
			Row(labels.Route, med.Route);
			Row(labels.RepeatInterval, med.RepeatInterval);
			html.Append("</tbody></table>");
			if (!string.IsNullOrEmpty(med.InventoryItemId))
				html.Append("<p class=\"rgp-med-inventory\"><a href=\"").Append(E(InventoryUrl(med.InventoryItemId))).Append("\">").Append(E(labels.InventoryItem)).Append(": ")
					.Append(E(med.InventoryItemName ?? med.InventoryItemId)).Append("</a></p>");
			if (!string.IsNullOrWhiteSpace(med.Notes))
				html.Append("<div class=\"rgp-med-notes\">").Append(med.Notes).Append("</div>");
			html.Append("<p class=\"rgp-med-notice\"><em>").Append(E(labels.DoseAsAuthored)).Append("</em></p>");
			html.Append("<span class=\"rgp-dosing-slot\"></span>");
		}

		public static string AttachmentUrl(int attachmentId) =>
			"/User/Protocols/GetProtocolAttachment?protocolAttachmentId=" + attachmentId.ToString(CultureInfo.InvariantCulture);

		public static string InventoryUrl(string itemId) => "/User/Inventory/Index?itemId=" + Uri.EscapeDataString(itemId ?? string.Empty);

		/// <summary>Where a link block goes in the web app (relative), or its https address.</summary>
		public static string LinkUrl(ProtocolContentBlock block)
		{
			var id = Uri.EscapeDataString(block?.RefId ?? string.Empty);
			switch (block?.LinkKind)
			{
				case ProtocolLinkKinds.Protocol: return "/User/Protocols/View/" + id;
				case ProtocolLinkKinds.Poi: return "/User/Mapping/EditPOI?poiId=" + id;
				case ProtocolLinkKinds.InventoryItem: return InventoryUrl(block.RefId);
				case ProtocolLinkKinds.Checklist: return "/User/Checklists/Template/" + id;
				case ProtocolLinkKinds.Training: return "/User/Trainings/View?trainingId=" + id;
				default: return block?.Url ?? string.Empty;
			}
		}

		#endregion Rendering

		#region Conversion

		/// <summary>
		/// "Convert to blocks" (P3): a P1 html document as a block document. Each heading becomes a heading block, each top-level
		/// ordered list without nested lists becomes a steps block (so it can be stepped through on a call), and the rest between them
		/// becomes paragraph blocks. Keys are c1, c2... in order. The source document is not changed.
		/// </summary>
		public static ProtocolContentDocument ConvertFromHtml(string html)
		{
			var document = new ProtocolContentDocument { Schema = ProtocolContentDocument.BlockSchema };
			var counter = 0;
			string Key() => "c" + (++counter).ToString(CultureInfo.InvariantCulture);
			var source = html ?? string.Empty;

			void AddSegment(string segment)
			{
				if (string.IsNullOrWhiteSpace(StringHelpers.HtmlToPlainText(segment)) && segment.IndexOf("<img", StringComparison.OrdinalIgnoreCase) < 0)
					return;

				var position = 0;
				foreach (Match list in OrderedList.Matches(segment))
				{
					var inner = list.Groups[2].Value;
					if (inner.IndexOf("<ol", StringComparison.OrdinalIgnoreCase) >= 0 || inner.IndexOf("<ul", StringComparison.OrdinalIgnoreCase) >= 0)
						continue;

					AddParagraph(segment.Substring(position, list.Index - position));
					var steps = new ProtocolContentBlock { Type = ProtocolBlockTypes.Steps, Key = Key(), Items = new List<ProtocolStepItem>() };
					foreach (Match item in ListItem.Matches(inner))
						steps.Items.Add(new ProtocolStepItem { Key = Key(), Html = item.Groups[2].Value.Trim() });
					if (steps.Items.Count > 0)
						document.Blocks.Add(steps);
					position = list.Index + list.Length;
				}

				AddParagraph(segment.Substring(position));
			}

			void AddParagraph(string segment)
			{
				if (string.IsNullOrWhiteSpace(StringHelpers.HtmlToPlainText(segment)) && segment.IndexOf("<img", StringComparison.OrdinalIgnoreCase) < 0)
					return;
				document.Blocks.Add(new ProtocolContentBlock { Type = ProtocolBlockTypes.Paragraph, Key = Key(), Html = segment.Trim() });
			}

			try
			{
				var position = 0;
				foreach (Match heading in Heading.Matches(source))
				{
					AddSegment(source.Substring(position, heading.Index - position));
					var text = WebUtility.HtmlDecode(StringHelpers.HtmlToPlainText(heading.Groups[3].Value) ?? string.Empty).Trim();
					if (text.Length > 0)
					{
						var level = int.Parse(heading.Groups[1].Value, CultureInfo.InvariantCulture);
						document.Blocks.Add(new ProtocolContentBlock { Type = ProtocolBlockTypes.Heading, Key = Key(), Text = text, Level = Math.Clamp(level, 2, 4) });
					}
					position = heading.Index + heading.Length;
				}

				AddSegment(source.Substring(position));
			}
			catch (RegexMatchTimeoutException)
			{
				document.Blocks.Clear();
				document.Blocks.Add(new ProtocolContentBlock { Type = ProtocolBlockTypes.Paragraph, Key = "c1", Html = source });
			}

			return Normalize(document);
		}

		#endregion Conversion

		#region Reading helpers

		/// <summary>Every block of the document in reading order, variants' blocks included (with the variant they are in).</summary>
		public static IEnumerable<(ProtocolContentBlock Block, ProtocolVariant Variant)> Flatten(ProtocolContentDocument document)
		{
			foreach (var block in document?.Blocks ?? new List<ProtocolContentBlock>())
			{
				if (block == null)
					continue;
				yield return (block, null);
				if (block.Type != ProtocolBlockTypes.Variants)
					continue;
				foreach (var variant in block.Variants ?? new List<ProtocolVariant>())
					foreach (var child in variant?.Blocks ?? new List<ProtocolContentBlock>())
						if (child != null)
							yield return (child, variant);
			}
		}

		/// <summary>The readable text of one block (no labels), for search, AI chunks and step lists.</summary>
		public static string PlainText(ProtocolContentBlock block)
		{
			if (block == null)
				return string.Empty;
			var parts = new List<string>();
			void Add(string value) { if (!string.IsNullOrWhiteSpace(value)) parts.Add(value.Trim()); }
			Add(block.Text);
			Add(block.Title);
			Add(StringHelpers.HtmlToPlainText(block.Html));
			foreach (var item in block.Items ?? new List<ProtocolStepItem>())
				Add(StringHelpers.HtmlToPlainText(item?.Html));
			Add(block.Contact);
			foreach (var row in block.Rows ?? new List<List<string>>())
				Add(string.Join(" | ", (row ?? new List<string>()).Where(c => !string.IsNullOrWhiteSpace(c))));
			Add(block.Alt);
			Add(block.Label);
			if (block.Medication != null)
			{
				var m = block.Medication;
				Add(string.Join(" · ", new[] { m.Drug, m.Indication, m.Concentration, m.DosePerKg, m.MaxSingleDose, m.MaxTotalDose, m.Route, m.RepeatInterval }
					.Where(v => !string.IsNullOrWhiteSpace(v))));
				Add(StringHelpers.HtmlToPlainText(m.Notes));
			}
			return string.Join("\n", parts);
		}

		#endregion Reading helpers
	}
}
