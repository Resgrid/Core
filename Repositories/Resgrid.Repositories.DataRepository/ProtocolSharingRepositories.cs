using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	/// <summary>Attribution kept on content from outside the department (Protocols P5, M0388). One row per subject.</summary>
	public class ContentAttributionsRepository : RmsRepositoryBase<ContentAttribution>, IContentAttributionsRepository
	{
		public ContentAttributionsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<ContentAttribution> GetForSubjectAsync(int departmentId, ContentAttributionSubjects subject, int subjectId)
		{
			return QueryFirstOrDefaultAsync<ContentAttribution>(
				$"SELECT * FROM {Tbl("ContentAttributions")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("SubjectKind")} = {P}Kind AND {Col("SubjectId")} = {P}SubjectId",
				new { DepartmentId = departmentId, Kind = (int)subject, SubjectId = subjectId });
		}

		public async Task<List<ContentAttribution>> GetForSubjectsAsync(int departmentId, ContentAttributionSubjects subject, IEnumerable<int> subjectIds)
		{
			var ids = InListValue(subjectIds?.Distinct());
			if (ids.Length == 0)
				return new List<ContentAttribution>();
			return (await QueryAsync<ContentAttribution>(
				$"SELECT * FROM {Tbl("ContentAttributions")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("SubjectKind")} = {P}Kind AND {InList("SubjectId", "Ids")}",
				new { DepartmentId = departmentId, Kind = (int)subject, Ids = ids })).ToList();
		}

		public async Task<ContentAttribution> SaveForSubjectAsync(ContentAttribution attribution, CancellationToken cancellationToken = default)
		{
			attribution.Normalize();
			var existing = await GetForSubjectAsync(attribution.DepartmentId, (ContentAttributionSubjects)attribution.SubjectKind, attribution.SubjectId);
			if (existing != null)
			{
				attribution.ContentAttributionId = existing.ContentAttributionId;
				return await UpdateAsync(attribution, cancellationToken, true);
			}

			try
			{
				return await InsertAsync(attribution, cancellationToken, true);
			}
			catch (Exception ex) when (IsUniqueViolation(ex))
			{
				// Two writers at once: the other row stands; write ours over it.
				var winner = await GetForSubjectAsync(attribution.DepartmentId, (ContentAttributionSubjects)attribution.SubjectKind, attribution.SubjectId);
				if (winner == null)
					throw;
				attribution.ContentAttributionId = winner.ContentAttributionId;
				return await UpdateAsync(attribution, cancellationToken, true);
			}
		}
	}

	/// <summary>Opt-in public pages (Protocols P5, M0388). Tokens are compared exactly (binary collation / varchar).</summary>
	public class ProtocolPublicLinksRepository : RmsRepositoryBase<ProtocolPublicLink>, IProtocolPublicLinksRepository
	{
		public ProtocolPublicLinksRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<ProtocolPublicLink> GetByTokenAsync(string token)
		{
			if (!ProtocolPublicPages.IsTokenShape(token))
				return Task.FromResult<ProtocolPublicLink>(null);
			var parameters = new DynamicParameters();
			parameters.Add("Token", token, System.Data.DbType.AnsiString, size: ProtocolPublicLink.TokenLength);
			return QueryFirstOrDefaultAsync<ProtocolPublicLink>($"SELECT * FROM {Tbl("ProtocolPublicLinks")} WHERE {Col("Token")} = {P}Token", parameters);
		}

		public Task<ProtocolPublicLink> GetByIdForDepartmentAsync(int departmentId, int protocolPublicLinkId)
		{
			return QueryFirstOrDefaultAsync<ProtocolPublicLink>(
				$"SELECT * FROM {Tbl("ProtocolPublicLinks")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ProtocolPublicLinkId")} = {P}Id",
				new { DepartmentId = departmentId, Id = protocolPublicLinkId });
		}

		public async Task<List<ProtocolPublicLink>> GetForDepartmentAsync(int departmentId, bool includeRevoked)
		{
			return (await QueryAsync<ProtocolPublicLink>(
				$"SELECT * FROM {Tbl("ProtocolPublicLinks")} WHERE {Col("DepartmentId")} = {P}DepartmentId" + (includeRevoked ? string.Empty : $" AND {Col("RevokedOn")} IS NULL") +
				$" ORDER BY CASE WHEN {Col("RevokedOn")} IS NULL THEN 0 ELSE 1 END, {Col("CreatedOn")} DESC, {Col("ProtocolPublicLinkId")} DESC",
				new { DepartmentId = departmentId })).ToList();
		}

		public async Task<bool> TryRevokeAsync(int departmentId, int protocolPublicLinkId, string userId, DateTime revokedOnUtc, CancellationToken cancellationToken = default)
		{
			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", departmentId);
			parameters.Add("Id", protocolPublicLinkId);
			parameters.Add("UserId", userId, System.Data.DbType.String);
			parameters.Add("On", DatabaseTimestamp(revokedOnUtc));
			var changed = await ExecuteAsync(
				$"UPDATE {Tbl("ProtocolPublicLinks")} SET {Col("RevokedOn")} = {P}On, {Col("RevokedByUserId")} = {P}UserId " +
				$"WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ProtocolPublicLinkId")} = {P}Id AND {Col("RevokedOn")} IS NULL",
				parameters, cancellationToken);
			return changed == 1;
		}
	}

	/// <summary>Zero-result protocol searches (Protocols P5 analytics, M0390): counted per department, surface, day and query.</summary>
	public class ProtocolSearchMissesRepository : RmsRepositoryBase<ProtocolSearchMiss>, IProtocolSearchMissesRepository
	{
		public ProtocolSearchMissesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task RecordAsync(int departmentId, ProtocolSearchSurfaces surface, string normalizedQuery, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(normalizedQuery))
				return;

			var parameters = new { DepartmentId = departmentId, Surface = (int)surface, Query = normalizedQuery, Day = DatabaseTimestamp(utcNow.Date), Now = DatabaseTimestamp(utcNow) };
			var update = $"UPDATE {Tbl("ProtocolSearchMisses")} SET {Col("Count")} = {Col("Count")} + 1, {Col("LastSeenOn")} = {P}Now " +
				$"WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("Surface")} = {P}Surface AND {Col("Day")} = {P}Day AND {Col("Query")} = {CaseInsensitive("Query")}";
			if (await ExecuteAsync(update, parameters, cancellationToken) > 0)
				return;

			try
			{
				await ExecuteAsync(
					$"INSERT INTO {Tbl("ProtocolSearchMisses")} ({Cols("DepartmentId", "Surface", "Query", "Day", "Count", "LastSeenOn")}) " +
					$"VALUES ({P}DepartmentId, {P}Surface, {P}Query, {P}Day, 1, {P}Now)", parameters, cancellationToken);
			}
			catch (Exception ex) when (IsUniqueViolation(ex))
			{
				// Another request counted the first miss of the day at the same time.
				await ExecuteAsync(update, parameters, cancellationToken);
			}
		}

		public async Task<List<ProtocolSearchMissSummary>> GetTopAsync(int departmentId, DateTime fromUtc, DateTime toUtc, int take)
		{
			return (await QueryAsync<ProtocolSearchMissSummary>(
				$"SELECT {Col("Query")} AS Query, {Col("Surface")} AS Surface, CAST(SUM({Col("Count")}) AS int) AS Count, MAX({Col("LastSeenOn")}) AS LastSeenOn " +
				$"FROM {Tbl("ProtocolSearchMisses")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("Day")} >= {P}From AND {Col("Day")} < {P}To " +
				$"GROUP BY {Col("Query")}, {Col("Surface")} ORDER BY SUM({Col("Count")}) DESC, MAX({Col("LastSeenOn")}) DESC " +
				(IsPostgres ? "LIMIT @Take" : "OFFSET 0 ROWS FETCH NEXT @Take ROWS ONLY"),
				new { DepartmentId = departmentId, From = DatabaseTimestamp(fromUtc.Date), To = DatabaseTimestamp(toUtc), Take = Math.Clamp(take, 1, 500) })).ToList();
		}
	}

	/// <summary>The call rows the protocol analytics read (Protocols P5): the department's protocols on calls logged in the period.</summary>
	public class ProtocolAnalyticsRepository : RmsRepositoryBase<CallProtocol>, IProtocolAnalyticsRepository
	{
		public ProtocolAnalyticsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<ProtocolCallUsageRow>> GetCallUsageAsync(int departmentId, DateTime fromUtc, DateTime toUtc, int take)
		{
			return (await QueryAsync<ProtocolCallUsageRow>(
				$"SELECT cp.{Col("CallProtocolId")} AS CallProtocolId, cp.{Col("CallId")} AS CallId, cp.{Col("DispatchProtocolId")} AS DispatchProtocolId, " +
				$"cp.{Col("ProtocolVersionId")} AS ProtocolVersionId, cp.{Col("Source")} AS Source, cp.{Col("Trigger")} AS {Col("Trigger")}, " +
				$"cp.{Col("Determinant")} AS Determinant, cp.{Col("OverrideReason")} AS OverrideReason, cp.{Col("AttachedOn")} AS AttachedOn, " +
				$"cp.{Col("RemovedOn")} AS RemovedOn, c.{Col("LoggedOn")} AS LoggedOn, cp.{Col("StepLogJson")} AS StepLogJson " +
				$"FROM {Tbl("CallProtocols")} cp JOIN {Tbl("Calls")} c ON c.{Col("CallId")} = cp.{Col("CallId")} " +
				$"JOIN {Tbl("DispatchProtocols")} p ON p.{Col("DispatchProtocolId")} = cp.{Col("DispatchProtocolId")} " +
				$"WHERE c.{Col("DepartmentId")} = {P}DepartmentId AND p.{Col("DepartmentId")} = {P}DepartmentId " +
				$"AND c.{Col("LoggedOn")} >= {P}From AND c.{Col("LoggedOn")} < {P}To " +
				$"ORDER BY c.{Col("LoggedOn")} DESC, cp.{Col("CallProtocolId")} DESC " + (IsPostgres ? "LIMIT @Take" : "OFFSET 0 ROWS FETCH NEXT @Take ROWS ONLY"),
				new { DepartmentId = departmentId, From = DatabaseTimestamp(fromUtc), To = DatabaseTimestamp(toUtc), Take = Math.Clamp(take, 1, ProtocolAnalytics.MaxCallRows + 1) })).ToList();
		}
	}

	/// <summary>Community exchange listings (Protocols P5 / Training T5, M0389). List reads never select the payload.</summary>
	public class ExchangeListingsRepository : RmsRepositoryBase<ExchangeListing>, IExchangeListingsRepository
	{
		private static readonly string[] HeaderColumns =
		{
			"ExchangeListingId", "DepartmentId", "ItemKind", "SourceRef", "Title", "Summary", "Tags", "VersionLabel", "PayloadHash", "License", "AttributionName",
			"ReviewFlagsJson", "Status", "SubmittedOn", "SubmittedByUserId", "AttestationVersion", "ReviewedOn", "ReviewedByUserId", "ReviewNote", "ListedOn",
			"WithdrawnOn", "RatingCount", "RatingTotal", "ImportCount"
		};

		public ExchangeListingsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		private static string Header(string alias = null) => string.Join(", ", HeaderColumns.Select(c => (alias == null ? string.Empty : alias + ".") + Col(c)));

		public Task<ExchangeListing> GetHeaderAsync(int exchangeListingId)
		{
			return QueryFirstOrDefaultAsync<ExchangeListing>($"SELECT {Header()} FROM {Tbl("ExchangeListings")} WHERE {Col("ExchangeListingId")} = {P}Id", new { Id = exchangeListingId });
		}

		public Task<ExchangeListing> GetWithPayloadAsync(int exchangeListingId)
		{
			return QueryFirstOrDefaultAsync<ExchangeListing>($"SELECT * FROM {Tbl("ExchangeListings")} WHERE {Col("ExchangeListingId")} = {P}Id", new { Id = exchangeListingId });
		}

		public async Task<(List<ExchangeListing> Items, int Total)> BrowseAsync(ExchangeBrowseQuery query)
		{
			query ??= new ExchangeBrowseQuery();
			var parameters = new DynamicParameters();
			parameters.Add("Status", (int)ExchangeListingStatuses.Listed);
			var where = $"WHERE {Col("Status")} = {P}Status";
			if (query.ItemKind.HasValue)
			{
				where += $" AND {Col("ItemKind")} = {P}Kind";
				parameters.Add("Kind", query.ItemKind.Value);
			}
			var search = query.Search?.Trim();
			if (!string.IsNullOrEmpty(search))
			{
				// A literal match in the title, summary or tags (LIKE wildcards in the text are escaped).
				var pattern = "%" + search.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("[", "\\[") + "%";
				where += $" AND ({Col("Title")} LIKE {P}Search ESCAPE '\\' OR {Col("Summary")} LIKE {P}Search ESCAPE '\\' OR {Col("Tags")} LIKE {P}Search ESCAPE '\\')";
				parameters.Add("Search", pattern, System.Data.DbType.String);
			}

			var order = query.Sort switch
			{
				"rating" => $"CASE WHEN {Col("RatingCount")} = 0 THEN 0 ELSE {Col("RatingTotal")} * 100 / {Col("RatingCount")} END DESC, {Col("RatingCount")} DESC",
				"imports" => $"{Col("ImportCount")} DESC",
				_ => $"{Col("ListedOn")} DESC"
			};
			parameters.Add("Skip", Math.Max(0, query.Skip));
			parameters.Add("Take", Math.Clamp(query.Take, 1, 100));

			var total = await ScalarAsync<int>($"SELECT COUNT(*) FROM {Tbl("ExchangeListings")} {where}", parameters);
			var items = (await QueryAsync<ExchangeListing>(
				$"SELECT {Header()} FROM {Tbl("ExchangeListings")} {where} ORDER BY {order}, {Col("ExchangeListingId")} DESC {Paging()}", parameters)).ToList();
			return (items, total);
		}

		public async Task<List<ExchangeListing>> GetForDepartmentAsync(int departmentId)
		{
			return (await QueryAsync<ExchangeListing>(
				$"SELECT {Header()} FROM {Tbl("ExchangeListings")} WHERE {Col("DepartmentId")} = {P}DepartmentId ORDER BY {Col("SubmittedOn")} DESC, {Col("ExchangeListingId")} DESC",
				new { DepartmentId = departmentId })).ToList();
		}

		public async Task<List<ExchangeListing>> GetPendingReviewAsync(int take)
		{
			return (await QueryAsync<ExchangeListing>(
				$"SELECT {Header()} FROM {Tbl("ExchangeListings")} WHERE {Col("Status")} = {P}Status ORDER BY {Col("SubmittedOn")}, {Col("ExchangeListingId")} " +
				(IsPostgres ? "LIMIT @Take" : "OFFSET 0 ROWS FETCH NEXT @Take ROWS ONLY"),
				new { Status = (int)ExchangeListingStatuses.PendingReview, Take = Math.Clamp(take, 1, 500) })).ToList();
		}

		public async Task<bool> TryUpdateStatusAsync(ExchangeListing listing, int expectedStatus, CancellationToken cancellationToken = default)
		{
			var parameters = new DynamicParameters();
			parameters.Add("Id", listing.ExchangeListingId);
			parameters.Add("Expected", expectedStatus);
			parameters.Add("Status", listing.Status);
			parameters.Add("ReviewedOn", listing.ReviewedOn.HasValue ? DatabaseTimestamp(listing.ReviewedOn.Value) : (DateTime?)null, System.Data.DbType.DateTime2);
			parameters.Add("ReviewedBy", listing.ReviewedByUserId, System.Data.DbType.String);
			parameters.Add("Note", listing.ReviewNote, System.Data.DbType.String);
			parameters.Add("ListedOn", listing.ListedOn.HasValue ? DatabaseTimestamp(listing.ListedOn.Value) : (DateTime?)null, System.Data.DbType.DateTime2);
			parameters.Add("WithdrawnOn", listing.WithdrawnOn.HasValue ? DatabaseTimestamp(listing.WithdrawnOn.Value) : (DateTime?)null, System.Data.DbType.DateTime2);
			var changed = await ExecuteAsync(
				$"UPDATE {Tbl("ExchangeListings")} SET {Col("Status")} = {P}Status, {Col("ReviewedOn")} = {P}ReviewedOn, {Col("ReviewedByUserId")} = {P}ReviewedBy, " +
				$"{Col("ReviewNote")} = {P}Note, {Col("ListedOn")} = {P}ListedOn, {Col("WithdrawnOn")} = {P}WithdrawnOn " +
				$"WHERE {Col("ExchangeListingId")} = {P}Id AND {Col("Status")} = {P}Expected",
				parameters, cancellationToken);
			return changed == 1;
		}

		public Task RefreshRatingAsync(int exchangeListingId, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync(
				$"UPDATE {Tbl("ExchangeListings")} SET " +
				$"{Col("RatingCount")} = (SELECT COUNT(*) FROM {Tbl("ExchangeRatings")} r WHERE r.{Col("ExchangeListingId")} = {P}Id), " +
				$"{Col("RatingTotal")} = COALESCE((SELECT SUM(r.{Col("Stars")}) FROM {Tbl("ExchangeRatings")} r WHERE r.{Col("ExchangeListingId")} = {P}Id), 0) " +
				$"WHERE {Col("ExchangeListingId")} = {P}Id",
				new { Id = exchangeListingId }, cancellationToken);
		}

		public Task IncrementImportCountAsync(int exchangeListingId, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync(
				$"UPDATE {Tbl("ExchangeListings")} SET {Col("ImportCount")} = {Col("ImportCount")} + 1 WHERE {Col("ExchangeListingId")} = {P}Id",
				new { Id = exchangeListingId }, cancellationToken);
		}
	}

	public class ExchangeRatingsRepository : RmsRepositoryBase<ExchangeRating>, IExchangeRatingsRepository
	{
		public ExchangeRatingsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<ExchangeRating> GetForListingAndUserAsync(int exchangeListingId, string userId)
		{
			return QueryFirstOrDefaultAsync<ExchangeRating>(
				$"SELECT * FROM {Tbl("ExchangeRatings")} WHERE {Col("ExchangeListingId")} = {P}Id AND {Col("UserId")} = {CaseInsensitive("UserId")}",
				new { Id = exchangeListingId, UserId = userId });
		}
	}

	public class ExchangeCommentsRepository : RmsRepositoryBase<ExchangeComment>, IExchangeCommentsRepository
	{
		public ExchangeCommentsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<ExchangeComment>> GetForListingAsync(int exchangeListingId, bool includeHidden)
		{
			return (await QueryAsync<ExchangeComment>(
				$"SELECT * FROM {Tbl("ExchangeComments")} WHERE {Col("ExchangeListingId")} = {P}Id" + (includeHidden ? string.Empty : $" AND {Col("HiddenOn")} IS NULL") +
				$" ORDER BY {Col("CreatedOn")}, {Col("ExchangeCommentId")}",
				new { Id = exchangeListingId })).ToList();
		}

		public Task<ExchangeComment> GetCommentAsync(int exchangeCommentId)
		{
			return QueryFirstOrDefaultAsync<ExchangeComment>($"SELECT * FROM {Tbl("ExchangeComments")} WHERE {Col("ExchangeCommentId")} = {P}Id", new { Id = exchangeCommentId });
		}

		public async Task<bool> TryHideAsync(int exchangeCommentId, string userId, DateTime hiddenOnUtc, CancellationToken cancellationToken = default)
		{
			var parameters = new DynamicParameters();
			parameters.Add("Id", exchangeCommentId);
			parameters.Add("UserId", userId, System.Data.DbType.String);
			parameters.Add("On", DatabaseTimestamp(hiddenOnUtc));
			return await ExecuteAsync(
				$"UPDATE {Tbl("ExchangeComments")} SET {Col("HiddenOn")} = {P}On, {Col("HiddenByUserId")} = {P}UserId WHERE {Col("ExchangeCommentId")} = {P}Id AND {Col("HiddenOn")} IS NULL",
				parameters, cancellationToken) == 1;
		}
	}

	public class ExchangeImportsRepository : RmsRepositoryBase<ExchangeImport>, IExchangeImportsRepository
	{
		public ExchangeImportsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<ExchangeImport>> GetForDepartmentAsync(int departmentId)
		{
			return (await QueryAsync<ExchangeImport>(
				$"SELECT * FROM {Tbl("ExchangeImports")} WHERE {Col("DepartmentId")} = {P}DepartmentId ORDER BY {Col("ImportedOn")} DESC",
				new { DepartmentId = departmentId })).ToList();
		}
	}
}
