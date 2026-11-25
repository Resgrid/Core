using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Step-through (P3): a Procedure or Dispatch protocol's steps run on a call like a checklist, each step timestamped, the log kept in
	/// CallProtocols.StepLogJson. Incident SOP checklists (MAYDAY, RIT, hazmat entry) are the department's Procedure protocols started on
	/// a call. The caller checks that the user may edit the call (write) or see it (read); this service checks the department.
	/// </summary>
	public interface IProtocolStepThroughService
	{
		/// <summary>The call's protocols that can be stepped through (not removed, Procedure or Dispatch kind), with steps and logs.</summary>
		Task<List<ProtocolCallChecklist>> GetCallChecklistsAsync(int departmentId, int callId);

		/// <summary>One call protocol as a checklist; null when it is not the department's call's.</summary>
		Task<ProtocolCallChecklist> GetCallChecklistAsync(int departmentId, int callId, int callProtocolId);

		/// <summary>The Procedure protocols in force the user may read and start on a call (the incident SOP library).</summary>
		Task<List<DispatchProtocol>> GetSopLibraryAsync(int departmentId, string userId);

		/// <summary>
		/// Starts a protocol on the call: attaches it (Manual) when it is not on the call yet, and opens the log on the version it cites.
		/// Starting one already started returns it unchanged.
		/// </summary>
		Task<ProtocolStepResult> StartAsync(int departmentId, int callId, int protocolId, string userId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Records one step event (done, skipped, not applicable, or back to pending), timestamped by the server. <paramref name="clientStepId"/>
		/// is the device's own id for the event (offline replay, round 5F): an event already in the log under that id is not recorded again.
		/// </summary>
		Task<ProtocolStepResult> RecordStepAsync(int departmentId, int callId, int callProtocolId, string stepId, ProtocolStepStates state, string note,
			DateTime? deviceAtUtc, string userId, CancellationToken cancellationToken = default, string clientStepId = null);

		/// <summary>Finishes the log (steps left pending stay pending, recorded as such).</summary>
		Task<ProtocolStepResult> CompleteAsync(int departmentId, int callId, int callProtocolId, string userId, CancellationToken cancellationToken = default);
	}

	/// <summary>Offline and staleness (P3, for Omni): the library manifest by hash and "v3 viewed, v4 effective since …".</summary>
	public interface IProtocolOfflineService
	{
		/// <summary>The readable protocols in force with their version hash, file SHA-256s, approved translations and scheduled version.</summary>
		Task<ProtocolOfflineManifest> GetManifestAsync(int departmentId, string userId);

		/// <summary>
		/// Whether the version (or content hash) a device shows is still the one in force; null when the protocol is not the reader's to
		/// read. <paramref name="viewedContentHash"/> is optional: a device that holds a hash can send it instead of (or with) the id.
		/// </summary>
		Task<ProtocolStaleness> GetStalenessAsync(int departmentId, string userId, int protocolId, int? viewedVersionId, string viewedContentHash = null);
	}

	/// <summary>The words a PDF book prints around the content, in the requester's language.</summary>
	public sealed class ProtocolBookText
	{
		public string LibraryTitle { get; set; } = "Protocol library";
		public string Contents { get; set; } = "Contents";
		public string Version { get; set; } = "Version";
		public string EffectiveOn { get; set; } = "Effective";
		public string ApprovedBy { get; set; } = "Approved by";
		public string ContentHash { get; set; } = "Content";
		public string GeneratedOn { get; set; } = "Generated";
		public string DocumentBacked { get; set; } = "This protocol is a document; open it in Resgrid to read the file.";
		public string Empty { get; set; } = "No published protocols.";
		public string Uncategorized { get; set; } = "Uncategorized";
		public string DosingToolLink { get; set; } = "Open the department's dosing tool";
	}

	/// <summary>The PDF book (P3): a category or the whole library, the versions in force, sealed when sealing is configured.</summary>
	public interface IProtocolBookService
	{
		/// <summary>The book the user may read: the readable protocols in force, by category then number/code, with the HTML to print.</summary>
		Task<ProtocolBook> BuildAsync(int departmentId, string userId, int? categoryId, ProtocolBookText text);

		/// <summary>
		/// The book as a PDF through the L3 sealing service: sealed when sealing is configured, otherwise the unsealed rendering recorded
		/// "sealing pending" (fails closed, as the single-version PDF does). The export is audited.
		/// </summary>
		Task<ProtocolBookExport> ExportPdfAsync(int departmentId, string userId, int? categoryId, ProtocolBookText text, string culture, string pageSize,
			CancellationToken cancellationToken = default);
	}

	/// <summary>Content translations (P3): a locale variant per version with its own L2 approval; the source hash never changes.</summary>
	public interface IProtocolTranslationService
	{
		Task<List<ProtocolVersionTranslation>> GetForVersionAsync(int departmentId, int protocolVersionId);

		Task<ProtocolVersionTranslation> GetAsync(int departmentId, int protocolVersionTranslationId);

		/// <summary>Starts a draft translation of a locked version, prefilled with the source wording (authors).</summary>
		Task<ProtocolTranslationResult> StartAsync(ProtocolReader reader, int protocolVersionId, string locale, CancellationToken cancellationToken = default);

		/// <summary>Saves the draft's wording; the structure, doses and links stay the source's (authors).</summary>
		Task<ProtocolTranslationResult> SaveAsync(ProtocolReader reader, int protocolVersionTranslationId, string contentJson, string contentHtml,
			long expectedRowVersion, CancellationToken cancellationToken = default);

		/// <summary>Approves the translation with an L2 signature bound to its own hash (approvers).</summary>
		Task<ProtocolTranslationResult> ApproveAsync(ProtocolReader reader, int protocolVersionTranslationId, SignatureProof proof, string statementText = null,
			CancellationToken cancellationToken = default);

		/// <summary>Takes a translation out of use; readers fall back to the source language (approvers).</summary>
		Task<ProtocolTranslationResult> WithdrawAsync(ProtocolReader reader, int protocolVersionTranslationId, CancellationToken cancellationToken = default);

		/// <summary>
		/// What a reader asking for <paramref name="requestedLocale"/> is shown: the approved translation made from the version's current
		/// content, else the source version (the department's language).
		/// </summary>
		Task<ProtocolLocalizedContent> GetLocalizedAsync(int departmentId, ProtocolVersion version, string requestedLocale);
	}
}
