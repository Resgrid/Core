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
	/// <summary>Offline map packs (M0309; maplibre-mapping-routing-navigation-plan.md §19.4).</summary>
	public class OfflineMapPacksRepository : RmsRepositoryBase<OfflineMapPack>, IOfflineMapPacksRepository
	{
		public OfflineMapPacksRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<OfflineMapPack> GetByPackKeyAsync(string packKey)
		{
			return QueryFirstOrDefaultAsync<OfflineMapPack>(
				$"SELECT * FROM {Tbl("OfflineMapPacks")} WHERE {Col("PackKey")} = {CaseInsensitive("PackKey")}",
				new { PackKey = packKey });
		}

		public async Task<List<OfflineMapPack>> GetByPackKeysAsync(IReadOnlyCollection<string> packKeys)
		{
			if (packKeys == null || packKeys.Count == 0)
				return new List<OfflineMapPack>();

			var rows = new List<OfflineMapPack>();

			// A large response area lists a few hundred cells; keep each IN list well inside parameter limits.
			foreach (var chunk in packKeys.Distinct().Chunk(500))
			{
				rows.AddRange(await QueryAsync<OfflineMapPack>(
					$"SELECT * FROM {Tbl("OfflineMapPacks")} WHERE {CaseInsensitiveInList("PackKey", "PackKeys")}",
					new { PackKeys = InListValue(chunk) }));
			}

			return rows;
		}

		public async Task<List<OfflineMapPack>> GetForDepartmentAsync(int departmentId)
		{
			var rows = await QueryAsync<OfflineMapPack>(
				$"SELECT * FROM {Tbl("OfflineMapPacks")} WHERE {Col("DepartmentId")} = {P}DepartmentId",
				new { DepartmentId = departmentId });

			return rows.ToList();
		}

		public async Task<List<OfflineMapPack>> GetQueuedAsync(int max)
		{
			var rows = await QueryAsync<OfflineMapPack>(
				$"SELECT * FROM {Tbl("OfflineMapPacks")} WHERE {Col("Status")} = {P}Status ORDER BY {Col("AddedOn")}",
				new { Status = (int)OfflineMapPackStatus.Queued });

			return rows.Take(Math.Max(1, max)).ToList();
		}

		public async Task<bool> TryClaimAsync(string offlineMapPackId, CancellationToken cancellationToken = default)
		{
			var claimed = await ExecuteAsync(
				$"UPDATE {Tbl("OfflineMapPacks")} SET {Col("Status")} = {P}Building, {Col("Attempts")} = {Col("Attempts")} + 1, {Col("LastRequestedOn")} = COALESCE({Col("LastRequestedOn")}, {P}Now), {Col("BuiltOn")} = {P}Now " +
				$"WHERE {Col("OfflineMapPackId")} = {CaseInsensitive("Id")} AND {Col("Status")} = {P}Queued",
				new { Id = offlineMapPackId, Building = (int)OfflineMapPackStatus.Building, Queued = (int)OfflineMapPackStatus.Queued, Now = DateTime.UtcNow },
				cancellationToken);

			return claimed == 1;
		}

		public Task SetStorageHandleAsync(string offlineMapPackId, string storagePath, string filesJson, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync(
				$"UPDATE {Tbl("OfflineMapPacks")} SET {Col("StoragePath")} = {P}StoragePath, {Col("FilesJson")} = {P}FilesJson WHERE {Col("OfflineMapPackId")} = {CaseInsensitive("Id")}",
				new { Id = offlineMapPackId, StoragePath = storagePath, FilesJson = filesJson },
				cancellationToken);
		}

		public async Task<List<OfflineMapPack>> GetStuckBuildingAsync(DateTime olderThan)
		{
			// BuiltOn holds the claim time while a pack builds.
			var rows = await QueryAsync<OfflineMapPack>(
				$"SELECT * FROM {Tbl("OfflineMapPacks")} WHERE {Col("Status")} = {P}Status AND {Col("BuiltOn")} < {P}OlderThan",
				new { Status = (int)OfflineMapPackStatus.Building, OlderThan = olderThan });

			return rows.ToList();
		}

		public async Task<List<OfflineMapPack>> GetNotRequestedSinceAsync(DateTime cutoff, int max)
		{
			var rows = await QueryAsync<OfflineMapPack>(
				$"SELECT * FROM {Tbl("OfflineMapPacks")} WHERE {Col("Status")} <> {P}Building AND COALESCE({Col("LastRequestedOn")}, {Col("AddedOn")}) < {P}Cutoff",
				new { Building = (int)OfflineMapPackStatus.Building, Cutoff = cutoff });

			return rows.Take(Math.Max(1, max)).ToList();
		}

		public async Task<int> TouchAsync(IReadOnlyCollection<string> offlineMapPackIds, CancellationToken cancellationToken = default)
		{
			if (offlineMapPackIds == null || offlineMapPackIds.Count == 0)
				return 0;

			var touched = 0;
			foreach (var chunk in offlineMapPackIds.Distinct().Chunk(500))
			{
				touched += await ExecuteAsync(
					$"UPDATE {Tbl("OfflineMapPacks")} SET {Col("LastRequestedOn")} = {P}Now WHERE {CaseInsensitiveInList("OfflineMapPackId", "Ids")}",
					new { Ids = InListValue(chunk), Now = DateTime.UtcNow }, cancellationToken);
			}

			return touched;
		}

		public Task<int> DeleteForDepartmentAsync(int departmentId, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync(
				$"DELETE FROM {Tbl("OfflineMapPacks")} WHERE {Col("DepartmentId")} = {P}DepartmentId",
				new { DepartmentId = departmentId }, cancellationToken);
		}
	}
}
