using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>What a protocol version's content is (ProtocolVersions.ContentKind, M0353). Persisted: append only.</summary>
	public enum ProtocolVersionContentKinds
	{
		/// <summary>Authored content: ContentJson blocks rendered to sanitized ContentHtml; the hash is of that HTML.</summary>
		Html = 0,

		/// <summary>
		/// An uploaded document (R-P5): one primary PDF or DOCX in ProtocolVersionFiles. The hash is the SHA-256 of the file's
		/// bytes; ContentHtml carries only a fixed stub (title and file name) for the readers that predate documents.
		/// </summary>
		Document = 1
	}

	/// <summary>Which file a ProtocolFileTexts row was extracted from. Persisted: append only.</summary>
	public enum ProtocolFileSourceKinds
	{
		/// <summary>A document-backed version's primary file (SourceId = ProtocolVersionFileId).</summary>
		VersionFile = 0,

		/// <summary>A protocol attachment (SourceId = DispatchProtocolAttachmentId).</summary>
		Attachment = 1
	}

	/// <summary>Where a file's text extraction stands. Persisted: append only.</summary>
	public enum ProtocolFileTextStatuses
	{
		/// <summary>Queued for worker 86 (a file over the inline threshold, or one whose inline attempt ran out of time).</summary>
		Pending = 0,

		/// <summary>Text extracted into ProtocolFileTextPages.</summary>
		Extracted = 1,

		/// <summary>No text could be read (encrypted, scanned, too large, unsupported); <see cref="ProtocolFileText.Reason"/> says why.</summary>
		NoText = 2,

		/// <summary>Extraction failed after its attempts; kept with the error so it is not retried forever.</summary>
		Failed = 3
	}

	/// <summary>Why a file has no text (ProtocolFileTexts.Reason). Stored as text; stable keys.</summary>
	public static class ProtocolFileNoTextReasons
	{
		public const string Encrypted = "encrypted";
		public const string Scanned = "scanned";
		public const string TooLarge = "too_large";
		public const string Unsupported = "unsupported";
		public const string Unreadable = "unreadable";
		public const string Timeout = "timeout";
	}

	/// <summary>
	/// The primary file of a document-backed protocol version (P1 R-P5, M0353): one per version, immutable once the version leaves
	/// draft. The bytes are only ever loaded by the one authorized file endpoint and by hashing; list reads select metadata.
	/// </summary>
	[Table("ProtocolVersionFiles")]
	public class ProtocolVersionFile : IEntity
	{
		public const int FileNameMaxLength = 256;
		public const int ContentTypeMaxLength = 128;

		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int ProtocolVersionFileId { get; set; }

		public int DepartmentId { get; set; }

		public int DispatchProtocolId { get; set; }

		public int ProtocolVersionId { get; set; }

		[MaxLength(FileNameMaxLength)]
		public string FileName { get; set; }

		[MaxLength(ContentTypeMaxLength)]
		public string ContentType { get; set; }

		public long Size { get; set; }

		/// <summary>SHA-256 of <see cref="Data"/>, lower-case hex; equals the version's ContentHash.</summary>
		[MaxLength(64)]
		public string Sha256 { get; set; }

		[JsonIgnore]
		public byte[] Data { get; set; }

		public DateTime CreatedOn { get; set; }

		public string CreatedByUserId { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return ProtocolVersionFileId; }
			set { ProtocolVersionFileId = (int)value; }
		}

		[NotMapped]
		public string TableName => "ProtocolVersionFiles";

		[NotMapped]
		public string IdName => "ProtocolVersionFileId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>
	/// The text extraction of one file (a version's primary file or an attachment, M0353): its status and, when extracted, its
	/// pages in ProtocolFileTextPages (page-numbered so the knowledge index can chunk by page). One row per file.
	/// </summary>
	[Table("ProtocolFileTexts")]
	public class ProtocolFileText : IEntity
	{
		public const int ReasonMaxLength = 64;

		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int ProtocolFileTextId { get; set; }

		public int DepartmentId { get; set; }

		public int DispatchProtocolId { get; set; }

		/// <summary><see cref="ProtocolFileSourceKinds"/>.</summary>
		public int SourceKind { get; set; }

		public int SourceId { get; set; }

		/// <summary>The version a primary file belongs to; null for an attachment.</summary>
		public int? ProtocolVersionId { get; set; }

		/// <summary><see cref="ProtocolFileTextStatuses"/>.</summary>
		public int Status { get; set; }

		/// <summary><see cref="ProtocolFileNoTextReasons"/> for NoText, a short error for Failed.</summary>
		[MaxLength(ReasonMaxLength)]
		public string Reason { get; set; }

		public int PageCount { get; set; }

		public int CharCount { get; set; }

		public int Attempts { get; set; }

		public DateTime QueuedOn { get; set; }

		public DateTime? ExtractedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return ProtocolFileTextId; }
			set { ProtocolFileTextId = (int)value; }
		}

		[NotMapped]
		public string TableName => "ProtocolFileTexts";

		[NotMapped]
		public string IdName => "ProtocolFileTextId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>One page of a file's extracted text (M0353). Plain text, never markup.</summary>
	[Table("ProtocolFileTextPages")]
	public class ProtocolFileTextPage : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int ProtocolFileTextPageId { get; set; }

		public int ProtocolFileTextId { get; set; }

		public int DepartmentId { get; set; }

		/// <summary>1-based.</summary>
		public int PageNumber { get; set; }

		public string Text { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return ProtocolFileTextPageId; }
			set { ProtocolFileTextPageId = (int)value; }
		}

		[NotMapped]
		public string TableName => "ProtocolFileTextPages";

		[NotMapped]
		public string IdName => "ProtocolFileTextPageId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>What one extraction produced: the pages, or why there is no text.</summary>
	public sealed class ProtocolTextExtractionResult
	{
		public ProtocolFileTextStatuses Status { get; set; }

		public string Reason { get; set; }

		public List<string> Pages { get; set; } = new List<string>();

		public int CharCount => Pages.Sum(p => p?.Length ?? 0);

		public static ProtocolTextExtractionResult NoText(string reason) => new ProtocolTextExtractionResult { Status = ProtocolFileTextStatuses.NoText, Reason = reason };

		public static ProtocolTextExtractionResult Failed(string reason) => new ProtocolTextExtractionResult { Status = ProtocolFileTextStatuses.Failed, Reason = reason };
	}

	/// <summary>One file of a bulk upload and what became of it.</summary>
	public sealed class ProtocolBulkUploadItem
	{
		public string FileName { get; set; }

		public string Title { get; set; }

		public string Number { get; set; }

		public string Category { get; set; }

		public int? ProtocolId { get; set; }

		/// <summary>Null when created; otherwise a <see cref="ProtocolLifecycleErrors"/> name or "duplicate".</summary>
		public string Error { get; set; }

		public bool Succeeded => ProtocolId.HasValue && Error == null;
	}

	/// <summary>The rules of document-backed protocols (P1 R-P5): which files, how big, how they are named, hashed and extracted.</summary>
	public static class ProtocolDocumentRules
	{
		public const string PdfContentType = "application/pdf";
		public const string DocxContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

		/// <summary>The per-file cap (decision 9b: 30 MB, allowed types only).</summary>
		public const long MaxFileBytes = 30L * 1024 * 1024;

		/// <summary>Files up to this size are extracted while the upload request waits; larger ones are queued for worker 86.</summary>
		public const long InlineExtractionMaxBytes = 2L * 1024 * 1024;

		/// <summary>Time an inline extraction may take before it is handed to worker 86.</summary>
		public static readonly TimeSpan InlineExtractionBudget = TimeSpan.FromSeconds(5);

		/// <summary>Time worker 86 gives one file.</summary>
		public static readonly TimeSpan WorkerExtractionBudget = TimeSpan.FromSeconds(60);

		/// <summary>Pages read from one file; a longer document keeps its first pages' text.</summary>
		public const int MaxPages = 2000;

		/// <summary>Characters kept from one file (the rest is not stored).</summary>
		public const int MaxChars = 2_000_000;

		/// <summary>A PDF yielding fewer letters than this over all its pages is treated as scanned (images, no text layer).</summary>
		public const int ScannedLetterThreshold = 20;

		/// <summary>Attempts before a queued extraction is marked Failed.</summary>
		public const int MaxAttempts = 3;

		/// <summary>Files one bulk upload takes.</summary>
		public const int MaxBulkFiles = 200;

		private static readonly Regex NumberAndTitle = new Regex(@"^(?<number>[A-Za-z]{1,8}[-_ ]?\d+(?:[.\-]\d+)*[A-Za-z]?)[\s_\-–]+(?<title>.+)$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));

		/// <summary>The content type the server trusts for a file, from its extension and its first bytes; null when it is not PDF or DOCX.</summary>
		public static string DetectContentType(string fileName, byte[] data)
		{
			if (data == null || data.Length < 4)
				return null;

			var extension = (Path.GetExtension(fileName ?? string.Empty) ?? string.Empty).ToLowerInvariant();
			var isPdf = data[0] == 0x25 && data[1] == 0x50 && data[2] == 0x44 && data[3] == 0x46; // %PDF
			var isZip = data[0] == 0x50 && data[1] == 0x4B && data[2] == 0x03 && data[3] == 0x04; // PK..

			if (isPdf && (extension == ".pdf" || extension.Length == 0))
				return PdfContentType;
			if (isZip && extension == ".docx")
				return DocxContentType;
			return null;
		}

		public static bool IsAllowed(string fileName, byte[] data) => DetectContentType(fileName, data) != null;

		/// <summary>The version hash of a document-backed version: SHA-256 of the file's bytes, lower-case hex.</summary>
		public static string HashFile(byte[] data) => Convert.ToHexString(SHA256.HashData(data ?? Array.Empty<byte>())).ToLowerInvariant();

		/// <summary>A file name made safe to store and to send back in a Content-Disposition header.</summary>
		public static string SafeFileName(string fileName)
		{
			var name = Path.GetFileName((fileName ?? string.Empty).Replace('\\', '/'));
			var clean = new string(name.Where(c => !char.IsControl(c) && c != '"' && c != '/' && c != '\\').ToArray()).Trim();
			if (clean.Length == 0)
				clean = "document";
			return clean.Length > ProtocolVersionFile.FileNameMaxLength ? clean.Substring(clean.Length - ProtocolVersionFile.FileNameMaxLength) : clean;
		}

		/// <summary>
		/// The number and title a file name supplies (R-P5: "SOG-204 Mayday.pdf" → SOG-204, "Mayday"). A name without a leading
		/// number gives the whole name as the title.
		/// </summary>
		public static (string Number, string Title) ParseFileName(string fileName)
		{
			var stem = Path.GetFileNameWithoutExtension(SafeFileName(fileName)).Replace('_', ' ').Trim();
			if (stem.Length == 0)
				return (null, "Document");

			try
			{
				var match = NumberAndTitle.Match(stem);
				if (match.Success)
				{
					var number = match.Groups["number"].Value.Replace('_', '-').Replace(' ', '-').ToUpperInvariant();
					var title = match.Groups["title"].Value.Trim(' ', '-', '_', '–');
					if (title.Length > 0)
						return (Truncate(number, DispatchProtocol.NumberMaxLength), Truncate(title, 100));
				}
			}
			catch (RegexMatchTimeoutException)
			{
				// A pathological name falls back to the whole name as the title.
			}

			return (null, Truncate(stem, 100));
		}

		/// <summary>
		/// The code a document-backed protocol gets: the file's number when it has one (fits <see cref="DispatchProtocol.CodeMaxLength"/>),
		/// else letters and digits of the title.
		/// </summary>
		public static string CodeFor(string number, string title)
		{
			var source = !string.IsNullOrWhiteSpace(number) ? number : title ?? "DOC";
			var code = new string(source.ToUpperInvariant().Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray());
			if (code.Length == 0)
				code = "DOC";
			return code.Length > DispatchProtocol.CodeMaxLength ? code.Substring(0, DispatchProtocol.CodeMaxLength) : code;
		}

		/// <summary>
		/// The fixed HTML a document-backed version stores as ContentHtml (and so as DispatchProtocols.ProtocolText once published): the
		/// title and the file's name, so a reader that predates documents (the older apps, call snapshots) shows what the protocol is
		/// and where to open it. Never the extracted text: that arrives later and must not change a version that left draft.
		/// </summary>
		public static string StubHtml(string title, string fileName)
		{
			var t = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(title) ? "Document" : title.Trim());
			var f = WebUtility.HtmlEncode(SafeFileName(fileName));
			return "<p><strong>" + t + "</strong></p><p>Document: " + f + "</p>";
		}

		/// <summary>Plain text of a page made safe to store: control characters out, whitespace runs folded, capped.</summary>
		public static string CleanPageText(string text, int max)
		{
			if (string.IsNullOrEmpty(text))
				return string.Empty;

			var builder = new StringBuilder(Math.Min(text.Length, max));
			var lastSpace = false;
			foreach (var ch in text)
			{
				if (builder.Length >= max)
					break;
				if (ch == '\n')
				{
					builder.Append('\n');
					lastSpace = true;
					continue;
				}
				if (char.IsWhiteSpace(ch) || char.IsControl(ch) || char.GetUnicodeCategory(ch) == UnicodeCategory.Format)
				{
					if (!lastSpace)
						builder.Append(' ');
					lastSpace = true;
					continue;
				}
				builder.Append(ch);
				lastSpace = false;
			}

			return builder.ToString().Trim();
		}

		/// <summary>Counts letters and digits (the scanned-PDF test).</summary>
		public static int CountLetters(IEnumerable<string> pages) => (pages ?? Enumerable.Empty<string>()).Sum(p => (p ?? string.Empty).Count(char.IsLetterOrDigit));

		private static string Truncate(string value, int max) => string.IsNullOrEmpty(value) || value.Length <= max ? value : value.Substring(0, max);
	}
}
