using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	/// <summary>Protocol flashcards (ProtocolFlashcards, M0399): portable SQL; the supersede flag is one update by department and protocol.</summary>
	public class ProtocolFlashcardsRepository : RmsRepositoryBase<ProtocolFlashcard>, IProtocolFlashcardsRepository
	{
		public ProtocolFlashcardsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		private static ProtocolFlashcard AsUtc(ProtocolFlashcard card)
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

		public async Task<List<ProtocolFlashcard>> GetForProtocolAsync(int departmentId, int dispatchProtocolId)
		{
			if (departmentId <= 0 || dispatchProtocolId <= 0)
				return new List<ProtocolFlashcard>();
			return (await QueryAsync<ProtocolFlashcard>(
				$"SELECT * FROM {Tbl("ProtocolFlashcards")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("DispatchProtocolId")} = {P}ProtocolId " +
				$"ORDER BY {Col("SortOrder")}, {Col("ProtocolFlashcardId")}",
				new { DepartmentId = departmentId, ProtocolId = dispatchProtocolId })).Select(AsUtc).ToList();
		}

		public async Task<ProtocolFlashcard> GetByIdForDepartmentAsync(int departmentId, int protocolFlashcardId)
		{
			if (departmentId <= 0 || protocolFlashcardId <= 0)
				return null;
			return AsUtc(await QueryFirstOrDefaultAsync<ProtocolFlashcard>(
				$"SELECT * FROM {Tbl("ProtocolFlashcards")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ProtocolFlashcardId")} = {P}Id",
				new { DepartmentId = departmentId, Id = protocolFlashcardId }));
		}

		public async Task<int> FlagOtherVersionsAsync(int departmentId, int dispatchProtocolId, int versionInForceId, int reason, DateTime utcNow,
			CancellationToken cancellationToken = default)
		{
			if (departmentId <= 0 || dispatchProtocolId <= 0)
				return 0;
			return await ExecuteAsync(
				$"UPDATE {Tbl("ProtocolFlashcards")} SET {Col("Status")} = {P}Flagged, {Col("FlagReason")} = {P}Reason, {Col("FlaggedOn")} = {P}Now " +
				$"WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("DispatchProtocolId")} = {P}ProtocolId " +
				$"AND {Col("ProtocolVersionId")} <> {P}VersionId AND {Col("Status")} = {P}Active",
				new
				{
					Flagged = (int)ProtocolFlashcardStatuses.NeedsReview,
					Active = (int)ProtocolFlashcardStatuses.Active,
					Reason = reason,
					Now = DatabaseTimestamp(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc)),
					DepartmentId = departmentId,
					ProtocolId = dispatchProtocolId,
					VersionId = versionInForceId
				}, cancellationToken);
		}

		public async Task<int> FlagCardsAsync(int departmentId, IEnumerable<int> protocolFlashcardIds, int reason, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var ids = InListValue((protocolFlashcardIds ?? Enumerable.Empty<int>()).Where(i => i > 0).Distinct());
			if (departmentId <= 0 || ids.Length == 0)
				return 0;
			return await ExecuteAsync(
				$"UPDATE {Tbl("ProtocolFlashcards")} SET {Col("Status")} = {P}Flagged, {Col("FlagReason")} = {P}Reason, {Col("FlaggedOn")} = {P}Now " +
				$"WHERE {Col("DepartmentId")} = {P}DepartmentId AND {InList("ProtocolFlashcardId", "Ids")} AND {Col("Status")} = {P}Active",
				new
				{
					Flagged = (int)ProtocolFlashcardStatuses.NeedsReview,
					Active = (int)ProtocolFlashcardStatuses.Active,
					Reason = reason,
					Now = DatabaseTimestamp(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc)),
					DepartmentId = departmentId,
					Ids = ids
				}, cancellationToken);
		}
	}
}
