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
	/// <summary>Background POI imports (M0313).</summary>
	public class PoiImportsRepository : RmsRepositoryBase<PoiImport>, IPoiImportsRepository
	{
		public PoiImportsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<PoiImport>> GetRecentForDepartmentAsync(int departmentId, int max, string userId = null)
		{
			// The pending rows can be megabytes; the list never shows them.
			var byUser = userId != null ? $" AND {Col("UserId")} = {CaseInsensitive("UserId")}" : string.Empty;
			var rows = await QueryAsync<PoiImport>(
				$"SELECT {Cols("PoiImportId", "DepartmentId", "PoiTypeId", "PoiTypeName", "UserId", "FileName", "Status", "TotalRows", "SkippedCount", "ImportedCount", "GeocodedCount", "UnlocatedCount", "LookupFailedCount", "LookupNotConfigured", "NotTriedCount", "GeocodingLimitReached", "PendingRetryCount", "RetryAfter", "UnlocatedJson", "Error", "AddedOn", "StartedOn", "HeartbeatOn", "CompletedOn", "Attempts")} " +
				$"FROM {Tbl("PoiImports")} WHERE {Col("DepartmentId")} = {P}DepartmentId{byUser} ORDER BY {Col("AddedOn")} DESC, {Col("PoiImportId")} {Paging()}",
				new { DepartmentId = departmentId, UserId = userId, Skip = 0, Take = Math.Max(1, max) });

			return rows.ToList();
		}

		public async Task<List<PoiImport>> GetRunnableAsync(DateTime staleBefore, int max)
		{
			// A locating import waiting out a geocoder outage is released with RetryAfter set; it is not runnable before then.
			var rows = await QueryAsync<PoiImport>(
				$"SELECT {Cols("PoiImportId", "Status", "AddedOn")} FROM {Tbl("PoiImports")} " +
				$"WHERE {Col("Status")} = {P}Queued OR ({InList("Status", "Running")} AND ({Col("HeartbeatOn")} IS NULL OR {Col("HeartbeatOn")} < {P}StaleBefore) " +
				$"AND ({Col("RetryAfter")} IS NULL OR {Col("RetryAfter")} <= {P}Now)) " +
				$"ORDER BY {Col("AddedOn")}, {Col("PoiImportId")} {Paging()}",
				new
				{
					Queued = (int)PoiImportStatus.Queued,
					Running = InListValue(new[] { (int)PoiImportStatus.Locating, (int)PoiImportStatus.Saving }),
					StaleBefore = DatabaseTimestamp(staleBefore),
					Now = DatabaseTimestamp(DateTime.UtcNow),
					Skip = 0,
					Take = Math.Max(1, max)
				});

			return rows.ToList();
		}

		public async Task<bool> TryClaimAsync(string poiImportId, int expectedStatus, int newStatus, DateTime staleBefore, string claimToken, CancellationToken cancellationToken = default)
		{
			var now = DatabaseTimestamp(DateTime.UtcNow);
			var claimed = await ExecuteAsync(
				$"UPDATE {Tbl("PoiImports")} SET {Col("Status")} = {P}NewStatus, {Col("HeartbeatOn")} = {P}Now, {Col("StartedOn")} = COALESCE({Col("StartedOn")}, {P}Now), {Col("Attempts")} = {Col("Attempts")} + 1, {Col("RetryAfter")} = NULL, {Col("ClaimToken")} = {P}Token " +
				$"WHERE {Col("PoiImportId")} = {CaseInsensitive("Id")} AND {Col("Status")} = {P}Expected " +
				$"AND ({Col("Status")} = {P}Queued OR (({Col("HeartbeatOn")} IS NULL OR {Col("HeartbeatOn")} < {P}StaleBefore) AND ({Col("RetryAfter")} IS NULL OR {Col("RetryAfter")} <= {P}Now)))",
				new { Id = poiImportId, NewStatus = newStatus, Expected = expectedStatus, Queued = (int)PoiImportStatus.Queued, Now = now, StaleBefore = DatabaseTimestamp(staleBefore), Token = claimToken },
				cancellationToken);

			return claimed == 1;
		}

		public async Task<bool> HeartbeatAsync(string poiImportId, string claimToken, CancellationToken cancellationToken = default)
		{
			var updated = await ExecuteAsync(
				$"UPDATE {Tbl("PoiImports")} SET {Col("HeartbeatOn")} = {P}Now WHERE {Col("PoiImportId")} = {CaseInsensitive("Id")} AND {Col("ClaimToken")} = {CaseInsensitive("Token")}",
				new { Id = poiImportId, Token = claimToken, Now = DatabaseTimestamp(DateTime.UtcNow) }, cancellationToken);

			return updated == 1;
		}

		public Task ReleaseAsync(string poiImportId, string claimToken, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync(
				$"UPDATE {Tbl("PoiImports")} SET {Col("HeartbeatOn")} = NULL WHERE {Col("PoiImportId")} = {CaseInsensitive("Id")} AND {InList("Status", "Running")} " +
				$"AND {Col("ClaimToken")} = {CaseInsensitive("Token")}",
				new { Id = poiImportId, Token = claimToken, Running = InListValue(new[] { (int)PoiImportStatus.Locating, (int)PoiImportStatus.Saving }) }, cancellationToken);
		}

		public async Task<bool> SaveRunAsync(PoiImport import, string claimToken, CancellationToken cancellationToken = default)
		{
			DateTime? Timestamp(DateTime? value) => value.HasValue ? DatabaseTimestamp(value.Value) : (DateTime?)null;

			var updated = await ExecuteAsync(
				$"UPDATE {Tbl("PoiImports")} SET {Col("Status")} = {P}Status, {Col("ImportedCount")} = {P}ImportedCount, {Col("GeocodedCount")} = {P}GeocodedCount, " +
				$"{Col("UnlocatedCount")} = {P}UnlocatedCount, {Col("LookupFailedCount")} = {P}LookupFailedCount, {Col("LookupNotConfigured")} = {P}LookupNotConfigured, " +
				$"{Col("NotTriedCount")} = {P}NotTriedCount, {Col("GeocodingLimitReached")} = {P}GeocodingLimitReached, {Col("PendingRetryCount")} = {P}PendingRetryCount, " +
				$"{Col("RetryAfter")} = {P}RetryAfter, {Col("RowsJson")} = {P}RowsJson, {Col("UnlocatedJson")} = {P}UnlocatedJson, {Col("Error")} = {P}Error, " +
				$"{Col("HeartbeatOn")} = {P}HeartbeatOn, {Col("CompletedOn")} = {P}CompletedOn, {Col("Attempts")} = {P}Attempts " +
				$"WHERE {Col("PoiImportId")} = {CaseInsensitive("Id")} AND {Col("ClaimToken")} = {CaseInsensitive("Token")}",
				new
				{
					Id = import.PoiImportId,
					Token = claimToken,
					import.Status,
					import.ImportedCount,
					import.GeocodedCount,
					import.UnlocatedCount,
					import.LookupFailedCount,
					import.LookupNotConfigured,
					import.NotTriedCount,
					import.GeocodingLimitReached,
					import.PendingRetryCount,
					RetryAfter = Timestamp(import.RetryAfter),
					import.RowsJson,
					import.UnlocatedJson,
					import.Error,
					HeartbeatOn = Timestamp(import.HeartbeatOn),
					CompletedOn = Timestamp(import.CompletedOn),
					import.Attempts
				},
				cancellationToken);

			return updated == 1;
		}

		public async Task<int> CountSavedPoisAsync(string poiImportId, CancellationToken cancellationToken = default)
		{
			return await QueryFirstOrDefaultAsync<int>(
				$"SELECT CAST(COUNT(*) AS INT) FROM {Tbl("Pois")} WHERE {Col("PoiImportId")} = {CaseInsensitive("Id")}",
				new { Id = poiImportId }, cancellationToken);
		}

		public async Task<int?> GetLastSavedRowAsync(string poiImportId, CancellationToken cancellationToken = default)
		{
			return await QueryFirstOrDefaultAsync<int?>(
				$"SELECT MAX({Col("PoiImportRow")}) FROM {Tbl("Pois")} WHERE {Col("PoiImportId")} = {CaseInsensitive("Id")}",
				new { Id = poiImportId }, cancellationToken);
		}

		public Task<int> DeleteFinishedBeforeAsync(DateTime cutoff, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync(
				$"DELETE FROM {Tbl("PoiImports")} WHERE {InList("Status", "Finished")} AND COALESCE({Col("CompletedOn")}, {Col("AddedOn")}) < {P}Cutoff",
				new { Finished = InListValue(new[] { (int)PoiImportStatus.Complete, (int)PoiImportStatus.Failed }), Cutoff = DatabaseTimestamp(cutoff) },
				cancellationToken);
		}
	}
}
