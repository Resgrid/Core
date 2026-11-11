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

		public async Task<List<PoiImport>> GetRecentForDepartmentAsync(int departmentId, int max)
		{
			// The pending rows can be megabytes; the list never shows them.
			var rows = await QueryAsync<PoiImport>(
				$"SELECT {Cols("PoiImportId", "DepartmentId", "PoiTypeId", "PoiTypeName", "UserId", "FileName", "Status", "TotalRows", "SkippedCount", "ImportedCount", "GeocodedCount", "UnlocatedCount", "LookupFailedCount", "LookupNotConfigured", "NotTriedCount", "GeocodingLimitReached", "PendingRetryCount", "RetryAfter", "UnlocatedJson", "Error", "AddedOn", "StartedOn", "HeartbeatOn", "CompletedOn", "Attempts")} " +
				$"FROM {Tbl("PoiImports")} WHERE {Col("DepartmentId")} = {P}DepartmentId ORDER BY {Col("AddedOn")} DESC",
				new { DepartmentId = departmentId });

			return rows.Take(Math.Max(1, max)).ToList();
		}

		public async Task<List<PoiImport>> GetRunnableAsync(DateTime staleBefore, int max)
		{
			// A locating import waiting out a geocoder outage is released with RetryAfter set; it is not runnable before then.
			var rows = await QueryAsync<PoiImport>(
				$"SELECT {Cols("PoiImportId", "Status", "AddedOn")} FROM {Tbl("PoiImports")} " +
				$"WHERE {Col("Status")} = {P}Queued OR ({InList("Status", "Running")} AND ({Col("HeartbeatOn")} IS NULL OR {Col("HeartbeatOn")} < {P}StaleBefore) " +
				$"AND ({Col("RetryAfter")} IS NULL OR {Col("RetryAfter")} <= {P}Now)) " +
				$"ORDER BY {Col("AddedOn")}",
				new
				{
					Queued = (int)PoiImportStatus.Queued,
					Running = InListValue(new[] { (int)PoiImportStatus.Locating, (int)PoiImportStatus.Saving }),
					StaleBefore = DatabaseTimestamp(staleBefore),
					Now = DatabaseTimestamp(DateTime.UtcNow)
				});

			return rows.Take(Math.Max(1, max)).ToList();
		}

		public async Task<bool> TryClaimAsync(string poiImportId, int expectedStatus, int newStatus, DateTime staleBefore, CancellationToken cancellationToken = default)
		{
			var now = DatabaseTimestamp(DateTime.UtcNow);
			var claimed = await ExecuteAsync(
				$"UPDATE {Tbl("PoiImports")} SET {Col("Status")} = {P}NewStatus, {Col("HeartbeatOn")} = {P}Now, {Col("StartedOn")} = COALESCE({Col("StartedOn")}, {P}Now), {Col("Attempts")} = {Col("Attempts")} + 1, {Col("RetryAfter")} = NULL " +
				$"WHERE {Col("PoiImportId")} = {CaseInsensitive("Id")} AND {Col("Status")} = {P}Expected " +
				$"AND ({Col("Status")} = {P}Queued OR (({Col("HeartbeatOn")} IS NULL OR {Col("HeartbeatOn")} < {P}StaleBefore) AND ({Col("RetryAfter")} IS NULL OR {Col("RetryAfter")} <= {P}Now)))",
				new { Id = poiImportId, NewStatus = newStatus, Expected = expectedStatus, Queued = (int)PoiImportStatus.Queued, Now = now, StaleBefore = DatabaseTimestamp(staleBefore) },
				cancellationToken);

			return claimed == 1;
		}

		public Task HeartbeatAsync(string poiImportId, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync(
				$"UPDATE {Tbl("PoiImports")} SET {Col("HeartbeatOn")} = {P}Now WHERE {Col("PoiImportId")} = {CaseInsensitive("Id")}",
				new { Id = poiImportId, Now = DatabaseTimestamp(DateTime.UtcNow) }, cancellationToken);
		}

		public Task ReleaseAsync(string poiImportId, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync(
				$"UPDATE {Tbl("PoiImports")} SET {Col("HeartbeatOn")} = NULL WHERE {Col("PoiImportId")} = {CaseInsensitive("Id")} AND {InList("Status", "Running")}",
				new { Id = poiImportId, Running = InListValue(new[] { (int)PoiImportStatus.Locating, (int)PoiImportStatus.Saving }) }, cancellationToken);
		}

		public async Task<int> CountSavedPoisAsync(string poiImportId, CancellationToken cancellationToken = default)
		{
			return await QueryFirstOrDefaultAsync<int>(
				$"SELECT CAST(COUNT(*) AS INT) FROM {Tbl("Pois")} WHERE {Col("PoiImportId")} = {CaseInsensitive("Id")}",
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
