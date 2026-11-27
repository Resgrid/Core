using System;
using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>
	/// What an offline client keeps of the library (P3, for Omni): every readable protocol's version in force with its content hash, the
	/// SHA-256 of each file it needs, the approved translations, and a version scheduled to take force. A client compares hashes to
	/// know what to fetch again; nothing here carries content or bytes.
	/// </summary>
	public sealed class ProtocolOfflineManifest
	{
		public int DepartmentId { get; set; }
		public DateTime GeneratedOn { get; set; }
		public string HashAlgorithm { get; set; } = ProtocolContentHash.Algorithm;
		public List<ProtocolOfflineItem> Items { get; set; } = new List<ProtocolOfflineItem>();
	}

	public sealed class ProtocolOfflineItem
	{
		public int ProtocolId { get; set; }
		public string Code { get; set; }
		public string Name { get; set; }
		public int Kind { get; set; }
		public int VersionId { get; set; }
		public string VersionLabel { get; set; }
		public DateTime? EffectiveOn { get; set; }
		public DateTime? ExpiresOn { get; set; }
		/// <summary>The version's content hash (of its HTML, or of its file for a document-backed version).</summary>
		public string ContentHash { get; set; }
		public int ContentKind { get; set; }
		public string Locale { get; set; }
		public bool HasSteps { get; set; }
		public List<ProtocolOfflineFile> Files { get; set; } = new List<ProtocolOfflineFile>();
		public List<ProtocolOfflineTranslation> Translations { get; set; } = new List<ProtocolOfflineTranslation>();
		/// <summary>An approved version waiting for its effective date, so the device can tell the member what is coming.</summary>
		public ProtocolOfflineScheduled Scheduled { get; set; }
	}

	/// <summary>A file a protocol needs offline: an attachment (FileKind "attachment") or a document-backed version's file ("document").</summary>
	public sealed class ProtocolOfflineFile
	{
		public string FileKind { get; set; }
		public int FileId { get; set; }
		public string FileName { get; set; }
		public string ContentType { get; set; }
		public long? Size { get; set; }
		/// <summary>Lower-case hex SHA-256 of the bytes; null only for an attachment stored before M0363 that has not been hashed yet.</summary>
		public string Sha256 { get; set; }
	}

	public sealed class ProtocolOfflineTranslation
	{
		public int TranslationId { get; set; }
		public string Locale { get; set; }
		public string ContentHash { get; set; }
	}

	public sealed class ProtocolOfflineScheduled
	{
		public int VersionId { get; set; }
		public string VersionLabel { get; set; }
		public DateTime? EffectiveOn { get; set; }
	}

	/// <summary>
	/// The answer to "I am showing version X; is it still the one in force?" (P3 offline staleness): "you are viewing v3 — v4 is
	/// effective since …". Omni shows it; the server only answers.
	/// </summary>
	public sealed class ProtocolStaleness
	{
		public int ProtocolId { get; set; }
		public int? ViewedVersionId { get; set; }
		public string ViewedLabel { get; set; }
		public int? CurrentVersionId { get; set; }
		public string CurrentLabel { get; set; }
		public DateTime? CurrentEffectiveOn { get; set; }
		public string CurrentContentHash { get; set; }
		/// <summary>The viewed version is not the one in force (or the hash the device holds differs from it).</summary>
		public bool IsStale { get; set; }
		/// <summary>The protocol has no version in force any more (retired or expired).</summary>
		public bool IsRetired { get; set; }
		/// <summary>An approved version waiting for its effective date.</summary>
		public ProtocolOfflineScheduled Scheduled { get; set; }
		/// <summary>The sentence to show, in the reader's language ("You are viewing v3 — v4 is effective since …"); null when current.</summary>
		public string Message { get; set; }
	}

	/// <summary>One protocol in a PDF book (P3): the version in force and who approved it.</summary>
	public sealed class ProtocolBookEntry
	{
		public DispatchProtocol Protocol { get; set; }
		public ProtocolVersion Version { get; set; }
		public string CategoryName { get; set; }
		public string ApproverNames { get; set; }
		public DateTime? ApprovedOn { get; set; }
		/// <summary>The body to print: sanitized HTML (block or html versions), or a note for a document-backed version.</summary>
		public string BodyHtml { get; set; }
	}

	/// <summary>A PDF book of a category or the whole library: its entries, the book's own hash (the seal binds to it) and the HTML.</summary>
	public sealed class ProtocolBook
	{
		public int DepartmentId { get; set; }
		public int? CategoryId { get; set; }
		public string Title { get; set; }
		public DateTime GeneratedOn { get; set; }
		public List<ProtocolBookEntry> Entries { get; set; } = new List<ProtocolBookEntry>();
		/// <summary>SHA-256 over the ordered (protocol, version, content hash) list: the same versions give the same book hash.</summary>
		public string BookHash { get; set; }
		public string Html { get; set; }
	}

	/// <summary>A book export as the member receives it.</summary>
	public sealed class ProtocolBookExport
	{
		public ProtocolBook Book { get; set; }
		public byte[] Pdf { get; set; }
		public bool Sealed { get; set; }
		public bool SealingPending { get; set; }
		public string FileName { get; set; }
	}
}
