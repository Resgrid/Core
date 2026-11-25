using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>Primary files of document-backed protocol versions (M0353). Metadata reads never select the bytes.</summary>
	public interface IProtocolVersionFilesRepository : IRepository<ProtocolVersionFile>
	{
		/// <summary>The version's file without its bytes; null when the version has none.</summary>
		Task<ProtocolVersionFile> GetMetadataForVersionAsync(int departmentId, int protocolVersionId);

		/// <summary>The files of the given versions, without bytes.</summary>
		Task<List<ProtocolVersionFile>> GetMetadataForVersionsAsync(int departmentId, IEnumerable<int> protocolVersionIds);

		/// <summary>The version's file with its bytes (the file endpoint and hashing only).</summary>
		Task<ProtocolVersionFile> GetWithDataForVersionAsync(int departmentId, int protocolVersionId);

		Task<ProtocolVersionFile> GetWithDataByIdAsync(int departmentId, int protocolVersionFileId);

		/// <summary>Removes the version's file (a draft's file being replaced; never a version that left draft).</summary>
		Task<int> DeleteForVersionAsync(int departmentId, int protocolVersionId, CancellationToken cancellationToken = default);

		/// <summary>Copies a version's file to another version of the same protocol server-side (a new draft from a document version).</summary>
		Task<ProtocolVersionFile> CopyToVersionAsync(int departmentId, int fromVersionId, int toVersionId, int toProtocolId, string userId, DateTime utcNow,
			CancellationToken cancellationToken = default);
	}

	/// <summary>File text extractions and their pages (M0353).</summary>
	public interface IProtocolFileTextsRepository : IRepository<ProtocolFileText>
	{
		Task<ProtocolFileText> GetForSourceAsync(int departmentId, int sourceKind, int sourceId);

		/// <summary>Every extraction of a protocol (its versions' files and its attachments).</summary>
		Task<List<ProtocolFileText>> GetForProtocolAsync(int departmentId, int protocolId);

		Task<List<ProtocolFileTextPage>> GetPagesAsync(int departmentId, int protocolFileTextId);

		/// <summary>
		/// The extracted text of the version in force's primary file and of the protocol's attachments, joined, capped at
		/// <paramref name="maxChars"/> (the search projection). Empty when nothing was extracted.
		/// </summary>
		Task<string> GetSearchTextAsync(int departmentId, int protocolId, int? currentVersionId, int maxChars);

		/// <summary>Replaces the extraction's pages and records its outcome in one transaction.</summary>
		Task SaveResultAsync(ProtocolFileText text, IReadOnlyList<string> pages, CancellationToken cancellationToken = default);

		/// <summary>Queued extractions, oldest first, across departments (worker 86; due work only).</summary>
		Task<List<ProtocolFileText>> GetPendingAsync(int take);

		/// <summary>
		/// PDF and DOCX attachments that have no extraction row yet, across departments (worker 86 queues them). Selects metadata only.
		/// </summary>
		Task<List<ProtocolAttachmentTextCandidate>> GetAttachmentsWithoutTextAsync(int take);

		/// <summary>Claims a pending row for one attempt (two workers never extract the same file at once).</summary>
		Task<bool> TryClaimAsync(int protocolFileTextId, int expectedAttempts, CancellationToken cancellationToken = default);

		/// <summary>Copies an extraction (with its pages) to another version's file (a new draft copied from a document version).</summary>
		Task CopyAsync(ProtocolFileText from, int toSourceId, int toVersionId, DateTime utcNow, CancellationToken cancellationToken = default);

		/// <summary>Removes the extraction of a source (a replaced draft file).</summary>
		Task<int> DeleteForSourceAsync(int departmentId, int sourceKind, int sourceId, CancellationToken cancellationToken = default);
	}

	/// <summary>A PDF or DOCX attachment of a live protocol that has no extraction row yet (worker 86 queues it).</summary>
	public sealed class ProtocolAttachmentTextCandidate
	{
		public int DispatchProtocolAttachmentId { get; set; }
		public int DispatchProtocolId { get; set; }
		public int DepartmentId { get; set; }
		public string FileName { get; set; }
		public string FileType { get; set; }
	}

	/// <summary>A department with outstanding acknowledgments owing a reminder (worker 86 starts from these, never from a sweep).</summary>
	public sealed class ProtocolAcknowledgmentDue
	{
		public int ProtocolAcknowledgmentId { get; set; }
		public int DepartmentId { get; set; }
		public int DispatchProtocolId { get; set; }
		public int ProtocolVersionId { get; set; }
		public string UserId { get; set; }
		public DateTime? DueOn { get; set; }
		public int ReminderStage { get; set; }
	}

	/// <summary>Protocol acknowledgments (M0354). Every read starts at the department.</summary>
	public interface IProtocolAcknowledgmentsRepository : IRepository<ProtocolAcknowledgment>
	{
		Task<ProtocolAcknowledgment> GetByIdForDepartmentAsync(int departmentId, int protocolAcknowledgmentId);

		Task<ProtocolAcknowledgment> GetForVersionAndUserAsync(int departmentId, int protocolVersionId, string userId);

		Task<List<ProtocolAcknowledgment>> GetForVersionAsync(int departmentId, int protocolVersionId);

		Task<List<ProtocolAcknowledgment>> GetForProtocolAsync(int departmentId, int protocolId);

		Task<List<ProtocolAcknowledgment>> GetForUserAsync(int departmentId, string userId);

		/// <summary>The department's rows (the compliance overview; one read).</summary>
		Task<List<ProtocolAcknowledgment>> GetForDepartmentAsync(int departmentId);

		/// <summary>Moves a row's status only while it is still <paramref name="expectedStatus"/> (two signers at once: one wins).</summary>
		Task<bool> TryUpdateStatusAsync(ProtocolAcknowledgment acknowledgment, int expectedStatus, CancellationToken cancellationToken = default);

		/// <summary>Open rows of the version's protocol on other versions (superseded by the version now in force).</summary>
		Task<int> SupersedeOpenAsync(int departmentId, int protocolId, int exceptVersionId, DateTime utcNow, CancellationToken cancellationToken = default);

		/// <summary>Withdraws every open row of a protocol (retired, a bulletin withdrawn).</summary>
		Task<int> WithdrawOpenForProtocolAsync(int departmentId, int protocolId, DateTime utcNow, CancellationToken cancellationToken = default);

		/// <summary>Open rows owing a reminder stage on <paramref name="utcNow"/>, across departments, oldest due first.</summary>
		Task<List<ProtocolAcknowledgmentDue>> GetRemindersDueAsync(DateTime utcNow, int take);

		/// <summary>Records the stage sent, only if the row is still open below that stage (a second run never sends it twice).</summary>
		Task<bool> MarkReminderAsync(int departmentId, int protocolAcknowledgmentId, int stage, DateTime utcNow, CancellationToken cancellationToken = default);

		/// <summary>Departments with rows waiting for a quiz pass (worker 86), and the rows.</summary>
		Task<List<ProtocolAcknowledgment>> GetAwaitingQuizAsync(int take);

		/// <summary>The member's open or signed bulletin rows whose bulletin is in force (the must-read card).</summary>
		Task<List<ProtocolAcknowledgment>> GetOpenBulletinsForUserAsync(int departmentId, string userId);
	}
}
