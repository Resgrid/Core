using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Resgrid.Framework;

namespace Resgrid.Model
{
	/// <summary>
	/// The block-aware chunker (§16.2 step 2), pure. Rich text is cut at its headings (h1–h4): one chunk per heading section, carrying the
	/// heading path above it and the section's anchor, which is exactly the id <see cref="ProtocolContentDocument.InjectAnchors"/> puts on
	/// that heading when the page is displayed, so a citation opens the exact section (heading anchors until P3 adds block anchors, R-S1).
	/// Text before the first heading is a chunk without an anchor. A section longer than the limit is cut at sentence or word boundaries into
	/// parts that keep its anchor. Extracted file text is chunked by page.
	/// <para>
	/// A P3 block document is chunked by block (<see cref="SplitBlocks"/>): a heading block starts a section whose paragraphs cite the
	/// heading's block anchor (b-{key}); every structured block (steps, caution, gate, table, medication, variant group...) is a section of
	/// its own citing its own anchor, so a citation opens exactly the block. P1 html documents keep their heading anchors.
	/// </para>
	/// </summary>
	public static class KnowledgeChunker
	{
		public const int DefaultMaxChars = 1800;
		public const string PathSeparator = " › ";

		// The heading pattern ProtocolContentDocument uses, so the sections line up with its outline and its display anchors.
		private static readonly Regex Heading = new Regex(@"<h([1-4])(\s[^>]*)?>([\s\S]*?)</h\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(2));

		public sealed class Section
		{
			/// <summary>The heading's anchor id; null for text before the first heading and for page chunks.</summary>
			public string Anchor { get; set; }

			public string HeadingPath { get; set; }

			public string Text { get; set; }
		}

		/// <summary>The sections of stored (sanitized) HTML, in document order.</summary>
		public static List<Section> SplitHtml(string html, int maxChars = DefaultMaxChars)
		{
			var sections = new List<Section>();
			if (string.IsNullOrWhiteSpace(html))
				return sections;

			maxChars = Math.Max(200, maxChars);
			var outline = ProtocolContentDocument.Outline(html);

			List<Match> headings;
			try
			{
				// Only headings with text get an anchor (Outline and InjectAnchors skip the empty ones), so only they start a section.
				headings = Heading.Matches(html).Cast<Match>().Where(m => HeadingText(m).Length > 0).ToList();
			}
			catch (RegexMatchTimeoutException)
			{
				headings = new List<Match>();
			}

			if (headings.Count != outline.Count)
			{
				// Never cite an anchor that might not be the one shown: without a matching outline the text is one unanchored section.
				Emit(sections, null, null, html, maxChars);
				return sections;
			}

			var stack = new List<(int Level, string Text)>();
			var position = 0;
			string anchor = null;
			string path = null;
			for (var i = 0; i < headings.Count; i++)
			{
				var match = headings[i];
				Emit(sections, anchor, path, html.Substring(position, match.Index - position), maxChars);
				position = match.Index + match.Length;

				var level = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
				while (stack.Count > 0 && stack[stack.Count - 1].Level >= level)
					stack.RemoveAt(stack.Count - 1);
				stack.Add((level, Truncate(HeadingText(match), 120)));

				anchor = outline[i].Id;
				path = Truncate(string.Join(PathSeparator, stack.Select(s => s.Text)), KnowledgeChunk.HeadingPathMaxLength);
			}

			Emit(sections, anchor, path, html.Substring(position), maxChars);
			return sections;
		}

		/// <summary>
		/// The sections of a P3 block document, in reading order (see the class summary). Anchors are the block anchors the version's HTML
		/// carries (<c>b-{key}</c>), so they are always the ids shown. Not a block document: the rendered html is split by headings instead.
		/// </summary>
		public static List<Section> SplitBlocks(ProtocolContentDocument document, int maxChars = DefaultMaxChars)
		{
			if (document == null || !document.IsBlockModel)
				return SplitHtml(document?.Render(), maxChars);

			maxChars = Math.Max(200, maxChars);
			var sections = new List<Section>();
			var stack = new List<(int Level, string Text)>();
			string headingAnchor = null;
			string path = null;
			string bufferAnchor = null;
			var buffer = new StringBuilder();

			void Flush()
			{
				if (buffer.Length > 0)
					EmitText(sections, bufferAnchor, path, buffer.ToString(), maxChars);
				buffer.Clear();
				bufferAnchor = null;
			}

			foreach (var block in document.Blocks ?? new List<ProtocolContentBlock>())
			{
				if (block == null || !ProtocolBlocks.IsValidKey(block.Key))
					continue;

				switch (block.Type)
				{
					case ProtocolBlockTypes.Heading:
						Flush();
						var level = Math.Clamp(block.Level ?? 2, 2, 4);
						while (stack.Count > 0 && stack[stack.Count - 1].Level >= level)
							stack.RemoveAt(stack.Count - 1);
						stack.Add((level, Truncate(block.Text ?? string.Empty, 120)));
						headingAnchor = ProtocolBlocks.Anchor(block.Key);
						path = Truncate(string.Join(PathSeparator, stack.Select(s => s.Text)), KnowledgeChunk.HeadingPathMaxLength);
						break;
					case ProtocolBlockTypes.Paragraph:
					case ProtocolBlockTypes.Html:
						bufferAnchor ??= headingAnchor ?? ProtocolBlocks.Anchor(block.Key);
						buffer.Append(' ').Append(ScopePrefix(block)).Append(StringHelpers.HtmlToPlainText(block.Html));
						break;
					case ProtocolBlockTypes.Variants:
						Flush();
						foreach (var variant in block.Variants ?? new List<ProtocolVariant>())
						{
							if (variant == null || !ProtocolBlocks.IsValidKey(variant.Key))
								continue;
							var text = string.Join(" ", (variant.Blocks ?? new List<ProtocolContentBlock>()).Select(KnowledgeText).Where(t => t.Length > 0));
							var variantPath = Truncate(string.IsNullOrEmpty(path) ? variant.Label : path + PathSeparator + variant.Label, KnowledgeChunk.HeadingPathMaxLength);
							EmitText(sections, ProtocolBlocks.Anchor(variant.Key), variantPath, (variant.Label ?? string.Empty) + ": " + text, maxChars);
						}
						break;
					default:
						Flush();
						EmitText(sections, ProtocolBlocks.Anchor(block.Key), path, KnowledgeText(block), maxChars);
						break;
				}
			}

			Flush();
			return sections;
		}

		/// <summary>One block's text for the knowledge index: its words with the field names a reader sees (English), never computed values.</summary>
		public static string KnowledgeText(ProtocolContentBlock block)
		{
			if (block == null)
				return string.Empty;
			var labels = ProtocolBlockLabels.Default;
			var prefix = ScopePrefix(block);
			switch (block.Type)
			{
				case ProtocolBlockTypes.Caution:
				case ProtocolBlockTypes.Warning:
					return prefix + (block.Type == ProtocolBlockTypes.Warning ? labels.Warning : labels.Caution) + ": " + ProtocolBlocks.PlainText(block);
				case ProtocolBlockTypes.Gate:
					return prefix + labels.ContactMedicalControl + ": " + ProtocolBlocks.PlainText(block);
				case ProtocolBlockTypes.Medication:
					var m = block.Medication ?? new ProtocolMedication();
					var parts = new List<string> { labels.Medication + ": " + m.Drug };
					void Add(string label, string value) { if (!string.IsNullOrWhiteSpace(value)) parts.Add(label + ": " + value); }
					Add(labels.Indication, m.Indication);
					Add(labels.Concentration, m.Concentration);
					Add(labels.DosePerKg, m.DosePerKg);
					Add(labels.MaxSingleDose, m.MaxSingleDose);
					Add(labels.MaxTotalDose, m.MaxTotalDose);
					Add(labels.Route, m.Route);
					Add(labels.RepeatInterval, m.RepeatInterval);
					Add(labels.InventoryItem, m.InventoryItemName);
					var notes = StringHelpers.HtmlToPlainText(m.Notes);
					if (!string.IsNullOrWhiteSpace(notes))
						parts.Add(notes);
					return prefix + string.Join(". ", parts);
				default:
					return prefix + ProtocolBlocks.PlainText(block);
			}
		}

		private static string ScopePrefix(ProtocolContentBlock block) =>
			block?.Scopes == null || block.Scopes.Count == 0 ? string.Empty
				: "(" + ProtocolBlockLabels.Default.ScopeOfPractice + ": " + string.Join(", ", block.Scopes.Select(s => s.Name)) + ") ";

		private static void EmitText(List<Section> sections, string anchor, string path, string text, int maxChars)
		{
			var clean = Normalize(text);
			if (clean.Length == 0)
				return;
			foreach (var part in Cut(clean, maxChars))
				sections.Add(new Section { Anchor = Truncate(anchor, KnowledgeChunk.AnchorMaxLength), HeadingPath = path, Text = part });
		}

		/// <summary>Extracted file text, one section per page (cut further when a page is long).</summary>
		public static List<Section> SplitPages(IEnumerable<KnowledgePageText> pages, int maxChars = DefaultMaxChars)
		{
			var sections = new List<Section>();
			maxChars = Math.Max(200, maxChars);
			foreach (var page in (pages ?? Enumerable.Empty<KnowledgePageText>())
				.Where(p => p != null && p.PageNumber > 0 && !string.IsNullOrWhiteSpace(p.Text))
				.OrderBy(p => p.AttachmentId, StringComparer.Ordinal).ThenBy(p => p.PageNumber))
			{
				var anchor = "page-" + Slug(page.AttachmentId) + "-" + page.PageNumber.ToString(CultureInfo.InvariantCulture);
				var path = Truncate((string.IsNullOrWhiteSpace(page.FileName) ? "" : page.FileName.Trim() + PathSeparator) + "p. " + page.PageNumber.ToString(CultureInfo.InvariantCulture),
					KnowledgeChunk.HeadingPathMaxLength);
				foreach (var part in Cut(Normalize(page.Text), maxChars))
					sections.Add(new Section { Anchor = Truncate(anchor, KnowledgeChunk.AnchorMaxLength), HeadingPath = path, Text = part });
			}

			return sections;
		}

		/// <summary>What is embedded and keyword-scored for a chunk: its title and heading path give a bare section its context.</summary>
		public static string EmbeddingInput(KnowledgeChunk chunk)
		{
			if (chunk == null)
				return string.Empty;
			var builder = new StringBuilder();
			if (!string.IsNullOrWhiteSpace(chunk.Title))
				builder.Append(chunk.Title.Trim());
			if (!string.IsNullOrWhiteSpace(chunk.HeadingPath))
				builder.Append(builder.Length > 0 ? PathSeparator : string.Empty).Append(chunk.HeadingPath.Trim());
			if (!string.IsNullOrWhiteSpace(chunk.Text))
				builder.Append(builder.Length > 0 ? "\n" : string.Empty).Append(chunk.Text.Trim());
			return builder.ToString();
		}

		/// <summary>SHA-256 of the embedding input, lower-case hex: an unchanged chunk keeps its embedding.</summary>
		public static string Hash(string text)
		{
			using var sha = SHA256.Create();
			var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text ?? string.Empty));
			var hex = new StringBuilder(bytes.Length * 2);
			foreach (var b in bytes)
				hex.Append(b.ToString("x2", CultureInfo.InvariantCulture));
			return hex.ToString();
		}

