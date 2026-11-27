using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Lms;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Services.Knowledge;

namespace Resgrid.Services
{
	/// <summary>One section of a course-studio source: its anchor, heading path and plain text (exactly as the knowledge index cuts it).</summary>
	public sealed class LmsStudioSection
	{
		public string Anchor { get; set; }
		public string HeadingPath { get; set; }
		public string Text { get; set; }
	}

	/// <summary>A source the course studio drafts from: a protocol version, an uploaded SOP or a course version, cut into sections.</summary>
	public sealed class LmsStudioSource
	{
		public string Outcome { get; set; }
		public string Title { get; set; }
		public string VersionLabel { get; set; }

		/// <summary>DispatchProtocolId or LmsCourseId; 0 for an uploaded document.</summary>
		public int ItemId { get; set; }

		/// <summary>ProtocolVersionId or LmsCourseVersionId; 0 for an uploaded document.</summary>
		public int VersionId { get; set; }

		/// <summary>The uploaded document's file name.</summary>
		public string FileName { get; set; }

		public bool IsProtocol { get; set; }
		public bool IsCourse { get; set; }
		public List<LmsStudioSection> Sections { get; set; } = new List<LmsStudioSection>();

		public static LmsStudioSource Fail(string outcome) => new LmsStudioSource { Outcome = outcome };

		/// <summary>A label for one cited section: "Protocol 1.2 · Heading", "file.pdf › p. 3".</summary>
		public string Label(LmsStudioSection section)
		{
			var head = string.IsNullOrWhiteSpace(VersionLabel) ? Title : Title + " " + VersionLabel;
			var path = section?.HeadingPath?.Trim();
			return string.IsNullOrWhiteSpace(path) ? head : head + " · " + path;
		}

		/// <summary>Where a cited section opens (a protocol version's section, a course lesson); null for an uploaded document.</summary>
		public string Url(string anchor)
		{
			if (IsProtocol)
				return KnowledgeLinks.WebPath((int)KnowledgeSourceKinds.Protocol, ItemId.ToString(CultureInfo.InvariantCulture), VersionId.ToString(CultureInfo.InvariantCulture), anchor);
			if (IsCourse)
				return KnowledgeLinks.WebPath((int)KnowledgeSourceKinds.Course, ItemId.ToString(CultureInfo.InvariantCulture), VersionId.ToString(CultureInfo.InvariantCulture), anchor);
			return null;
		}

		/// <summary>Every section with the anchor, as one text (a long section is cut into parts that share it).</summary>
		public string TextOf(string anchor) =>
			string.Join("\n", Sections.Where(s => string.Equals(s.Anchor ?? string.Empty, anchor ?? string.Empty, StringComparison.Ordinal)).Select(s => s.Text));

		public bool HasAnchor(string anchor) => Sections.Any(s => string.Equals(s.Anchor ?? string.Empty, anchor ?? string.Empty, StringComparison.Ordinal));
	}

	/// <summary>
	/// Builds course-studio sources the way the K1 knowledge index cuts content, so a draft cites the anchors the reading views show: a protocol
	/// version per block (b-{key}) or heading, a document-backed version per extracted page, an uploaded SOP per page (its text extracted by the
	/// protocol file reader, PdfPig/OpenXml, never stored), a course version per lesson block (l{lesson}-…). Never reads assessments or learners.
	/// </summary>
	public static class LmsStudioSources
	{
		/// <summary>An uploaded SOP's extraction budget while the request waits.</summary>
		public static readonly TimeSpan DocumentExtractionBudget = TimeSpan.FromSeconds(20);

		public static readonly string[] DocumentExtensions = { ".pdf", ".docx", ".txt" };

		/// <summary>A published (or superseded) protocol version's sections (the baseline <see cref="ProtocolAiSources"/> cut, shared with the protocol AI tools).</summary>
		public static async Task<LmsStudioSource> ProtocolAsync(IKnowledgePageTextSource pages, int departmentId, DispatchProtocol protocol, ProtocolVersion version,
			CancellationToken cancellationToken)
		{
			var cut = await ProtocolAiSources.FromVersionAsync(pages, departmentId, protocol, version, cancellationToken);
			return new LmsStudioSource
			{
				Title = cut.Title,
				VersionLabel = cut.VersionLabel,
				ItemId = cut.ProtocolId,
				VersionId = cut.VersionId,
				IsProtocol = true,
				Sections = cut.Sections.Select(s => new LmsStudioSection { Anchor = s.Anchor, HeadingPath = s.HeadingPath, Text = s.Text }).ToList()
			};
		}

