using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Model;
using Resgrid.Model.Lms;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	/// <summary>Publishers' protocol and course sets (round 10 A3, M0415).</summary>
	public class ContentSetsRepository : RmsRepositoryBase<ContentSet>, IContentSetsRepository
	{
		public ContentSetsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<ContentSet> GetByIdForDepartmentAsync(int departmentId, int contentSetId) =>
			QueryFirstOrDefaultAsync<ContentSet>(
				$"SELECT * FROM {Tbl("ContentSets")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ContentSetId")} = {P}Id",
				new { DepartmentId = departmentId, Id = contentSetId });

		public async Task<List<ContentSet>> GetForDepartmentAsync(int departmentId, ContentSetKinds kind, bool includeArchived) =>
			(await QueryAsync<ContentSet>(
				$"SELECT * FROM {Tbl("ContentSets")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("Kind")} = {P}Kind" +
				(includeArchived ? string.Empty : $" AND {Col("IsArchived")} = {P}Archived") + $" ORDER BY {Col("ContentSetId")} DESC",
				new { DepartmentId = departmentId, Kind = (int)kind, Archived = false })).ToList();

		public async Task<List<ContentSet>> GetLiveForPublishersAsync(IEnumerable<int> publisherDepartmentIds, ContentSetKinds kind)
		{
			var ids = InListValue(publisherDepartmentIds?.Where(i => i > 0).Distinct());
			if (ids.Length == 0)
				return new List<ContentSet>();
			return (await QueryAsync<ContentSet>(
				$"SELECT * FROM {Tbl("ContentSets")} WHERE {InList("DepartmentId", "Ids")} AND {Col("Kind")} = {P}Kind AND {Col("IsArchived")} = {P}Archived ORDER BY {Col("Name")}, {Col("ContentSetId")}",
				new { Ids = ids, Kind = (int)kind, Archived = false })).ToList();
		}
	}

	public class ContentSetItemsRepository : RmsRepositoryBase<ContentSetItem>, IContentSetItemsRepository
	{
		public ContentSetItemsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<ContentSetItem>> GetForSetAsync(int contentSetId, bool includeRemoved) =>
			(await QueryAsync<ContentSetItem>(
				$"SELECT * FROM {Tbl("ContentSetItems")} WHERE {Col("ContentSetId")} = {P}SetId" + (includeRemoved ? string.Empty : $" AND {Col("RemovedOn")} IS NULL") +
				$" ORDER BY {Col("SortOrder")}, {Col("ContentSetItemId")}",
				new { SetId = contentSetId })).ToList();

		public async Task<List<ContentSetItem>> GetLiveByKindAsync(ContentSetItemKinds itemKind, int take)
		{
			var top = Math.Max(1, Math.Min(take, 10000));
			var sql = IsPostgres
				? $"SELECT * FROM {Tbl("ContentSetItems")} WHERE {Col("ItemKind")} = {P}Kind AND {Col("RemovedOn")} IS NULL ORDER BY {Col("ContentSetItemId")} LIMIT {top}"
				: $"SELECT TOP ({top}) * FROM {Tbl("ContentSetItems")} WHERE {Col("ItemKind")} = {P}Kind AND {Col("RemovedOn")} IS NULL ORDER BY {Col("ContentSetItemId")}";
			return (await QueryAsync<ContentSetItem>(sql, new { Kind = (int)itemKind })).ToList();
		}

		public async Task<List<ContentSetItem>> GetLiveForItemAsync(int publisherDepartmentId, ContentSetItemKinds itemKind, int itemId) =>
			(await QueryAsync<ContentSetItem>(
				$"SELECT * FROM {Tbl("ContentSetItems")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ItemKind")} = {P}Kind AND {Col("ItemId")} = {P}ItemId AND {Col("RemovedOn")} IS NULL",
				new { DepartmentId = publisherDepartmentId, Kind = (int)itemKind, ItemId = itemId })).ToList();
	}

	public class ContentSetSubscriptionsRepository : RmsRepositoryBase<ContentSetSubscription>, IContentSetSubscriptionsRepository
	{
		private const int Ended = (int)ContentSetSubscriptionStatuses.Ended;

		public ContentSetSubscriptionsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<ContentSetSubscription> GetByIdForDepartmentAsync(int departmentId, int contentSetSubscriptionId) =>
			QueryFirstOrDefaultAsync<ContentSetSubscription>(
				$"SELECT * FROM {Tbl("ContentSetSubscriptions")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ContentSetSubscriptionId")} = {P}Id",
				new { DepartmentId = departmentId, Id = contentSetSubscriptionId });

		public async Task<List<ContentSetSubscription>> GetForDepartmentAsync(int departmentId, bool includeEnded) =>
			(await QueryAsync<ContentSetSubscription>(
				$"SELECT * FROM {Tbl("ContentSetSubscriptions")} WHERE {Col("DepartmentId")} = {P}DepartmentId" + (includeEnded ? string.Empty : $" AND {Col("Status")} <> {P}Ended") +
				$" ORDER BY {Col("ContentSetSubscriptionId")} DESC",
				new { DepartmentId = departmentId, Ended })).ToList();

		public async Task<List<ContentSetSubscription>> GetOpenForSetAsync(int contentSetId) =>
			(await QueryAsync<ContentSetSubscription>(
				$"SELECT * FROM {Tbl("ContentSetSubscriptions")} WHERE {Col("ContentSetId")} = {P}SetId AND {Col("Status")} <> {P}Ended ORDER BY {Col("ContentSetSubscriptionId")}",
				new { SetId = contentSetId, Ended })).ToList();

		public Task<ContentSetSubscription> GetOpenAsync(int contentSetId, int departmentId) =>
			QueryFirstOrDefaultAsync<ContentSetSubscription>(
				$"SELECT * FROM {Tbl("ContentSetSubscriptions")} WHERE {Col("ContentSetId")} = {P}SetId AND {Col("DepartmentId")} = {P}DepartmentId AND {Col("Status")} <> {P}Ended",
				new { SetId = contentSetId, DepartmentId = departmentId, Ended });

		public async Task<List<ContentSetSubscription>> GetOpenForLinkAsync(int departmentLinkId) =>
			(await QueryAsync<ContentSetSubscription>(
				$"SELECT * FROM {Tbl("ContentSetSubscriptions")} WHERE {Col("DepartmentLinkId")} = {P}LinkId AND {Col("Status")} <> {P}Ended",
				new { LinkId = departmentLinkId, Ended })).ToList();

		public async Task<List<ContentSetSubscription>> GetAllOpenAsync(int take)
		{
			var top = Math.Max(1, Math.Min(take, 10000));
			var sql = IsPostgres
				? $"SELECT * FROM {Tbl("ContentSetSubscriptions")} WHERE {Col("Status")} <> {P}Ended ORDER BY {Col("ContentSetSubscriptionId")} LIMIT {top}"
				: $"SELECT TOP ({top}) * FROM {Tbl("ContentSetSubscriptions")} WHERE {Col("Status")} <> {P}Ended ORDER BY {Col("ContentSetSubscriptionId")}";
			return (await QueryAsync<ContentSetSubscription>(sql, new { Ended })).ToList();
		}

		public async Task<List<ContentSetSubscription>> GetOpenBetweenAsync(int publisherDepartmentId, int subscriberDepartmentId) =>
			(await QueryAsync<ContentSetSubscription>(
				$"SELECT * FROM {Tbl("ContentSetSubscriptions")} WHERE {Col("PublisherDepartmentId")} = {P}PublisherId AND {Col("DepartmentId")} = {P}DepartmentId AND {Col("Status")} <> {P}Ended",
				new { PublisherId = publisherDepartmentId, DepartmentId = subscriberDepartmentId, Ended })).ToList();
	}

	public class ContentSetCopiesRepository : RmsRepositoryBase<ContentSetCopy>, IContentSetCopiesRepository
	{
		public ContentSetCopiesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<ContentSetCopy> GetByIdForDepartmentAsync(int departmentId, int contentSetCopyId) =>
			QueryFirstOrDefaultAsync<ContentSetCopy>(
				$"SELECT * FROM {Tbl("ContentSetCopies")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ContentSetCopyId")} = {P}Id",
				new { DepartmentId = departmentId, Id = contentSetCopyId });

		public async Task<List<ContentSetCopy>> GetForSubscriptionAsync(int contentSetSubscriptionId) =>
			(await QueryAsync<ContentSetCopy>(
				$"SELECT * FROM {Tbl("ContentSetCopies")} WHERE {Col("ContentSetSubscriptionId")} = {P}SubscriptionId ORDER BY {Col("ContentSetCopyId")}",
				new { SubscriptionId = contentSetSubscriptionId })).ToList();

		public Task<ContentSetCopy> GetManagedForLocalItemAsync(int departmentId, ContentSetItemKinds itemKind, int localItemId) =>
			QueryFirstOrDefaultAsync<ContentSetCopy>(
				$"SELECT * FROM {Tbl("ContentSetCopies")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ItemKind")} = {P}Kind AND {Col("LocalItemId")} = {P}LocalId " +
				$"AND {Col("DetachedOn")} IS NULL ORDER BY {Col("ContentSetCopyId")} DESC",
				new { DepartmentId = departmentId, Kind = (int)itemKind, LocalId = localItemId });

		public async Task<List<ContentSetCopy>> GetManagedForDepartmentAsync(int departmentId, ContentSetItemKinds itemKind) =>
			(await QueryAsync<ContentSetCopy>(
				$"SELECT * FROM {Tbl("ContentSetCopies")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ItemKind")} = {P}Kind AND {Col("DetachedOn")} IS NULL",
				new { DepartmentId = departmentId, Kind = (int)itemKind })).ToList();

		public Task<int> DetachAsync(int contentSetSubscriptionId, int? contentSetItemId, DateTime detachedOnUtc, CancellationToken cancellationToken = default)
		{
			var parameters = new DynamicParameters();
			parameters.Add("SubscriptionId", contentSetSubscriptionId);
			parameters.Add("ItemId", contentSetItemId ?? 0);
			parameters.Add("On", DatabaseTimestamp(detachedOnUtc));
			return ExecuteAsync(
				$"UPDATE {Tbl("ContentSetCopies")} SET {Col("DetachedOn")} = {P}On WHERE {Col("ContentSetSubscriptionId")} = {P}SubscriptionId AND {Col("DetachedOn")} IS NULL" +
				(contentSetItemId.HasValue ? $" AND {Col("ContentSetItemId")} = {P}ItemId" : string.Empty),
				parameters, cancellationToken);
		}
	}

	public class ContentSetUpdatesRepository : RmsRepositoryBase<ContentSetUpdate>, IContentSetUpdatesRepository
	{
		private static readonly int[] OpenStatuses = { (int)ContentSetUpdateStatuses.Pending, (int)ContentSetUpdateStatuses.Deferred, (int)ContentSetUpdateStatuses.Scheduled };

		public ContentSetUpdatesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<ContentSetUpdate> GetByIdForDepartmentAsync(int departmentId, int contentSetUpdateId) =>
			QueryFirstOrDefaultAsync<ContentSetUpdate>(
				$"SELECT * FROM {Tbl("ContentSetUpdates")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ContentSetUpdateId")} = {P}Id",
				new { DepartmentId = departmentId, Id = contentSetUpdateId });

		public async Task<List<ContentSetUpdate>> GetForDepartmentAsync(int departmentId, bool openOnly, int take)
		{
			var top = Math.Max(1, Math.Min(take, 500));
			var sql = IsPostgres
				? $"SELECT * FROM {Tbl("ContentSetUpdates")} WHERE {Col("DepartmentId")} = {P}DepartmentId" + (openOnly ? $" AND {InList("Status", "Open")}" : string.Empty) +
					$" ORDER BY {Col("ContentSetUpdateId")} DESC LIMIT {top}"
				: $"SELECT TOP ({top}) * FROM {Tbl("ContentSetUpdates")} WHERE {Col("DepartmentId")} = {P}DepartmentId" + (openOnly ? $" AND {InList("Status", "Open")}" : string.Empty) +
					$" ORDER BY {Col("ContentSetUpdateId")} DESC";
			return (await QueryAsync<ContentSetUpdate>(sql, new { DepartmentId = departmentId, Open = OpenStatuses })).ToList();
		}

		public async Task<List<ContentSetUpdate>> GetOpenForItemAsync(int contentSetSubscriptionId, int contentSetItemId) =>
			(await QueryAsync<ContentSetUpdate>(
				$"SELECT * FROM {Tbl("ContentSetUpdates")} WHERE {Col("ContentSetSubscriptionId")} = {P}SubscriptionId AND {Col("ContentSetItemId")} = {P}ItemId AND {InList("Status", "Open")}",
				new { SubscriptionId = contentSetSubscriptionId, ItemId = contentSetItemId, Open = OpenStatuses })).ToList();

		public async Task<List<ContentSetUpdate>> GetDueAsync(DateTime utcNow, TimeSpan claimTimeout, int take)
		{
			var top = Math.Max(1, Math.Min(take, 500));
			var parameters = new DynamicParameters();
			parameters.Add("Scheduled", (int)ContentSetUpdateStatuses.Scheduled);
			parameters.Add("Now", DatabaseTimestamp(utcNow));
			parameters.Add("Stale", DatabaseTimestamp(utcNow - claimTimeout));
			var where = $"{Col("Status")} = {P}Scheduled AND {Col("ApplyOn")} <= {P}Now AND ({Col("ClaimedOn")} IS NULL OR {Col("ClaimedOn")} < {P}Stale)";
			var sql = IsPostgres
				? $"SELECT * FROM {Tbl("ContentSetUpdates")} WHERE {where} ORDER BY {Col("ApplyOn")}, {Col("ContentSetUpdateId")} LIMIT {top}"
				: $"SELECT TOP ({top}) * FROM {Tbl("ContentSetUpdates")} WHERE {where} ORDER BY {Col("ApplyOn")}, {Col("ContentSetUpdateId")}";
			return (await QueryAsync<ContentSetUpdate>(sql, parameters)).ToList();
		}

		public async Task<List<ContentSetUpdate>> GetUndecidedDueAsync(DateTime utcNow, int take)
		{
			var top = Math.Max(1, Math.Min(take, 500));
			var parameters = new DynamicParameters();
			parameters.Add("Pending", (int)ContentSetUpdateStatuses.Pending);
			parameters.Add("Deferred", (int)ContentSetUpdateStatuses.Deferred);
			parameters.Add("Active", (int)ContentSetSubscriptionStatuses.Active);
			parameters.Add("Auto", true);
			parameters.Add("Now", DatabaseTimestamp(utcNow));
			var where = $"u.{Col("Status")} IN ({P}Pending, {P}Deferred) AND s.{Col("Status")} = {P}Active AND (" +
				$"(u.{Col("Status")} = {P}Pending AND s.{Col("AutoAccept")} = {P}Auto) OR (u.{Col("DeadlineOn")} IS NOT NULL AND u.{Col("DeadlineOn")} <= {P}Now) OR " +
				$"(u.{Col("Status")} = {P}Deferred AND u.{Col("DeferredUntil")} IS NOT NULL AND u.{Col("DeferredUntil")} <= {P}Now))";
			var from = $"{Tbl("ContentSetUpdates")} u INNER JOIN {Tbl("ContentSetSubscriptions")} s ON s.{Col("ContentSetSubscriptionId")} = u.{Col("ContentSetSubscriptionId")}";
			var sql = IsPostgres
				? $"SELECT u.* FROM {from} WHERE {where} ORDER BY u.{Col("ContentSetUpdateId")} LIMIT {top}"
				: $"SELECT TOP ({top}) u.* FROM {from} WHERE {where} ORDER BY u.{Col("ContentSetUpdateId")}";
			return (await QueryAsync<ContentSetUpdate>(sql, parameters)).ToList();
		}

		public async Task<bool> TryClaimAsync(int contentSetUpdateId, DateTime utcNow, TimeSpan claimTimeout, CancellationToken cancellationToken = default)
		{
			var parameters = new DynamicParameters();
			parameters.Add("Id", contentSetUpdateId);
			parameters.Add("Scheduled", (int)ContentSetUpdateStatuses.Scheduled);
			parameters.Add("Now", DatabaseTimestamp(utcNow));
			parameters.Add("Stale", DatabaseTimestamp(utcNow - claimTimeout));
			return await ExecuteAsync(
				$"UPDATE {Tbl("ContentSetUpdates")} SET {Col("ClaimedOn")} = {P}Now WHERE {Col("ContentSetUpdateId")} = {P}Id AND {Col("Status")} = {P}Scheduled " +
				$"AND ({Col("ClaimedOn")} IS NULL OR {Col("ClaimedOn")} < {P}Stale)", parameters, cancellationToken) == 1;
		}

		public async Task<bool> TryUpdateFromStatusAsync(ContentSetUpdate update, ContentSetUpdateStatuses expectedStatus, CancellationToken cancellationToken = default)
		{
			var parameters = new DynamicParameters();
			parameters.Add("Id", update.ContentSetUpdateId);
			parameters.Add("Expected", (int)expectedStatus);
			parameters.Add("Status", update.Status);
			parameters.Add("ApplyOn", update.ApplyOn.HasValue ? DatabaseTimestamp(update.ApplyOn.Value) : (DateTime?)null, System.Data.DbType.DateTime2);
			parameters.Add("DeferredUntil", update.DeferredUntil.HasValue ? DatabaseTimestamp(update.DeferredUntil.Value) : (DateTime?)null, System.Data.DbType.DateTime2);
			parameters.Add("DecidedOn", update.DecidedOn.HasValue ? DatabaseTimestamp(update.DecidedOn.Value) : (DateTime?)null, System.Data.DbType.DateTime2);
			parameters.Add("DecidedBy", update.DecidedByUserId, System.Data.DbType.String);
			parameters.Add("Auto", update.DecidedAutomatically);
			parameters.Add("AppliedOn", update.AppliedOn.HasValue ? DatabaseTimestamp(update.AppliedOn.Value) : (DateTime?)null, System.Data.DbType.DateTime2);
			parameters.Add("LocalVersionId", update.LocalVersionId, System.Data.DbType.Int32);
			parameters.Add("CopyId", update.ContentSetCopyId, System.Data.DbType.Int32);
			parameters.Add("Failure", update.FailureReason, System.Data.DbType.String);
			parameters.Add("ClaimedOn", update.ClaimedOn.HasValue ? DatabaseTimestamp(update.ClaimedOn.Value) : (DateTime?)null, System.Data.DbType.DateTime2);
			var changed = await ExecuteAsync(
				$"UPDATE {Tbl("ContentSetUpdates")} SET {Col("Status")} = {P}Status, {Col("ApplyOn")} = {P}ApplyOn, {Col("DeferredUntil")} = {P}DeferredUntil, " +
				$"{Col("DecidedOn")} = {P}DecidedOn, {Col("DecidedByUserId")} = {P}DecidedBy, {Col("DecidedAutomatically")} = {P}Auto, {Col("AppliedOn")} = {P}AppliedOn, " +
				$"{Col("LocalVersionId")} = {P}LocalVersionId, {Col("ContentSetCopyId")} = {P}CopyId, {Col("FailureReason")} = {P}Failure, {Col("ClaimedOn")} = {P}ClaimedOn " +
				$"WHERE {Col("ContentSetUpdateId")} = {P}Id AND {Col("Status")} = {P}Expected",
				parameters, cancellationToken);
			return changed == 1;
		}

		public Task<int> CloseOpenForSubscriptionAsync(int contentSetSubscriptionId, ContentSetUpdateStatuses status, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var parameters = new DynamicParameters();
			parameters.Add("SubscriptionId", contentSetSubscriptionId);
			parameters.Add("Status", (int)status);
			parameters.Add("Now", DatabaseTimestamp(utcNow));
			parameters.Add("Open", OpenStatuses);
			return ExecuteAsync(
				$"UPDATE {Tbl("ContentSetUpdates")} SET {Col("Status")} = {P}Status, {Col("DecidedOn")} = {P}Now, {Col("DecidedAutomatically")} = {P}Auto " +
				$"WHERE {Col("ContentSetSubscriptionId")} = {P}SubscriptionId AND {InList("Status", "Open")}",
				AddAuto(parameters), cancellationToken);
		}

		private static DynamicParameters AddAuto(DynamicParameters parameters)
		{
			parameters.Add("Auto", true);
			return parameters;
		}
	}

	public class ContentSetProposalsRepository : RmsRepositoryBase<ContentSetProposal>, IContentSetProposalsRepository
	{
		public ContentSetProposalsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<ContentSetProposal>> GetForPublisherAsync(int publisherDepartmentId, bool openOnly, int take)
		{
			var top = Math.Max(1, Math.Min(take, 500));
			var where = $"{Col("PublisherDepartmentId")} = {P}PublisherId" + (openOnly ? $" AND {Col("Status")} = {P}Open" : string.Empty);
			var sql = IsPostgres
				? $"SELECT * FROM {Tbl("ContentSetProposals")} WHERE {where} ORDER BY {Col("ContentSetProposalId")} DESC LIMIT {top}"
				: $"SELECT TOP ({top}) * FROM {Tbl("ContentSetProposals")} WHERE {where} ORDER BY {Col("ContentSetProposalId")} DESC";
			return (await QueryAsync<ContentSetProposal>(sql, new { PublisherId = publisherDepartmentId, Open = (int)ContentSetProposalStatuses.Open })).ToList();
		}

		public async Task<List<ContentSetProposal>> GetForDepartmentAsync(int departmentId, int take)
		{
			var top = Math.Max(1, Math.Min(take, 500));
			var sql = IsPostgres
				? $"SELECT * FROM {Tbl("ContentSetProposals")} WHERE {Col("DepartmentId")} = {P}DepartmentId ORDER BY {Col("ContentSetProposalId")} DESC LIMIT {top}"
				: $"SELECT TOP ({top}) * FROM {Tbl("ContentSetProposals")} WHERE {Col("DepartmentId")} = {P}DepartmentId ORDER BY {Col("ContentSetProposalId")} DESC";
			return (await QueryAsync<ContentSetProposal>(sql, new { DepartmentId = departmentId })).ToList();
		}

		public Task<ContentSetProposal> GetForPublisherByIdAsync(int publisherDepartmentId, int contentSetProposalId) =>
			QueryFirstOrDefaultAsync<ContentSetProposal>(
				$"SELECT * FROM {Tbl("ContentSetProposals")} WHERE {Col("PublisherDepartmentId")} = {P}PublisherId AND {Col("ContentSetProposalId")} = {P}Id",
				new { PublisherId = publisherDepartmentId, Id = contentSetProposalId });
	}

	/// <summary>Sponsored LMS seats (round 10 A3, M0416).</summary>
	public class LmsSponsorshipsRepository : RmsRepositoryBase<LmsSponsorship>, ILmsSponsorshipsRepository
	{
		private const int Active = (int)LmsSponsorshipStatuses.Active;

		public LmsSponsorshipsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<LmsSponsorship>> GetActiveForDepartmentAsync(int departmentId) =>
			(await QueryAsync<LmsSponsorship>(
				$"SELECT * FROM {Tbl("LmsSponsorships")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("Status")} = {P}Active ORDER BY {Col("LmsSponsorshipId")}",
				new { DepartmentId = departmentId, Active })).ToList();

		public async Task<List<LmsSponsorship>> GetActiveForSponsorAsync(int sponsorDepartmentId) =>
			(await QueryAsync<LmsSponsorship>(
				$"SELECT * FROM {Tbl("LmsSponsorships")} WHERE {Col("SponsorDepartmentId")} = {P}SponsorId AND {Col("Status")} = {P}Active ORDER BY {Col("LmsSponsorshipId")}",
				new { SponsorId = sponsorDepartmentId, Active })).ToList();

		public async Task<List<LmsSponsorship>> GetForPartyAsync(int departmentId, int take)
		{
			var top = Math.Max(1, Math.Min(take, 500));
			var where = $"({Col("DepartmentId")} = {P}DepartmentId OR {Col("SponsorDepartmentId")} = {P}DepartmentId)";
			var sql = IsPostgres
				? $"SELECT * FROM {Tbl("LmsSponsorships")} WHERE {where} ORDER BY {Col("LmsSponsorshipId")} DESC LIMIT {top}"
				: $"SELECT TOP ({top}) * FROM {Tbl("LmsSponsorships")} WHERE {where} ORDER BY {Col("LmsSponsorshipId")} DESC";
			return (await QueryAsync<LmsSponsorship>(sql, new { DepartmentId = departmentId })).ToList();
		}

		public Task<LmsSponsorship> GetActiveAsync(int sponsorDepartmentId, int departmentId) =>
			QueryFirstOrDefaultAsync<LmsSponsorship>(
				$"SELECT * FROM {Tbl("LmsSponsorships")} WHERE {Col("SponsorDepartmentId")} = {P}SponsorId AND {Col("DepartmentId")} = {P}DepartmentId AND {Col("Status")} = {P}Active",
				new { SponsorId = sponsorDepartmentId, DepartmentId = departmentId, Active });

		public async Task<List<LmsSponsorship>> GetAllActiveAsync(int take)
		{
			var top = Math.Max(1, Math.Min(take, 10000));
			var sql = IsPostgres
				? $"SELECT * FROM {Tbl("LmsSponsorships")} WHERE {Col("Status")} = {P}Active ORDER BY {Col("LmsSponsorshipId")} LIMIT {top}"
				: $"SELECT TOP ({top}) * FROM {Tbl("LmsSponsorships")} WHERE {Col("Status")} = {P}Active ORDER BY {Col("LmsSponsorshipId")}";
			return (await QueryAsync<LmsSponsorship>(sql, new { Active })).ToList();
		}

		public async Task<List<LmsSponsorship>> GetActiveForLinkAsync(int departmentLinkId) =>
			(await QueryAsync<LmsSponsorship>(
				$"SELECT * FROM {Tbl("LmsSponsorships")} WHERE {Col("DepartmentLinkId")} = {P}LinkId AND {Col("Status")} = {P}Active",
				new { LinkId = departmentLinkId, Active })).ToList();

		public async Task<bool> TryEndAsync(int lmsSponsorshipId, LmsSponsorshipEndReasons reason, string userId, DateTime endedOnUtc, CancellationToken cancellationToken = default)
		{
			var parameters = new DynamicParameters();
			parameters.Add("Id", lmsSponsorshipId);
			parameters.Add("Active", Active);
			parameters.Add("Ended", (int)LmsSponsorshipStatuses.Ended);
			parameters.Add("Reason", (int)reason);
			parameters.Add("UserId", userId, System.Data.DbType.String);
			parameters.Add("On", DatabaseTimestamp(endedOnUtc));
			return await ExecuteAsync(
				$"UPDATE {Tbl("LmsSponsorships")} SET {Col("Status")} = {P}Ended, {Col("EndReason")} = {P}Reason, {Col("EndedByUserId")} = {P}UserId, {Col("EndedOn")} = {P}On " +
				$"WHERE {Col("LmsSponsorshipId")} = {P}Id AND {Col("Status")} = {P}Active", parameters, cancellationToken) == 1;
		}
	}
}
