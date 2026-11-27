using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Lms;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	/// <summary>Where a department course came from (LmsCourseOrigins, M0398): portable SQL, one row per course.</summary>
	public class LmsCourseOriginsRepository : LmsRepositoryBase<LmsCourseOrigin>, ILmsCourseOriginsRepository
	{
		public LmsCourseOriginsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		private static LmsCourseOrigin AsUtc(LmsCourseOrigin origin)
		{
			if (origin != null)
				origin.CreatedOn = DateTime.SpecifyKind(origin.CreatedOn, DateTimeKind.Utc);
			return origin;
		}

		public async Task<LmsCourseOrigin> GetForCourseAsync(int departmentId, int lmsCourseId)
		{
			if (departmentId <= 0 || lmsCourseId <= 0)
				return null;
			return AsUtc(await QueryFirstOrDefaultAsync<LmsCourseOrigin>(
				$"SELECT * FROM {Tbl("LmsCourseOrigins")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsCourseId")} = {P}CourseId " +
				$"ORDER BY {Col("LmsCourseOriginId")}",
				new { DepartmentId = departmentId, CourseId = lmsCourseId }));
		}

		public async Task<List<LmsCourseOrigin>> GetForDepartmentAsync(int departmentId)
		{
			if (departmentId <= 0)
				return new List<LmsCourseOrigin>();
			// The citations are only needed for one course at a time; a list never loads them.
			return (await QueryAsync<LmsCourseOrigin>(
				$"SELECT {Cols("LmsCourseOriginId", "DepartmentId", "LmsCourseId", "LmsCourseVersionId", "OriginKind", "SourceKey", "SourceName", "SourceVersion", "SourceUrl", "License", "Attribution", "ExchangeId", "CreatedOn", "CreatedByUserId")} " +
				$"FROM {Tbl("LmsCourseOrigins")} WHERE {Col("DepartmentId")} = {P}DepartmentId ORDER BY {Col("LmsCourseOriginId")}",
				new { DepartmentId = departmentId })).Select(AsUtc).ToList();
		}
	}

	/// <summary>Course flashcards (LmsFlashcards, M0398): portable SQL; the supersede flag is one update by department and source.</summary>
	public class LmsFlashcardsRepository : LmsRepositoryBase<LmsFlashcard>, ILmsFlashcardsRepository
	{
		public LmsFlashcardsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		private static LmsFlashcard AsUtc(LmsFlashcard card)
		{
			if (card != null)
			{
				card.CreatedOn = DateTime.SpecifyKind(card.CreatedOn, DateTimeKind.Utc);
				if (card.FlaggedOn.HasValue)
					card.FlaggedOn = DateTime.SpecifyKind(card.FlaggedOn.Value, DateTimeKind.Utc);
				if (card.ReviewedOn.HasValue)
					card.ReviewedOn = DateTime.SpecifyKind(card.ReviewedOn.Value, DateTimeKind.Utc);
			}
			return card;
		}

		public async Task<List<LmsFlashcard>> GetForCourseAsync(int departmentId, int lmsCourseId)
		{
			if (departmentId <= 0 || lmsCourseId <= 0)
				return new List<LmsFlashcard>();
			return (await QueryAsync<LmsFlashcard>(
				$"SELECT * FROM {Tbl("LmsFlashcards")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsCourseId")} = {P}CourseId " +
				$"ORDER BY {Col("SortOrder")}, {Col("LmsFlashcardId")}",
				new { DepartmentId = departmentId, CourseId = lmsCourseId })).Select(AsUtc).ToList();
		}

		public async Task<LmsFlashcard> GetByIdForDepartmentAsync(int departmentId, int lmsFlashcardId)
		{
			if (departmentId <= 0 || lmsFlashcardId <= 0)
				return null;
			return AsUtc(await QueryFirstOrDefaultAsync<LmsFlashcard>(
				$"SELECT * FROM {Tbl("LmsFlashcards")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsFlashcardId")} = {P}Id",
				new { DepartmentId = departmentId, Id = lmsFlashcardId }));
		}

		public async Task<int> FlagOtherVersionsAsync(int departmentId, int sourceKind, int sourceItemId, int versionInForceId, int reason, DateTime utcNow,
			CancellationToken cancellationToken = default)
		{
			if (departmentId <= 0 || sourceItemId <= 0)
				return 0;
			return await ExecuteAsync(
				$"UPDATE {Tbl("LmsFlashcards")} SET {Col("Status")} = {P}Flagged, {Col("FlagReason")} = {P}Reason, {Col("FlaggedOn")} = {P}Now " +
				$"WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("SourceKind")} = {P}SourceKind AND {Col("SourceItemId")} = {P}ItemId " +
				$"AND {Col("SourceVersionId")} <> {P}VersionId AND {Col("Status")} = {P}Active",
				new
				{
					Flagged = (int)LmsFlashcardStatuses.NeedsReview,
					Active = (int)LmsFlashcardStatuses.Active,
					Reason = reason,
					Now = DatabaseTimestamp(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc)),
					DepartmentId = departmentId,
					SourceKind = sourceKind,
					ItemId = sourceItemId,
					VersionId = versionInForceId
				}, cancellationToken);
		}

		public async Task<int> FlagCardsAsync(int departmentId, IEnumerable<int> lmsFlashcardIds, int reason, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var ids = InListValue((lmsFlashcardIds ?? Enumerable.Empty<int>()).Where(i => i > 0).Distinct());
			if (departmentId <= 0 || ids.Length == 0)
				return 0;
			return await ExecuteAsync(
				$"UPDATE {Tbl("LmsFlashcards")} SET {Col("Status")} = {P}Flagged, {Col("FlagReason")} = {P}Reason, {Col("FlaggedOn")} = {P}Now " +
				$"WHERE {Col("DepartmentId")} = {P}DepartmentId AND {InList("LmsFlashcardId", "Ids")} AND {Col("Status")} = {P}Active",
				new
				{
					Flagged = (int)LmsFlashcardStatuses.NeedsReview,
					Active = (int)LmsFlashcardStatuses.Active,
					Reason = reason,
					Now = DatabaseTimestamp(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc)),
					DepartmentId = departmentId,
					Ids = ids
				}, cancellationToken);
		}
	}
}