		/// <summary>An uploaded SOP's pages (PDF, DOCX or UTF-8 text); NoContent when nothing readable came out.</summary>
		public static LmsStudioSource Document(IProtocolTextExtractor extractor, string fileName, string contentType, byte[] data, CancellationToken cancellationToken)
		{
			var name = string.IsNullOrWhiteSpace(fileName) ? "document" : System.IO.Path.GetFileName(fileName.Trim());
			if (name.Length > 200)
				name = name.Substring(name.Length - 200);
			var extension = System.IO.Path.GetExtension(name).ToLowerInvariant();
			if (data == null || data.Length == 0 || data.Length > LmsCourseDraftRequest.MaxDocumentBytes || !DocumentExtensions.Contains(extension))
				return LmsStudioSource.Fail(LmsStudioOutcomes.Invalid);

			List<string> pages;
			if (extension == ".txt")
			{
				string text;
				try
				{
					text = new UTF8Encoding(false, true).GetString(data);
				}
				catch (DecoderFallbackException)
				{
					return LmsStudioSource.Fail(LmsStudioOutcomes.NoContent);
				}
				pages = new List<string> { text };
			}
			else
			{
				if (extractor == null)
					return LmsStudioSource.Fail(LmsStudioOutcomes.Unavailable);
				using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
				budget.CancelAfter(DocumentExtractionBudget);
				var extracted = extractor.Extract(name, extension == ".pdf" ? ProtocolDocumentRules.PdfContentType : ProtocolDocumentRules.DocxContentType, data, budget.Token);
				if (extracted == null || extracted.Status != ProtocolFileTextStatuses.Extracted)
					return LmsStudioSource.Fail(LmsStudioOutcomes.NoContent);
				pages = extracted.Pages ?? new List<string>();
			}

			var sections = KnowledgeChunker.SplitPages(pages.Select((p, i) => new KnowledgePageText { AttachmentId = "sop", FileName = name, PageNumber = i + 1, Text = p }),
				KnowledgeConfig.MaxChunkChars);
			return new LmsStudioSource { Title = name, FileName = name, Sections = Clean(sections) };
		}

		/// <summary>A course version's lesson sections (blocks and video transcripts), citing each lesson; nothing from assessments or learners.</summary>
		public static LmsStudioSource Course(LmsCourseOutline outline)
		{
			var source = new LmsStudioSource
			{
				Title = string.IsNullOrWhiteSpace(outline?.Version?.Title) ? outline?.Course?.Title : outline.Version.Title,
				VersionLabel = outline?.Version?.Label,
				ItemId = outline?.Course?.LmsCourseId ?? 0,
				VersionId = outline?.Version?.LmsCourseVersionId ?? 0,
				IsCourse = true
			};
			var sections = new List<LmsStudioSection>();
			foreach (var module in outline?.Modules ?? new List<LmsOutlineModule>())
			{
				foreach (var lesson in module.Lessons ?? new List<LmsLesson>())
				{
					IEnumerable<KnowledgeChunker.Section> parts;
					switch ((LmsLessonKinds)lesson.Kind)
					{
						case LmsLessonKinds.Blocks:
							var document = ProtocolContentDocument.Parse(lesson.ContentJson, lesson.ContentHtml);
							parts = document.IsBlockModel ? KnowledgeChunker.SplitBlocks(document, KnowledgeConfig.MaxChunkChars)
								: KnowledgeChunker.SplitHtml(string.IsNullOrWhiteSpace(document.Render()) ? lesson.ContentHtml : document.Render(), KnowledgeConfig.MaxChunkChars);
							break;
						case LmsLessonKinds.Video:
							parts = KnowledgeChunker.Cut(lesson.TranscriptText ?? string.Empty, KnowledgeConfig.MaxChunkChars).Select(t => new KnowledgeChunker.Section { Text = t });
							break;
						default:
							continue;
					}

					var lessonPath = Join(module.Module?.Title, lesson.Title);
					foreach (var part in parts)
						sections.Add(new LmsStudioSection
						{
							Anchor = KnowledgeLinks.CourseAnchor(lesson.LmsLessonId, part.Anchor),
							HeadingPath = Truncate(Join(lessonPath, part.HeadingPath), LmsFlashcard.HeadingMaxLength),
							Text = part.Text
						});
				}
			}
			source.Sections = sections.Where(s => !string.IsNullOrWhiteSpace(s.Text)).ToList();
			return source;
		}

		private static List<LmsStudioSection> Clean(IEnumerable<KnowledgeChunker.Section> sections) =>
			(sections ?? Enumerable.Empty<KnowledgeChunker.Section>()).Where(s => s != null && !string.IsNullOrWhiteSpace(s.Text))
				.Select(s => new LmsStudioSection { Anchor = s.Anchor, HeadingPath = s.HeadingPath, Text = s.Text.Trim() }).ToList();

		private static string Join(string first, string second)
		{
			var a = (first ?? string.Empty).Trim();
			var b = (second ?? string.Empty).Trim();
			return a.Length == 0 ? b : b.Length == 0 ? a : a + KnowledgeChunker.PathSeparator + b;
		}

		private static string Truncate(string value, int max) => string.IsNullOrEmpty(value) || value.Length <= max ? value : value.Substring(0, max);

		/// <summary>Plain text as html paragraphs (encoded).</summary>
		public static string Paragraphs(string text) => ProtocolAiSources.Paragraphs(text);
	}
}
