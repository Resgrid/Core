using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Repositories;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// The one read rule for protocols (R-P4), applied by every read path: who the reader is (<see cref="ProtocolReader"/>) and
	/// which protocols they may read (<see cref="ProtocolAccessRules"/>). MCP has no protocol tools today; any it gains must
	/// filter through here too.
	/// </summary>
	public interface IProtocolAccessService
	{
		/// <summary>The reader's rights and memberships in the department (one read per request).</summary>
		Task<ProtocolReader> GetReaderAsync(int departmentId, string userId);

		/// <summary>The protocols the reader may read, in the order given; audiences are read once for the department.</summary>
		Task<List<DispatchProtocol>> FilterReadableAsync(ProtocolReader reader, IEnumerable<DispatchProtocol> protocols);

		Task<bool> CanReadAsync(ProtocolReader reader, DispatchProtocol protocol);

		/// <summary>The protocol (header) when the user may read it, else null.</summary>
		Task<DispatchProtocol> GetReadableAsync(int departmentId, string userId, int protocolId);
	}

	/// <summary>What a new protocol starts with (the creation wizard, v4 SaveProtocol, import, copy).</summary>
	public sealed class ProtocolCreateRequest
	{
		public int DepartmentId { get; set; }
		public DispatchProtocol Protocol { get; set; }
		public string ContentHtml { get; set; }

		/// <summary>A P3 block document (ContentJson); when set it is the content and <see cref="ContentHtml"/> is ignored.</summary>
		public string ContentJson { get; set; }

		/// <summary>
		/// Start the draft as a block document (P3: the block editor replaces the HTML editor for new drafts): <see cref="ContentHtml"/>,
		/// if any, is converted to blocks. Off for v4 SaveProtocol, import and copy, which keep their content's shape.
		/// </summary>
		public bool AsBlocks { get; set; }

		public bool? RequiresAcknowledgment { get; set; }
		public string Locale { get; set; }
		public List<ContentAudienceRule> Audience { get; set; } = new List<ContentAudienceRule>();
		public string UserId { get; set; }
	}

	/// <summary>A draft save from the editor (autosave included).</summary>
	public sealed class ProtocolDraftInput
	{
		public string ContentHtml { get; set; }

		/// <summary>The block editor's document (P3). When set it is the draft's content and <see cref="ContentHtml"/> is ignored.</summary>
		public string ContentJson { get; set; }

		/// <summary>
		/// P4: the case flow builder's decision graph (<see cref="ProtocolDecisionGraph"/> JSON) for a Dispatch protocol. Null keeps the draft's
		/// own graph (editors that do not edit graphs never drop it); blank or a graph without steps removes it.
		/// </summary>
		public string DecisionGraphJson { get; set; }
		public string ChangeSummary { get; set; }
		public bool RequiresAcknowledgment { get; set; }
		public string Locale { get; set; }
		/// <summary>The draft's RowVersion the editor loaded; a different stored value means someone else saved since.</summary>
		public long ExpectedRowVersion { get; set; }
	}

	/// <summary>The outcome of a lifecycle step: the changed version, or why it was refused (with who changed it, for a stale save).</summary>
	public sealed class ProtocolLifecycleResult
	{
		public ProtocolLifecycleErrors Error { get; set; }
		public ProtocolVersion Version { get; set; }
		public DispatchProtocol Protocol { get; set; }
		public string ConflictUserId { get; set; }
		public DateTime? ConflictOn { get; set; }
		public SignatureRecord Signature { get; set; }
		public bool Succeeded => Error == ProtocolLifecycleErrors.None;

		public static ProtocolLifecycleResult Fail(ProtocolLifecycleErrors error) => new ProtocolLifecycleResult { Error = error };
	}

	/// <summary>The library fields of a protocol (identity, not content).</summary>
	public sealed class ProtocolLibraryFields
	{
		public int Kind { get; set; }
		public int? CategoryId { get; set; }
		public string Number { get; set; }
		public string Tags { get; set; }
		public string OwnerUserId { get; set; }
		public int? ReviewIntervalDays { get; set; }
		public bool ExcludeFromAi { get; set; }
		public bool IsLicensedContent { get; set; }
		public bool LegalHold { get; set; }
	}

	/// <summary>A version in the approver's review queue with the step the reader may decide.</summary>
	public sealed class ProtocolReviewItem
	{
		public DispatchProtocol Protocol { get; set; }
		public ProtocolVersion Version { get; set; }
		public ProtocolVersionApproval Step { get; set; }
		public int StepsTotal { get; set; }
		public bool CanDecide { get; set; }
		public int OpenComments { get; set; }
	}

	/// <summary>
	/// The protocol library and lifecycle (P1), built as the shared content core (§12.1): versions (draft → submit → approval
	/// steps → publish now or on an effective date → supersede), approvals signed at L2 and bound to the content hash, audiences,
	/// review cycles and stamps, comments and suggestions, retention, categories, favorites, and the JSON exchange format.
	/// </summary>
	public interface IProtocolLibraryService
	{
		// ---- reading ---------------------------------------------------------------------------------------------------------

		Task<List<ProtocolCategory>> GetCategoriesAsync(int departmentId);

		Task<List<ProtocolVersion>> GetVersionsAsync(int departmentId, int protocolId, bool withContent = false);

		Task<ProtocolVersion> GetVersionAsync(int departmentId, int protocolVersionId);

		/// <summary>Every version header of the department (library tiles and rows).</summary>
		Task<List<ProtocolVersion>> GetDepartmentVersionHeadersAsync(int departmentId);

		/// <summary>The protocol's open working copy (Draft, InReview or Approved and not yet published), if any.</summary>
		Task<ProtocolVersion> GetOpenDraftAsync(int departmentId, int protocolId);

		Task<List<ProtocolAudience>> GetAudienceAsync(int departmentId, int protocolId);

		Task<List<ProtocolAudience>> GetDepartmentAudiencesAsync(int departmentId);

		Task<List<ProtocolVersionApproval>> GetApprovalsAsync(int departmentId, int protocolVersionId);

		Task<List<ProtocolComment>> GetCommentsAsync(int departmentId, int protocolId);

		/// <summary>The versions waiting for approval, with the step the reader may decide marked.</summary>
		Task<List<ProtocolReviewItem>> GetReviewQueueAsync(ProtocolReader reader);

		Task<ProtocolDiffResult> CompareAsync(int departmentId, int fromVersionId, int toVersionId);

		Task<ProtocolLibrarySettings> GetSettingsAsync(int departmentId);

		Task<ProtocolLibrarySettings> SaveSettingsAsync(int departmentId, ProtocolLibrarySettings settings, string userId, CancellationToken cancellationToken = default);

		Task<List<ProtocolCallUsage>> GetRecentCallsAsync(int departmentId, int protocolId, int take = 20);

		// ---- authoring -------------------------------------------------------------------------------------------------------

		/// <summary>Creates the protocol (Status Draft) with its first draft version and audience. Nothing is published.</summary>
		Task<ProtocolLifecycleResult> CreateAsync(ProtocolCreateRequest request, CancellationToken cancellationToken = default);

		/// <summary>Opens a new draft from the version in force (or blank). Refused when an open draft exists.</summary>
		Task<ProtocolLifecycleResult> StartDraftAsync(ProtocolReader reader, int protocolId, CancellationToken cancellationToken = default);

		/// <summary>Saves the draft's content. Refused (StaleCopy, with who and when) when the draft changed since it was loaded.</summary>
		Task<ProtocolLifecycleResult> SaveDraftAsync(ProtocolReader reader, int protocolId, ProtocolDraftInput input, CancellationToken cancellationToken = default);

		/// <summary>Saves the library fields; refused (StaleCopy) when the protocol changed since <paramref name="expectedRowVersion"/>.</summary>
		Task<ProtocolLifecycleResult> UpdateLibraryFieldsAsync(ProtocolReader reader, int protocolId, ProtocolLibraryFields fields, long expectedRowVersion, CancellationToken cancellationToken = default);

		Task<ProtocolLifecycleResult> SetAudienceAsync(ProtocolReader reader, int protocolId, IEnumerable<IContentAudienceRule> rules, CancellationToken cancellationToken = default);

		/// <summary>
		/// "Convert to blocks" (P3): the open draft's P1 html becomes a block document. Refused for a document-backed or locked draft and
		/// when the draft changed since <paramref name="expectedRowVersion"/>. Published versions are never converted.
		/// </summary>
		Task<ProtocolLifecycleResult> ConvertDraftToBlocksAsync(ProtocolReader reader, int protocolId, long expectedRowVersion, CancellationToken cancellationToken = default);

		/// <summary>Discards a draft that was never submitted. Anything submitted or published is retained (R-P10).</summary>
		Task<ProtocolLifecycleResult> DiscardDraftAsync(ProtocolReader reader, int protocolVersionId, CancellationToken cancellationToken = default);

		/// <summary>"Start from template": a new draft protocol copied from one the reader can read.</summary>
		Task<ProtocolLifecycleResult> CopyAsync(ProtocolReader reader, int protocolId, CancellationToken cancellationToken = default);

		// ---- review and publish ----------------------------------------------------------------------------------------------

		/// <summary>Draft → InReview, opening the approval steps (the category's, or one step for any approver).</summary>
		Task<ProtocolLifecycleResult> SubmitAsync(ProtocolReader reader, int protocolVersionId, string changeSummary, CancellationToken cancellationToken = default);

		/// <summary>Back to draft by its author or an approver before it is decided; open steps are withdrawn.</summary>
		Task<ProtocolLifecycleResult> WithdrawAsync(ProtocolReader reader, int protocolVersionId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Approves the reader's step with an L2 signature bound to the version's content hash. Refused while review comments are
		/// open, without step-up or hash-bound passkey proof, or when the reader already approved another step of this round.
		/// The last step moves the version to Approved.
		/// </summary>
		Task<ProtocolLifecycleResult> ApproveAsync(ProtocolReader reader, int protocolVersionId, string comment, SignatureProof proof, string statementText = null,
			CancellationToken cancellationToken = default);

		/// <summary>Rejects the version (with a comment) back to Draft; the other open steps are withdrawn.</summary>
		Task<ProtocolLifecycleResult> RejectAsync(ProtocolReader reader, int protocolVersionId, string comment, CancellationToken cancellationToken = default);

		/// <summary>
		/// Publishes an approved version now, or schedules it for <paramref name="effectiveOnUtc"/> (worker 84 activates it). The
		/// change summary is required; major or minor is chosen here. Every approval signature must still match the content.
		/// </summary>
		Task<ProtocolLifecycleResult> PublishAsync(ProtocolReader reader, int protocolVersionId, bool major, string changeSummary, DateTime? effectiveOnUtc,
			DateTime? expiresOnUtc, CancellationToken cancellationToken = default);

		/// <summary>"Reviewed, no change" (R-P9): an L2-signed stamp on the version in force; resets NextReviewOn, no new version.</summary>
		Task<ProtocolLifecycleResult> RecordReviewAsync(ProtocolReader reader, int protocolId, SignatureProof proof, string statementText = null,
			CancellationToken cancellationToken = default);

		/// <summary>Retires the protocol: its version in force becomes Retired; the protocol stays for history and the calls that cite it.</summary>
		Task<ProtocolLifecycleResult> RetireAsync(ProtocolReader reader, int protocolId, CancellationToken cancellationToken = default);

		// ---- comments and suggestions (R-P8) ---------------------------------------------------------------------------------

		Task<ProtocolComment> AddCommentAsync(ProtocolReader reader, int protocolId, int? protocolVersionId, ProtocolCommentKinds kind, string anchor, string body,
			int? parentCommentId, CancellationToken cancellationToken = default);

		Task<ProtocolComment> ResolveCommentAsync(ProtocolReader reader, int protocolCommentId, CancellationToken cancellationToken = default);

		// ---- categories and member conveniences ------------------------------------------------------------------------------

		Task<ProtocolCategory> SaveCategoryAsync(int departmentId, ProtocolCategory category, string userId, CancellationToken cancellationToken = default);

		/// <summary>Deletes a category no protocol and no child category uses; false otherwise.</summary>
		Task<bool> DeleteCategoryAsync(int departmentId, int protocolCategoryId, string userId, CancellationToken cancellationToken = default);

		Task SetFavoriteAsync(int departmentId, string userId, int protocolId, bool favorite, CancellationToken cancellationToken = default);

		Task RecordViewAsync(int departmentId, string userId, int protocolId, CancellationToken cancellationToken = default);

		/// <summary>The member's favorite protocol ids and recently opened ones (newest first).</summary>
		Task<(List<int> Favorites, List<int> Recent)> GetFavoritesAndRecentAsync(int departmentId, string userId);

		// ---- exchange --------------------------------------------------------------------------------------------------------

		Task<ProtocolExchangeDocument> ExportAsync(ProtocolReader reader, IEnumerable<int> protocolIds);

		/// <summary>Imports protocols as drafts (never published). Names are matched to the department's priorities, types, groups and roles.</summary>
		Task<ProtocolImportResult> ImportAsync(ProtocolReader reader, ProtocolExchangeDocument document, CancellationToken cancellationToken = default);

		/// <summary>
		/// The same import as a dry run (the department import wizard's preview): every check and name match runs, nothing is written.
		/// <see cref="ProtocolImportResult.Created"/> is how many would be created.
		/// </summary>
		Task<ProtocolImportResult> PreviewImportAsync(ProtocolReader reader, ProtocolExchangeDocument document);

		// ---- worker 84 -------------------------------------------------------------------------------------------------------

		/// <summary>Publishes approved versions whose effective date has come. Returns how many.</summary>
		Task<int> ActivateDueVersionsAsync(DateTime utcNow, CancellationToken cancellationToken = default);

		/// <summary>Retires published versions past their expiry. Returns how many.</summary>
		Task<int> ExpireDueVersionsAsync(DateTime utcNow, CancellationToken cancellationToken = default);

		/// <summary>Sends the owner's review reminders at T-30, T-7 and when overdue. Returns how many were sent.</summary>
		Task<int> SendReviewRemindersAsync(DateTime utcNow, CancellationToken cancellationToken = default);

		/// <summary>Purges superseded and retired versions past their kind's retention (never on hold, never cited). Returns how many.</summary>
		Task<int> PurgeExpiredRetentionAsync(DateTime utcNow, CancellationToken cancellationToken = default);
	}
}
