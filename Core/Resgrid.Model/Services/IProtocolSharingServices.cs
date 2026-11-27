using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Opt-in public pages (Protocols P5, decision 6): a public URL per published protocol (its version in force, or one pinned version) or per
	/// category as a set, with an embed route. Approvers and department admins manage them; anybody can read a live link. Every page is
	/// re-checked on every request (<see cref="ProtocolPublicPages.Eligibility"/>): drafts, audience-restricted and licensed content never show.
	/// </summary>
	public interface IProtocolPublicPageService
	{
		/// <summary>The department's links with what they point at (approvers and admins; empty for anyone else).</summary>
		Task<List<ProtocolPublicLinkView>> GetLinksAsync(ProtocolReader reader, bool includeRevoked);

		/// <summary>Whether a protocol could be public right now, and if not, why.</summary>
		Task<ProtocolPublicEligibility> GetEligibilityAsync(int departmentId, int protocolId);

		Task<ProtocolPublicLinkResult> CreateProtocolLinkAsync(ProtocolReader reader, int protocolId, bool pinVersion, bool embedEnabled, string embedOrigins,
			CancellationToken cancellationToken = default);

		Task<ProtocolPublicLinkResult> CreateCategoryLinkAsync(ProtocolReader reader, int categoryId, bool embedEnabled, string embedOrigins,
			CancellationToken cancellationToken = default);

		/// <summary>Revokes a link at once: the next request for it is a 404.</summary>
		Task<ProtocolPublicLinkResult> RevokeAsync(ProtocolReader reader, int protocolPublicLinkId, CancellationToken cancellationToken = default);

		/// <summary>The public page of a live link (anonymous): null for an unknown or revoked token, or a protocol link whose protocol is no longer eligible.</summary>
		Task<PublicProtocolPage> GetPageAsync(string token, string requestedLocale);

		/// <summary>The embed route's framing policy for a live link (disabled for an unknown or revoked token).</summary>
		Task<PublicProtocolEmbedPolicy> GetEmbedPolicyAsync(string token);
	}

	/// <summary>The Resgrid template library (Protocols P5): browse the curated sets and clone items into the department as drafts.</summary>
	public interface IProtocolTemplateLibraryService
	{
		IReadOnlyList<ProtocolExchangeDocument> GetSets();

		ProtocolExchangeDocument GetSet(string key);

		/// <summary>
		/// Clones the set's items (all when <paramref name="codes"/> is empty) into the department as drafts, each version keeping the set's
		/// attribution (source, licence, set version). Authors only. Nothing is published.
		/// </summary>
		Task<ProtocolImportResult> CloneAsync(ProtocolReader reader, string setKey, IEnumerable<string> codes, CancellationToken cancellationToken = default);

		/// <summary>Every set in a language (round 9C: machine-translated variants, English when the language ships none).</summary>
		IReadOnlyList<ProtocolExchangeDocument> GetSets(string culture);

		/// <summary>
		/// <see cref="CloneAsync(ProtocolReader, string, IEnumerable{string}, CancellationToken)"/> in a language: the drafts carry the set's
		/// text in that language (English when none ships); codes and attribution are the same.
		/// </summary>
		Task<ProtocolImportResult> CloneAsync(ProtocolReader reader, string setKey, IEnumerable<string> codes, string culture, CancellationToken cancellationToken = default);
	}

	/// <summary>
	/// One kind of item the community exchange can carry (protocols and trainings here; LMS skill sheets and lesson plans register their own,
	/// named Lms*, so the free exchange never references an LMS type). Each source builds the stripped payload of a submission and imports a
	/// listing's payload into a department with the listing's attribution kept.
	/// </summary>
	public interface IExchangeItemSource
	{
		ExchangeItemKinds Kind { get; }

		/// <summary>An imported item of this kind needs the LMS add-on to use (decision 7c: LMS types).</summary>
		bool NeedsLms { get; }

		bool CanSubmit(ExchangeActor actor);

		/// <summary>Whether the actor may import this kind (their rights, and the LMS add-on for an LMS kind).</summary>
		Task<bool> CanImportAsync(ExchangeActor actor);

		/// <summary>The department's items of this kind, each with why it cannot be submitted (licensed, not published…) or None.</summary>
		Task<List<ExchangeSourceItem>> GetSubmittableAsync(ExchangeActor actor);

		/// <summary>The stripped, self-contained payload of one item with the scrubber's findings, or why it cannot be submitted.</summary>
		Task<ExchangePreparedItem> PrepareAsync(ExchangeActor actor, string sourceRef);

		/// <summary>Sanitized display HTML of a payload (the listing's preview).</summary>
		string PreviewHtml(string payloadJson);

		/// <summary>Creates the item in the actor's department from a listed payload, keeping <paramref name="attribution"/>; the new item's id, or null.</summary>
		Task<int?> ImportAsync(ExchangeActor actor, ExchangeListing listing, ContentAttribution attribution, CancellationToken cancellationToken = default);
	}

	/// <summary>
	/// The community exchange (Protocols P5 / Training T5, §13a row 42, decision 7c — free for every department): submit (licensed content
	/// refused, department data stripped, phone numbers flagged), Resgrid review before listing, browse, rate, comment, and import-and-modify
	/// with attribution kept.
	/// </summary>
	public interface ICommunityExchangeService
	{
		/// <summary>The kinds this server carries (a kind without a registered source is not offered).</summary>
		IReadOnlyList<ExchangeItemKinds> GetKinds();

		bool CanSubmitAny(ExchangeActor actor);

		Task<List<ExchangeSourceItem>> GetSubmittableAsync(ExchangeActor actor, ExchangeItemKinds kind);

		/// <summary>What a submission would carry (the stripped payload and the findings), without submitting.</summary>
		Task<ExchangeResult> PreviewSubmissionAsync(ExchangeActor actor, ExchangeItemKinds kind, string sourceRef);

		Task<ExchangeResult> SubmitAsync(ExchangeActor actor, ExchangeSubmitRequest request, CancellationToken cancellationToken = default);

		/// <summary>The contributing department takes its listing back (pending or listed).</summary>
		Task<ExchangeResult> WithdrawAsync(ExchangeActor actor, int exchangeListingId, CancellationToken cancellationToken = default);

		Task<(List<ExchangeListing> Items, int Total)> BrowseAsync(ExchangeBrowseQuery query);

		/// <summary>A listing as the actor may see it: listed items for everyone, other statuses for the contributor and reviewers only.</summary>
		Task<ExchangeListingView> GetListingAsync(ExchangeActor actor, int exchangeListingId);

		Task<List<ExchangeListing>> GetMySubmissionsAsync(ExchangeActor actor);

		/// <summary>Submissions waiting for review (Resgrid reviewers only).</summary>
		Task<List<ExchangeListing>> GetReviewQueueAsync(ExchangeActor actor);

		/// <summary>The reviewer lists or rejects a pending submission (a rejection needs a note).</summary>
		Task<ExchangeResult> DecideAsync(ExchangeActor actor, int exchangeListingId, bool approve, string note, CancellationToken cancellationToken = default);

		/// <summary>The reviewer takes a listed item down (with a note).</summary>
		Task<ExchangeResult> RemoveAsync(ExchangeActor actor, int exchangeListingId, string note, CancellationToken cancellationToken = default);

		Task<ExchangeResult> RateAsync(ExchangeActor actor, int exchangeListingId, int stars, CancellationToken cancellationToken = default);

		Task<ExchangeResult> CommentAsync(ExchangeActor actor, int exchangeListingId, string body, CancellationToken cancellationToken = default);

		/// <summary>Hides a comment (its author or a reviewer).</summary>
		Task<ExchangeResult> HideCommentAsync(ExchangeActor actor, int exchangeCommentId, CancellationToken cancellationToken = default);

		/// <summary>Imports a listed item into the actor's department (a draft protocol, a training…) with the listing's attribution kept.</summary>
		Task<ExchangeResult> ImportAsync(ExchangeActor actor, int exchangeListingId, CancellationToken cancellationToken = default);
	}

	/// <summary>
	/// Protocol analytics (P5, free; never needs the LMS): usage by protocol and version, determinant distribution and override rate, time on
	/// protocol, acknowledgment compliance and protocol gaps (zero-result searches and open K1 knowledge gaps).
	/// </summary>
	public interface IProtocolAnalyticsService
	{
		/// <summary>The dashboard for [from, to) (managers: authors, approvers, admins; null for anyone else). The period is capped at a year.</summary>
		Task<ProtocolAnalyticsReport> GetReportAsync(ProtocolReader reader, DateTime fromUtc, DateTime toUtc);

		/// <summary>The dashboard as CSV with the given column headers; the export is audited.</summary>
		Task<string> ExportCsvAsync(ProtocolReader reader, DateTime fromUtc, DateTime toUtc, IReadOnlyList<string> headers);

		/// <summary>Counts a protocol search that found nothing (never fails the search that called it).</summary>
		Task RecordSearchMissAsync(int departmentId, ProtocolSearchSurfaces surface, string query);
	}
}