		private static void Emit(List<Section> sections, string anchor, string path, string bodyHtml, int maxChars)
		{
			var text = Normalize(StringHelpers.HtmlToPlainText(bodyHtml ?? string.Empty));
			if (text.Length == 0)
				return;
			foreach (var part in Cut(text, maxChars))
				sections.Add(new Section { Anchor = anchor, HeadingPath = path, Text = part });
		}

		private static string HeadingText(Match match) =>
			WebUtility.HtmlDecode(StringHelpers.HtmlToPlainText(match.Groups[3].Value) ?? string.Empty).Trim();

		private static string Normalize(string text) =>
			Regex.Replace(text ?? string.Empty, @"\s+", " ", RegexOptions.None, TimeSpan.FromSeconds(1)).Trim();

		/// <summary>Parts no longer than <paramref name="maxChars"/>, cut after a sentence where one ends late enough, else at a space.</summary>
		public static IEnumerable<string> Cut(string text, int maxChars)
		{
			text = (text ?? string.Empty).Trim();
			while (text.Length > maxChars)
			{
				var window = text.Substring(0, maxChars);
				var cut = Math.Max(window.LastIndexOf(". ", StringComparison.Ordinal), Math.Max(window.LastIndexOf("? ", StringComparison.Ordinal), window.LastIndexOf("! ", StringComparison.Ordinal)));
				if (cut < maxChars / 2)
					cut = window.LastIndexOf(' ');
				else
					cut += 1;
				if (cut < maxChars / 4)
					cut = maxChars;
				yield return text.Substring(0, cut).Trim();
				text = text.Substring(cut).Trim();
			}

			if (text.Length > 0)
				yield return text;
		}

		private static string Slug(string value)
		{
			var slug = Regex.Replace((value ?? string.Empty).ToLowerInvariant(), "[^a-z0-9]+", "-", RegexOptions.None, TimeSpan.FromSeconds(1)).Trim('-');
			return slug.Length == 0 ? "file" : (slug.Length > 40 ? slug.Substring(0, 40) : slug);
		}

		private static string Truncate(string value, int max) =>
			string.IsNullOrEmpty(value) || value.Length <= max ? value : value.Substring(0, max);
	}
}
