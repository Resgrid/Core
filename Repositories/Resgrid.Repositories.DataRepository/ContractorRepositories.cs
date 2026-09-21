using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Invoicing;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	// Workforce & Business Operations plan, Phase C (C2): contractor path repositories (registry M0215–M0217).

	public class RateScheduleRepository : RmsRepositoryBase<RateSchedule>, IRateScheduleRepository
	{
		public RateScheduleRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		private static string False => IsPostgres ? "FALSE" : "0";
		private static string True => IsPostgres ? "TRUE" : "1";

		public Task<RateSchedule> GetByIdForDepartmentAsync(string rateScheduleId, int departmentId) =>
			QueryFirstOrDefaultAsync<RateSchedule>($"SELECT * FROM {Tbl("RateSchedules")} WHERE {Col("RateScheduleId")} = {P}Id AND {Col("DepartmentId")} = {P}DepartmentId", new { Id = rateScheduleId, DepartmentId = departmentId });

		public Task<IEnumerable<RateSchedule>> GetForDepartmentAsync(int departmentId, bool includeInactive) =>
			QueryAsync<RateSchedule>(
				$"SELECT * FROM {Tbl("RateSchedules")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("IsDeleted")} = {False}" + (includeInactive ? string.Empty : $" AND {Col("IsActive")} = {True}") +
				$" ORDER BY {Col("Name")}", new { DepartmentId = departmentId });
	}

	public class RateScheduleEntryRepository : RmsRepositoryBase<RateScheduleEntry>, IRateScheduleEntryRepository
	{
		public RateScheduleEntryRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<IEnumerable<RateScheduleEntry>> GetByScheduleAsync(string rateScheduleId, bool includeInactive) =>
			QueryAsync<RateScheduleEntry>(
				$"SELECT * FROM {Tbl("RateScheduleEntries")} WHERE {Col("RateScheduleId")} = {P}Id AND {Col("IsDeleted")} = {(IsPostgres ? "FALSE" : "0")}" + (includeInactive ? string.Empty : $" AND {Col("IsActive")} = {(IsPostgres ? "TRUE" : "1")}") +
				$" ORDER BY {Col("SortOrder")}, {Col("Name")}", new { Id = rateScheduleId });

		public Task<IEnumerable<RateScheduleEntry>> GetByIdsAsync(IEnumerable<string> rateScheduleEntryIds) =>
			QueryAsync<RateScheduleEntry>($"SELECT * FROM {Tbl("RateScheduleEntries")} WHERE {InList("RateScheduleEntryId", "Ids")}", new { Ids = InListValue(rateScheduleEntryIds) });
	}

	public class RateScheduleEntryBandRepository : RmsRepositoryBase<RateScheduleEntryBand>, IRateScheduleEntryBandRepository
	{
		public RateScheduleEntryBandRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<IEnumerable<RateScheduleEntryBand>> GetByScheduleAsync(string rateScheduleId) =>
			QueryAsync<RateScheduleEntryBand>(
				$"SELECT b.* FROM {Tbl("RateScheduleEntryBands")} b JOIN {Tbl("RateScheduleEntries")} e ON e.{Col("RateScheduleEntryId")} = b.{Col("RateScheduleEntryId")} " +
				$"WHERE e.{Col("RateScheduleId")} = {P}Id ORDER BY b.{Col("SortOrder")}, b.{Col("BandType")}", new { Id = rateScheduleId });

		public Task<IEnumerable<RateScheduleEntryBand>> GetByEntryAsync(string rateScheduleEntryId) =>
			QueryAsync<RateScheduleEntryBand>($"SELECT * FROM {Tbl("RateScheduleEntryBands")} WHERE {Col("RateScheduleEntryId")} = {P}Id ORDER BY {Col("SortOrder")}, {Col("BandType")}", new { Id = rateScheduleEntryId });

		public async Task<bool> DeleteByEntryAsync(string rateScheduleEntryId, CancellationToken cancellationToken = default)
		{
			await ExecuteAsync($"DELETE FROM {Tbl("RateScheduleEntryBands")} WHERE {Col("RateScheduleEntryId")} = {P}Id", new { Id = rateScheduleEntryId }, cancellationToken);
			return true;
		}
	}

	public class RatePremiumRepository : RmsRepositoryBase<RatePremium>, IRatePremiumRepository
	{
		public RatePremiumRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<IEnumerable<RatePremium>> GetByScheduleAsync(string rateScheduleId, bool includeInactive) =>
			QueryAsync<RatePremium>(
				$"SELECT * FROM {Tbl("RatePremiums")} WHERE {Col("RateScheduleId")} = {P}Id AND {Col("IsDeleted")} = {(IsPostgres ? "FALSE" : "0")}" + (includeInactive ? string.Empty : $" AND {Col("IsActive")} = {(IsPostgres ? "TRUE" : "1")}") +
				$" ORDER BY {Col("Name")}", new { Id = rateScheduleId });
	}

	public class ServiceContractRepository : RmsRepositoryBase<ServiceContract>, IServiceContractRepository
	{
		public ServiceContractRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		private static string False => IsPostgres ? "FALSE" : "0";
		private const int Active = (int)ServiceContractStatuses.Active;

		public Task<ServiceContract> GetByIdForDepartmentAsync(string serviceContractId, int departmentId) =>
			QueryFirstOrDefaultAsync<ServiceContract>($"SELECT * FROM {Tbl("ServiceContracts")} WHERE {Col("ServiceContractId")} = {P}Id AND {Col("DepartmentId")} = {P}DepartmentId", new { Id = serviceContractId, DepartmentId = departmentId });

		public Task<IEnumerable<ServiceContract>> GetForDepartmentAsync(int departmentId, int? status) =>
			QueryAsync<ServiceContract>(
				$"SELECT * FROM {Tbl("ServiceContracts")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("IsDeleted")} = {False}" + (status.HasValue ? $" AND {Col("Status")} = {P}Status" : string.Empty) +
				$" ORDER BY {Col("StartOn")} DESC", new { DepartmentId = departmentId, Status = status ?? 0 });

		public Task<IEnumerable<ServiceContract>> GetByContactIdAsync(int departmentId, string contactId) =>
			QueryAsync<ServiceContract>($"SELECT * FROM {Tbl("ServiceContracts")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ContactId")} = {P}ContactId AND {Col("IsDeleted")} = {False} ORDER BY {Col("StartOn")} DESC", new { DepartmentId = departmentId, ContactId = contactId });

		public Task<IEnumerable<ServiceContract>> GetEndingBetweenAsync(DateTime fromUtc, DateTime toUtc) =>
			QueryAsync<ServiceContract>($"SELECT * FROM {Tbl("ServiceContracts")} WHERE {Col("Status")} = {Active} AND {Col("IsDeleted")} = {False} AND {Col("EndOn")} >= {P}From AND {Col("EndOn")} <= {P}To", new { From = DatabaseTimestamp(fromUtc), To = DatabaseTimestamp(toUtc) });

		public Task<IEnumerable<ServiceContract>> GetLapsedAsync(DateTime asOfUtc) =>
			QueryAsync<ServiceContract>($"SELECT * FROM {Tbl("ServiceContracts")} WHERE {Col("Status")} = {Active} AND {Col("IsDeleted")} = {False} AND {Col("EndOn")} < {P}AsOf", new { AsOf = DatabaseTimestamp(asOfUtc) });
	}

	public class ServiceContractDocumentRequirementRepository : RmsRepositoryBase<ServiceContractDocumentRequirement>, IServiceContractDocumentRequirementRepository
	{
		public ServiceContractDocumentRequirementRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<IEnumerable<ServiceContractDocumentRequirement>> GetByContractAsync(string serviceContractId) =>
			QueryAsync<ServiceContractDocumentRequirement>($"SELECT * FROM {Tbl("ServiceContractDocumentRequirements")} WHERE {Col("ServiceContractId")} = {P}Id ORDER BY {Col("SortOrder")}", new { Id = serviceContractId });

		public async Task<bool> DeleteByContractAsync(string serviceContractId, CancellationToken cancellationToken = default)
		{
			await ExecuteAsync($"DELETE FROM {Tbl("ServiceContractDocumentRequirements")} WHERE {Col("ServiceContractId")} = {P}Id", new { Id = serviceContractId }, cancellationToken);
			return true;
		}
	}

	public class DepartmentComplianceDocumentRepository : RmsRepositoryBase<DepartmentComplianceDocument>, IDepartmentComplianceDocumentRepository
	{
		public DepartmentComplianceDocumentRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		private static readonly string[] Meta = { "DepartmentComplianceDocumentId", "DepartmentId", "DocumentType", "Name", "DocumentNumber", "Issuer", "EffectiveOn", "ExpiresOn", "AlertLeadDays", "FileName", "FileType", "FileSize", "IsDeleted", "AddedOn", "AddedByUserId", "EditedOn", "EditedByUserId" };
		private static string False => IsPostgres ? "FALSE" : "0";

		public Task<IEnumerable<DepartmentComplianceDocument>> GetForDepartmentAsync(int departmentId) =>
			QueryAsync<DepartmentComplianceDocument>($"SELECT {Cols(Meta)} FROM {Tbl("DepartmentComplianceDocuments")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("IsDeleted")} = {False} ORDER BY {Col("DocumentType")}, {Col("Name")}", new { DepartmentId = departmentId });

		public Task<DepartmentComplianceDocument> GetByIdWithDataAsync(int departmentComplianceDocumentId) =>
			QueryFirstOrDefaultAsync<DepartmentComplianceDocument>($"SELECT * FROM {Tbl("DepartmentComplianceDocuments")} WHERE {Col("DepartmentComplianceDocumentId")} = {P}Id", new { Id = departmentComplianceDocumentId });

		public Task<IEnumerable<DepartmentComplianceDocument>> GetExpiringAsync(DateTime asOfUtc)
		{
			// ExpiresOn within the row's own lead window, or already past: the sweep decides which notification to send.
			var window = IsPostgres
				? $"{Col("ExpiresOn")} <= ({P}AsOf + ({Col("AlertLeadDays")} * INTERVAL '1 day'))"
				: $"{Col("ExpiresOn")} <= DATEADD(day, {Col("AlertLeadDays")}, {P}AsOf)";
			return QueryAsync<DepartmentComplianceDocument>($"SELECT {Cols(Meta)} FROM {Tbl("DepartmentComplianceDocuments")} WHERE {Col("IsDeleted")} = {False} AND {Col("ExpiresOn")} IS NOT NULL AND {window} ORDER BY {Col("DepartmentId")}, {Col("ExpiresOn")}", new { AsOf = DatabaseTimestamp(asOfUtc) });
		}
	}

	public class BidRepository : RmsRepositoryBase<Bid>, IBidRepository
	{
		public BidRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		private static string False => IsPostgres ? "FALSE" : "0";

		public Task<Bid> GetByIdForDepartmentAsync(string bidId, int departmentId) =>
			QueryFirstOrDefaultAsync<Bid>($"SELECT * FROM {Tbl("Bids")} WHERE {Col("BidId")} = {P}Id AND {Col("DepartmentId")} = {P}DepartmentId", new { Id = bidId, DepartmentId = departmentId });

		public Task<IEnumerable<Bid>> GetForDepartmentAsync(int departmentId, int? status, int skip, int take) =>
			QueryAsync<Bid>(
				$"SELECT * FROM {Tbl("Bids")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("IsDeleted")} = {False}" + (status.HasValue ? $" AND {Col("Status")} = {P}Status" : string.Empty) +
				$" ORDER BY {Col("BidNumber")} DESC {Paging()}",
				new { DepartmentId = departmentId, Status = status ?? 0, Skip = Math.Max(0, skip), Take = Math.Clamp(take, 1, 500) });

		public Task<int> CountForDepartmentAsync(int departmentId, int? status) =>
			ScalarAsync<int>($"SELECT COUNT(*) FROM {Tbl("Bids")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("IsDeleted")} = {False}" + (status.HasValue ? $" AND {Col("Status")} = {P}Status" : string.Empty), new { DepartmentId = departmentId, Status = status ?? 0 });

		public Task<IEnumerable<Bid>> GetByContactIdAsync(int departmentId, string contactId, int skip, int take) =>
			QueryAsync<Bid>($"SELECT * FROM {Tbl("Bids")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ContactId")} = {P}ContactId AND {Col("IsDeleted")} = {False} ORDER BY {Col("BidNumber")} DESC {Paging()}",
				new { DepartmentId = departmentId, ContactId = contactId, Skip = Math.Max(0, skip), Take = Math.Clamp(take, 1, 500) });

		public Task<IEnumerable<Bid>> GetByContractAsync(string serviceContractId) =>
			QueryAsync<Bid>($"SELECT * FROM {Tbl("Bids")} WHERE {Col("ServiceContractId")} = {P}Id AND {Col("IsDeleted")} = {False} ORDER BY {Col("BidNumber")} DESC", new { Id = serviceContractId });

		public Task<IEnumerable<Bid>> GetExpiryCandidatesAsync(DateTime asOfUtc) =>
			QueryAsync<Bid>($"SELECT * FROM {Tbl("Bids")} WHERE {Col("Status")} = {(int)BidStatuses.Submitted} AND {Col("IsDeleted")} = {False} AND {Col("ValidUntil")} IS NOT NULL AND {Col("ValidUntil")} < {P}AsOf", new { AsOf = DatabaseTimestamp(asOfUtc) });
	}

	public class BidLineItemRepository : RmsRepositoryBase<BidLineItem>, IBidLineItemRepository
	{
		public BidLineItemRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<IEnumerable<BidLineItem>> GetByBidAsync(string bidId) =>
			QueryAsync<BidLineItem>($"SELECT * FROM {Tbl("BidLineItems")} WHERE {Col("BidId")} = {P}Id ORDER BY {Col("SortOrder")}", new { Id = bidId });
	}

	public class BidNumberSequenceRepository : RmsRepositoryBase<BidNumberSequence>, IBidNumberSequenceRepository
	{
		public BidNumberSequenceRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<int> GetNextNumberAsync(int departmentId, CancellationToken cancellationToken = default)
		{
			// The invoice/time-report sequence precedent: one atomic statement that inserts the row on first use (handing out 1) or advances it.
			string sql;
			if (IsPostgres)
				sql = $"INSERT INTO {Tbl("BidNumberSequences")} ({Col("DepartmentId")}, {Col("NextBidNumber")}) VALUES ({P}DepartmentId, 2) " +
					  $"ON CONFLICT ({Col("DepartmentId")}) DO UPDATE SET {Col("NextBidNumber")} = {Tbl("BidNumberSequences")}.{Col("NextBidNumber")} + 1 " +
					  $"RETURNING {Col("NextBidNumber")} - 1";
			else
				sql = $"MERGE {Tbl("BidNumberSequences")} WITH (HOLDLOCK) AS t USING (SELECT {P}DepartmentId AS DepartmentId) AS s ON t.[DepartmentId] = s.DepartmentId " +
					  "WHEN MATCHED THEN UPDATE SET [NextBidNumber] = t.[NextBidNumber] + 1 " +
					  "WHEN NOT MATCHED THEN INSERT ([DepartmentId], [NextBidNumber]) VALUES (s.DepartmentId, 2) " +
					  "OUTPUT inserted.[NextBidNumber] - 1;";
			return ScalarAsync<int>(sql, new { DepartmentId = departmentId }, cancellationToken);
		}
	}
}
