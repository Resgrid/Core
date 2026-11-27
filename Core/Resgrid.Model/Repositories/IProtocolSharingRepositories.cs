using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>Attribution kept on content from outside the department (Protocols P5, M0388).</summary>
	public interface IContentAttributionsRepository : IRepository<ContentAttribution>
	{
		/// <summary>The subject's attribution, or null.</summary>
		Task<ContentAttribution> GetForSubjectAsync(int departmentId, ContentAttributionSubjects subject, int subjectId);

		/// <summary>The attributions of several subjects of one kind (one read).</summary>
		Task<List<ContentAttribution>> GetForSubjectsAsync(int departmentId, ContentAttributionSubjects subject, IEnumerable<int> subjectIds);

		/// <summary>Writes the subject's attribution, replacing any it had.</summary>
		Task<ContentAttribution> SaveForSubjectAsync(ContentAttribution attribution, CancellationToken cancellationToken = default);
	}

	/// <summary>Opt-in public pages (Protocols P5, M0388).</summary>
	public interface IProtocolPublicLinksRepository : IRepository<ProtocolPublicLink>
	{
		/// <summary>The link with this token (any department, revoked included: the caller decides); null for an unknown token.</summary>
		Task<ProtocolPublicLink> GetByTokenAsync(string token);

		Task<ProtocolPublicLink> GetByIdForDepartmentAsync(int departmentId, int protocolPublicLinkId);

		/// <summary>The department's links, live first then revoked, newest first.</summary>
		Task<List<ProtocolPublicLink>> GetForDepartmentAsync(int departmentId, bool includeRevoked);

		/// <summary>Revokes a live link once; false when it was already revoked or is not the department's.</summary>
		Task<bool> TryRevokeAsync(int departmentId, int protocolPublicLinkId, string userId, DateTime revokedOnUtc, CancellationToken cancellationToken = default);
	}

	/// <summary>Zero-result protocol searches (Protocols P5 analytics, M0390).</summary>
	public interface IProtocolSearchMissesRepository : IRepository<ProtocolSearchMiss>
	{
		/// <summary>Counts one miss for the (department, surface, day, normalized query) row, creating it on the first miss.</summary>
		Task RecordAsync(int departmentId, ProtocolSearchSurfaces surface, string normalizedQuery, DateTime utcNow, CancellationToken cancellationToken = default);

		/// <summary>The most frequent misses in the period (summed over days), most first.</summary>
		Task<List<ProtocolSearchMissSummary>> GetTopAsync(int departmentId, DateTime fromUtc, DateTime toUtc, int take);
	}

	/// <summary>The call rows the protocol analytics read (Protocols P5).</summary>
	public interface IProtocolAnalyticsRepository
	{
		/// <summary>
		/// The department's call-protocol rows of calls logged in [from, to), at most <paramref name="take"/> (newest first), with the step
		/// log for time on protocol. Only the department's own protocols' rows.
		/// </summary>
		Task<List<ProtocolCallUsageRow>> GetCallUsageAsync(int departmentId, DateTime fromUtc, DateTime toUtc, int take);
	}

	/// <summary>Community exchange listings (Protocols P5 / Training T5, M0389).</summary>
	public interface IExchangeListingsRepository : IRepository<ExchangeListing>
	{
		/// <summary>A listing by id without its payload (any department; the caller decides what the reader may see).</summary>
		Task<ExchangeListing> GetHeaderAsync(int exchangeListingId);

		/// <summary>A listing by id with its payload.</summary>
		Task<ExchangeListing> GetWithPayloadAsync(int exchangeListingId);

		/// <summary>Listed items (no payloads) matching the query, and how many match in all.</summary>
		Task<(List<ExchangeListing> Items, int Total)> BrowseAsync(ExchangeBrowseQuery query);

		/// <summary>The department's own submissions (no payloads), newest first.</summary>
		Task<List<ExchangeListing>> GetForDepartmentAsync(int departmentId);

		/// <summary>Submissions waiting for review (no payloads), oldest first.</summary>
		Task<List<ExchangeListing>> GetPendingReviewAsync(int take);

		/// <summary>Moves the listing from <paramref name="expectedStatus"/> to <paramref name="listing"/>.Status with its review fields; false if it moved since.</summary>
		Task<bool> TryUpdateStatusAsync(ExchangeListing listing, int expectedStatus, CancellationToken cancellationToken = default);

		/// <summary>Recomputes the rating counters from the ratings (one statement).</summary>
		Task RefreshRatingAsync(int exchangeListingId, CancellationToken cancellationToken = default);

		Task IncrementImportCountAsync(int exchangeListingId, CancellationToken cancellationToken = default);
	}

	public interface IExchangeRatingsRepository : IRepository<ExchangeRating>
	{
		Task<ExchangeRating> GetForListingAndUserAsync(int exchangeListingId, string userId);
	}

	public interface IExchangeCommentsRepository : IRepository<ExchangeComment>
	{
		/// <summary>The listing's comments, oldest first; hidden ones only when asked (reviewers).</summary>
		Task<List<ExchangeComment>> GetForListingAsync(int exchangeListingId, bool includeHidden);

		Task<ExchangeComment> GetCommentAsync(int exchangeCommentId);

		/// <summary>Hides a visible comment once; false when it was already hidden.</summary>
		Task<bool> TryHideAsync(int exchangeCommentId, string userId, DateTime hiddenOnUtc, CancellationToken cancellationToken = default);
	}

	public interface IExchangeImportsRepository : IRepository<ExchangeImport>
	{
		Task<List<ExchangeImport>> GetForDepartmentAsync(int departmentId);
	}
}
