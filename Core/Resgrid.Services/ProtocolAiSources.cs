using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Services.Knowledge;

namespace Resgrid.Services
{
	/// <summary>One section of a protocol version as the AI tools cite it: its anchor, heading path and plain text (as the knowledge index cuts it).</summary>
	public sealed class ProtocolAiSection
	{
		public string Anchor { get; set; }
		public string HeadingPath { get; set; }
		public string Text { get; set; }
	}

	/// <summary>A published (or superseded) protocol version cut into sections for drafting (content-neutral; the LMS course studio uses it too).</summary>
	public sealed class ProtocolAiSource
	{
		public string Title { get; set; }
		public string VersionLabel { get; set; }
		public int ProtocolId { get; set; }
		public int VersionId { get; set; }
		public List<ProtocolAiSection> Sections { get; set; } = new List<ProtocolAiSection>();

		/// <summary>A label for one cited section: "Protocol 1.2 · Heading".</summary>
		public string Label(ProtocolAiSection section)
		{
			var head = string.IsNullOrWhiteSpace(VersionLabel) ? Title : Title + " " + VersionLabel;
			var path = section?.HeadingPath?.Trim();
			return string.IsNullOrWhiteSpace(path) ? head : head + " · " + path;
		}

		/// <summary>Where a cited section of this version opens.</summary>
		public string Url(string anchor) =>
			KnowledgeLinks.WebPath((int)KnowledgeSourceKinds.Protocol, ProtocolId.ToString(CultureInfo.InvariantCulture), VersionId.ToString(CultureInfo.InvariantCulture), anchor);

		/// <summary>Every section with the anchor, as one text (a long section is cut into parts that share it).</summary>
		public string TextOf(string anchor) =>
			string.Join("\n", Sections.Where(s => string.Equals(s.Anchor ?? string.Empty, anchor ?? string.Empty, StringComparison.Ordinal)).Select(s => s.Text));

		public bool HasAnchor(string anchor) => Sections.Any(s => string.Equals(s.Anchor ?? string.Empty, anchor ?? string.Empty, StringComparison.Ordinal));
	}

	/// <summary>
	/// Builds the drafting source of a protocol version the way the K1 knowledge index cuts it, so a draft cites the anchors the reading view
	/// shows: a block version per block (b-{key}) or heading, a document-backed version per extracted page. Shared by the protocol AI tools and
	/// the LMS course studio (which maps it onto its own source type); it never reads anything but the version's content.
	/// </summary>
	public static class ProtocolAiSources
	{
		public static async Task<ProtocolAiSource> FromVersionAsync(IKnowledgePageTextSource pages, int departmentId, DispatchProtocol protocol, ProtocolVersion version,
			CancellationToken cancellationToken)
		{
			var source = new ProtocolAiSource
			{
				Title = protocol.Name,
				VersionLabel = version.Label,
				ProtocolId = protocol.DispatchProtocolId,
				VersionId = version.ProtocolVersionId
			};

			IEnumerable<KnowledgeChunker.Section> sections;
			if (version.IsDocument)
			{
				var texts = pages == null ? null : await pages.GetPagesAsync(departmentId, KnowledgeSourceKinds.ProtocolFile, protocol.DispatchProtocolId.ToString(CultureInfo.InvariantCulture),
					version.ProtocolVersionId.ToString(CultureInfo.InvariantCulture), cancellationToken);
				sections = KnowledgeChunker.SplitPages((texts ?? new List<KnowledgePageText>()).Where(p => p.AttachmentId != null && p.AttachmentId.StartsWith("file-", StringComparison.Ordinal)),
					KnowledgeConfig.MaxChunkChars);
			}
			else
			{
				sections = ProtocolKnowledgeSource.BuildVersionChunks(departmentId, protocol, version, null)
					.Select(c => new KnowledgeChunker.Section { Anchor = c.Anchor, HeadingPath = c.HeadingPath, Text = c.Text });
			}

			source.Sections = (sections ?? Enumerable.Empty<KnowledgeChunker.Section>()).Where(s => s != null && !string.IsNullOrWhiteSpace(s.Text))
				.Select(s => new ProtocolAiSection { Anchor = s.Anchor, HeadingPath = s.HeadingPath, Text = s.Text.Trim() }).ToList();
			return source;
		}

		/// <summary>Plain text as html paragraphs (encoded).</summary>
		public static string Paragraphs(string text) =>
			string.Concat((text ?? string.Empty).Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).Select(l => "<p>" + WebUtility.HtmlEncode(l) + "</p>"));

		/// <summary>An absolute link for stored HTML (the sanitizer keeps only absolute links): the system's own address in front; null without one.</summary>
		public static string AbsoluteUrl(string path)
		{
			if (string.IsNullOrWhiteSpace(path))
				return null;
			var root = (SystemBehaviorConfig.ResgridBaseUrl ?? string.Empty).Trim().TrimEnd('/');
			return Uri.TryCreate(root, UriKind.Absolute, out var baseUri) && (baseUri.Scheme == Uri.UriSchemeHttps || baseUri.Scheme == Uri.UriSchemeHttp)
				? root + (path.StartsWith("/", StringComparison.Ordinal) ? path : "/" + path)
				: null;
		}
	}
}
