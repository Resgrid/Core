using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Model.Inventories;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	/// <summary>Tenant-scoped inventory persistence. The service owns the transaction spanning ledger, stock, audit and outbox writes.</summary>
	public sealed class InventoryStore : RmsRepositoryBase<InventoryItem>, IInventoryStore
	{
		private const int PageSize = 501;
		private const int MaximumRelatedRows = 5000;
		private const string LegacyMigrationRequestId = "00000000-0000-0000-0000-000000000001";
		private static readonly HashSet<string> RelationshipColumns = new(StringComparer.Ordinal)
		{
			"ParentCategoryId", "CategoryId", "ContainerAssetId", "ParentLocationId", "ItemId", "LocationId", "LotId", "CurrentLocationId",
			"OperationId", "AssetId", "FromLocationId", "ToLocationId", "ReferenceId", "ReversesTransactionId", "IssuanceId", "TransferId",
			"TransactionId", "ReturnedToLocationId", "KitId", "RequestId", "IssuedToUserId", "UserId", "SourceId", "ReversesUsageId", "ContactId", "VendorId", "PurchaseOrderId", "PurchaseOrderItemId", "CountId", "CountItemId", "AlertId", "DedupKey"
		};

		public InventoryStore(IConnectionProvider connection, SqlConfiguration config, IUnitOfWork uow, IQueryFactory queries)
			: base(connection, config, uow, queries) { }

		private static string Table<T>() where T : InventoryRow => InventoryTables.All[typeof(T)];
		private static PropertyInfo[] Properties<T>() where T : InventoryRow => typeof(T).GetProperties()
			.Where(p => p.CanWrite && !Attribute.IsDefined(p, typeof(NotMappedAttribute))).ToArray();
		private static string[] Columns<T>() where T : InventoryRow => Properties<T>().Select(p => p.Name).ToArray();
		private static DynamicParameters Parameters<T>(T row) where T : InventoryRow
		{
			var parameters = new DynamicParameters();
			foreach (var property in Properties<T>())
			{
				var value = property.GetValue(row);
				parameters.Add(property.Name, value is DateTime date ? DatabaseTimestamp(date) : value);
			}
			return parameters;
		}
		private void Transaction()
		{
			if (UnitOfWork.Transaction == null) throw new InvalidOperationException("Inventory writes require an existing transaction.");
		}

		public Task LockDepartmentAsync(int departmentId) => LockRecordsDepartmentAsync(departmentId, default);
		public async Task<List<int>> AlertDepartmentsAsync(int afterDepartmentId) => (await QueryAsync<int>(
			$"SELECT DISTINCT {Col("DepartmentId")} FROM {Tbl("InventoryItems")} WHERE {Col("DepartmentId")}>@AfterDepartmentId ORDER BY {Col("DepartmentId")} {Paging()}",
			new { AfterDepartmentId = afterDepartmentId, Skip = 0, Take = 100 })).ToList();

		public Task<long> LastEntryAsync(int departmentId) => ScalarAsync<long>(
			$"SELECT COALESCE(MAX({Col("EntryId")}),0) FROM {Tbl("InventoryTransactions")} WHERE {Col("DepartmentId")}={P}DepartmentId",
			new { DepartmentId = departmentId });

		public Task<InventoryAlert> OpenAlertAsync(int departmentId, string dedupKey) => QueryFirstOrDefaultAsync<InventoryAlert>(
			$"SELECT {Cols(Columns<InventoryAlert>())} FROM {Tbl("InventoryAlerts")} WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("DedupKey")}={P}DedupKey AND {Col("Status")}=0",
			new { DepartmentId = departmentId, DedupKey = dedupKey });

		public Task<InventoryAlertDelivery> AlertDeliveryAsync(int departmentId, string alertId, string userId) => QueryFirstOrDefaultAsync<InventoryAlertDelivery>(
			$"SELECT {Cols(Columns<InventoryAlertDelivery>())} FROM {Tbl("InventoryAlertDeliveries")} WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("AlertId")}={P}AlertId AND {Col("UserId")}={P}UserId",
			new { DepartmentId = departmentId, AlertId = alertId, UserId = userId });

		public async Task<List<InventoryAlert>> OpenAlertsAsync(int departmentId, int skip = 0)
		{
			if (skip < 0) throw new ArgumentOutOfRangeException(nameof(skip));
			return (await QueryAsync<InventoryAlert>($"SELECT {Cols(Columns<InventoryAlert>())} FROM {Tbl("InventoryAlerts")} WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("Status")}=0 ORDER BY {Col("OpenedOn")},{Col("Id")} {Paging()}",
				new { DepartmentId = departmentId, Skip = skip, Take = PageSize })).ToList();
		}

		public Task<T> GetAsync<T>(int departmentId, string id) where T : InventoryRow => QueryFirstOrDefaultAsync<T>(
			$"SELECT {Cols(Columns<T>())} FROM {Tbl(Table<T>())} WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("Id")}={P}Id",
			new { DepartmentId = departmentId, Id = id });

		public async Task<List<InventoryAlert>> ClaimableAlertsAsync(int departmentId, string userId, DateTime now, int skip = 0)
		{
			if (string.IsNullOrWhiteSpace(userId) || skip < 0) throw new ArgumentException("A recipient and valid page are required.");
			var columns = string.Join(",", Columns<InventoryAlert>().Select(c => "a." + Col(c)));
			return (await QueryAsync<InventoryAlert>($@"SELECT {columns} FROM {Tbl("InventoryAlerts")} a
LEFT JOIN {Tbl("InventoryAlertDeliveries")} d ON d.{Col("DepartmentId")}=a.{Col("DepartmentId")} AND d.{Col("AlertId")}=a.{Col("Id")} AND d.{Col("UserId")}=@UserId
WHERE a.{Col("DepartmentId")}=@DepartmentId AND a.{Col("Status")}=0
AND (d.{Col("Id")} IS NULL OR (d.{Col("State")} NOT IN (2,3) AND d.{Col("NextAttemptOn")}<=@Now AND (d.{Col("LeaseUntil")} IS NULL OR d.{Col("LeaseUntil")}<=@Now)))
ORDER BY a.{Col("OpenedOn")},a.{Col("Id")} {Paging()}", new { DepartmentId = departmentId, UserId = userId, Now = DatabaseTimestamp(now), Skip = skip, Take = PageSize })).ToList();
		}

		public async Task<List<T>> RelatedManyAsync<T>(int departmentId, string column, IReadOnlyCollection<string> ids) where T : InventoryRow
		{
			if (column == null || column != "Id" && !RelationshipColumns.Contains(column) || !Properties<T>().Any(p => p.Name == column && p.PropertyType == typeof(string)))
				throw new ArgumentException("Invalid inventory relationship column.", nameof(column));
			if (ids == null || ids.Count > 500) throw new ArgumentException("A bounded set of inventory IDs is required.", nameof(ids));
			if (ids.Count == 0) return new List<T>();
			var predicate = IsPostgres ? $"{Col(column)}=ANY(@Ids)" : $"{Col(column)} IN @Ids";
			var rows = (await QueryAsync<T>($"SELECT {Cols(Columns<T>())} FROM {Tbl(Table<T>())} WHERE {Col("DepartmentId")}=@DepartmentId AND {predicate} ORDER BY {Col("Id")} {Paging()}",
				new { DepartmentId = departmentId, Ids = ids.ToArray(), Skip = 0, Take = MaximumRelatedRows + 1 })).ToList();
			if (rows.Count > MaximumRelatedRows) throw new InvalidOperationException("The inventory relationship exceeds the supported operation size.");
			return rows;
		}

		public async Task<List<T>> ListAsync<T>(int departmentId, int skip = 0) where T : InventoryRow
		{
			if (skip < 0) throw new ArgumentOutOfRangeException(nameof(skip));
			return (await QueryAsync<T>($"SELECT {Cols(Columns<T>())} FROM {Tbl(Table<T>())} WHERE {Col("DepartmentId")}={P}DepartmentId ORDER BY {Col("Id")} {Paging()}",
				new { DepartmentId = departmentId, Skip = skip, Take = PageSize })).ToList();
		}

		public async Task<List<T>> RelatedAsync<T>(int departmentId, string column, string id) where T : InventoryRow
		{
			// Callers can choose only reviewed, persisted relationship columns, never SQL expressions or content fields.
			if (column == null || !RelationshipColumns.Contains(column) || !Properties<T>().Any(p => p.Name == column && p.PropertyType == typeof(string)))
				throw new ArgumentException("Invalid inventory relationship column.", nameof(column));
			var rows = (await QueryAsync<T>($"SELECT {Cols(Columns<T>())} FROM {Tbl(Table<T>())} WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col(column)}={P}RelatedId ORDER BY {Col("Id")} {Paging()}",
				new { DepartmentId = departmentId, RelatedId = id, Skip = 0, Take = MaximumRelatedRows + 1 })).ToList();
			if (rows.Count > MaximumRelatedRows) throw new InvalidOperationException("The inventory relationship exceeds the supported operation size.");
			return rows;
		}

		public async Task<List<T>> QueryAsync<T>(int departmentId, InventoryQuery filter, int skip = 0) where T : InventoryRow
		{
			if (skip < 0) throw new ArgumentOutOfRangeException(nameof(skip));
			filter ??= new InventoryQuery();
			var conditions = new List<string> { $"{Col("DepartmentId")}={P}DepartmentId" };
			var parameters = new DynamicParameters(new { DepartmentId = departmentId, Skip = skip, Take = PageSize });
			void Equal(string column, string value)
			{
				if (value == null) return;
				if (!Properties<T>().Any(p => p.Name == column)) throw new ArgumentException("Unsupported inventory filter.");
				conditions.Add($"{Col(column)}={P}{column}"); parameters.Add(column, value);
			}
			Equal("ItemId", filter.ItemId); Equal("AssetId", filter.AssetId); Equal("IssuedToUserId", filter.IssuedToUserId); Equal("KitId", filter.KitId);
			Equal("SourceId", filter.SourceId);
			if (filter.SourceType.HasValue || filter.RecordKind.HasValue)
			{
				if (typeof(T) != typeof(RecordInventoryUsage)) throw new ArgumentException("Unsupported inventory source filter.");
				if (filter.SourceType.HasValue) { conditions.Add($"{Col("SourceType")}={P}SourceType"); parameters.Add("SourceType", filter.SourceType.Value); }
				if (filter.RecordKind.HasValue) { conditions.Add($"{Col("RecordKind")}={P}RecordKind"); parameters.Add("RecordKind", filter.RecordKind.Value); }
			}
			if (typeof(T) == typeof(InventoryTransaction) && filter.LocationId != null)
			{ conditions.Add($"({Col("FromLocationId")}={P}LocationId OR {Col("ToLocationId")}={P}LocationId)"); parameters.Add("LocationId", filter.LocationId); }
			else Equal(typeof(T) == typeof(RecordInventoryUsage) ? "SourceLocationId" : "LocationId", filter.LocationId);
			if (typeof(InventoryMutableRow).IsAssignableFrom(typeof(T))) conditions.Add($"{Col("IsDeleted")}={(IsPostgres ? "false" : "0")}");
			var order = typeof(T) == typeof(InventoryTransaction) ? $"{Col("EntryId")} DESC" : typeof(T) == typeof(InventoryCount) ? $"{Col("CreatedOn")} DESC,{Col("Id")}" : typeof(T) == typeof(InventoryAlert) ? $"{Col("Status")},{Col("OpenedOn")} DESC,{Col("Id")}" : Col("Id");
			return (await QueryAsync<T>($"SELECT {Cols(Columns<T>())} FROM {Tbl(Table<T>())} WHERE {string.Join(" AND ", conditions)} ORDER BY {order} {Paging()}", parameters)).ToList();
		}

		public async Task InsertAsync<T>(T row) where T : InventoryRow
		{
			Transaction();
			if (row == null || row.DepartmentId <= 0 || !Guid.TryParse(row.Id, out var id) || id == Guid.Empty) throw new ArgumentException("A tenant and stable inventory identity are required.", nameof(row));
			var columns = Columns<T>().Where(c => c != nameof(InventoryTransaction.EntryId)).ToArray();
			var values = string.Join(",", columns.Select(c => P + c));
			if (row is InventoryTransaction transaction)
			{
				if (transaction.EntryId != 0) throw new InvalidOperationException("Inventory ledger identities are allocated by the database.");
				transaction.EntryId = await ScalarAsync<long>($"INSERT INTO {Tbl(Table<T>())} ({Cols(columns)}) {(IsPostgres ? "" : "OUTPUT INSERTED.[EntryId]")} VALUES ({values}) {(IsPostgres ? "RETURNING entryid" : "")}", Parameters(row));
			}
			else if (await ExecuteAsync($"INSERT INTO {Tbl(Table<T>())} ({Cols(columns)}) VALUES ({values})", Parameters(row)) != 1)
				throw new InvalidOperationException("The inventory row could not be inserted.");
		}

		public async Task UpdateAsync<T>(T row, int expectedRevision) where T : InventoryRow
		{
			Transaction();
			if (row == null) throw new ArgumentNullException(nameof(row));
			if (row is InventoryTransaction or InventoryTransferItem or RecordInventoryUsage) throw new InvalidOperationException("Inventory ledger and usage evidence are immutable.");
			if (expectedRevision < 1 || expectedRevision == int.MaxValue || row.Revision != expectedRevision + 1) throw new ArgumentException("Inventory updates must advance the expected revision once.", nameof(expectedRevision));
			var columns = Columns<T>().Where(c => c is not "Id" and not "DepartmentId" and not "CreatedOn" and not "CreatedBy").ToArray();
			var parameters = Parameters(row); parameters.Add("ExpectedRevision", expectedRevision);
			if (await ExecuteAsync($"UPDATE {Tbl(Table<T>())} SET {string.Join(",", columns.Select(c => Col(c) + "=" + P + c))} WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("Id")}={P}Id AND {Col("Revision")}={P}ExpectedRevision", parameters) != 1)
				throw new InvalidOperationException("The inventory row changed or is unavailable.");
		}

		public Task<InventoryOperation> RequestAsync(int departmentId, string requestId) => QueryFirstOrDefaultAsync<InventoryOperation>(
			$"SELECT {Cols(Columns<InventoryOperation>())} FROM {Tbl("InventoryOperations")} WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("RequestId")}={P}RequestId", new { DepartmentId = departmentId, RequestId = requestId });

		public async Task<InventoryStock> ApplyStockDeltaAsync(int departmentId, string itemId, string locationId, string lotId, decimal delta, string userId)
		{
			Transaction();
			await LockDepartmentAsync(departmentId);
			var now = DateTime.UtcNow;
			var parameters = new { DepartmentId = departmentId, ItemId = itemId, LocationId = locationId, LotId = lotId, Delta = delta, Now = DatabaseTimestamp(now) };
			var columns = Columns<InventoryStock>();
			var output = IsPostgres ? "" : "OUTPUT " + string.Join(",", columns.Select(c => "INSERTED." + Col(c)));
			var stock = await QueryFirstOrDefaultAsync<InventoryStock>($"UPDATE {Tbl("InventoryStocks")} SET {Col("Quantity")}={Col("Quantity")}+{P}Delta,{Col("Revision")}={Col("Revision")}+1,{Col("ModifiedOn")}={P}Now,{Col("IsDeleted")}={(IsPostgres ? "false" : "0")} {output} WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("ItemId")}={P}ItemId AND {Col("LocationId")}={P}LocationId AND ({Col("LotId")}={P}LotId OR ({Col("LotId")} IS NULL AND {P}LotId IS NULL)) {(IsPostgres ? "RETURNING " + Cols(columns) : "")}", parameters);
			if (stock != null) return stock;
			// The department lock covers the missing-row race as well as every multi-location transfer.
			stock = new InventoryStock { DepartmentId = departmentId, ItemId = itemId, LocationId = locationId, LotId = lotId, Quantity = delta, CreatedOn = now, ModifiedOn = now, CreatedBy = userId };
			await InsertAsync(stock);
			return stock;
		}

		public Task<InventoryItem> LegacyItemAsync(int departmentId, int typeId) => QueryFirstOrDefaultAsync<InventoryItem>(
			$"SELECT {Cols(Columns<InventoryItem>())} FROM {Tbl("InventoryItems")} WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("LegacyInventoryTypeId")}={P}TypeId", new { DepartmentId = departmentId, TypeId = typeId });

		public Task<InventoryTransaction> LegacyTransactionAsync(int departmentId, int inventoryId) => QueryFirstOrDefaultAsync<InventoryTransaction>(
			$"SELECT {Cols(Columns<InventoryTransaction>())} FROM {Tbl("InventoryTransactions")} WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("LegacyInventoryId")}={P}InventoryId", new { DepartmentId = departmentId, InventoryId = inventoryId });

		public async Task<List<InventoryStockQuantity>> StockQuantitiesAsync(int departmentId, IReadOnlyCollection<string> itemIds)
		{
			if (itemIds == null || itemIds.Count > 500) throw new ArgumentException("A catalog page of item IDs is required.", nameof(itemIds));
			if (itemIds.Count == 0) return new List<InventoryStockQuantity>();
			var predicate = IsPostgres ? $"{Col("ItemId")}=ANY(@ItemIds)" : $"{Col("ItemId")} IN @ItemIds";
			return (await QueryAsync<InventoryStockQuantity>($"SELECT {Cols("ItemId", "LocationId")},SUM({Col("Quantity")}) AS {Col("Quantity")} FROM {Tbl("InventoryStocks")} WHERE {Col("DepartmentId")}=@DepartmentId AND {Col("IsDeleted")}={(IsPostgres ? "false" : "0")} AND {predicate} GROUP BY {Cols("ItemId", "LocationId")}",
				new { DepartmentId = departmentId, ItemIds = itemIds.ToArray() })).ToList();
		}

		public async Task<List<InventoryTransaction>> AssetHistoryAsync(int departmentId, IReadOnlyCollection<string> assetIds, DateTime at, bool after, int skip = 0)
		{
			if (assetIds == null || assetIds.Count > 500 || skip < 0) throw new ArgumentException("A bounded asset history query is required.");
			if (assetIds.Count == 0) return new List<InventoryTransaction>();
			var predicate = IsPostgres ? $"{Col("AssetId")}=ANY(@AssetIds)" : $"{Col("AssetId")} IN @AssetIds";
			return (await QueryAsync<InventoryTransaction>($"SELECT {Cols(Columns<InventoryTransaction>())} FROM {Tbl("InventoryTransactions")} WHERE {Col("DepartmentId")}=@DepartmentId AND {predicate} AND {Col("OccurredOn")} {(after ? ">" : "<=")} @At ORDER BY {Col("OccurredOn")},{Col("EntryId")} {Paging()}",
				new { DepartmentId = departmentId, AssetIds = assetIds.ToArray(), At = DatabaseTimestamp(at), Skip = skip, Take = PageSize })).ToList();
		}

		public async Task RebuildStocksAsync(int departmentId)
		{
			Transaction();
			await LockDepartmentAsync(departmentId);
			var parameters = new { DepartmentId = departmentId, Now = DatabaseTimestamp(DateTime.UtcNow) };
			await ExecuteAsync($"DELETE FROM {Tbl("InventoryStocks")} WHERE {Col("DepartmentId")}={P}DepartmentId", parameters);
			var columns = Cols("Id", "DepartmentId", "Revision", "CreatedOn", "ModifiedOn", "CreatedBy", "Content", "IsProtected", "IsDeleted", "ItemId", "LocationId", "LotId", "Quantity");
			var id = IsPostgres ? "gen_random_uuid()::text" : "CONVERT(varchar(36),NEWID())";
			var disabled = IsPostgres ? "false" : "0";
			string LedgerSide(string location, bool subtract) => $"SELECT t.{Col("ItemId")},t.{Col(location)} AS {Col("LocationId")},t.{Col("LotId")},{(subtract ? "-" : "")}t.{Col("Quantity")} AS {Col("Quantity")} FROM {Tbl("InventoryTransactions")} t JOIN {Tbl("InventoryItems")} i ON i.{Col("DepartmentId")}=t.{Col("DepartmentId")} AND i.{Col("Id")}=t.{Col("ItemId")} WHERE t.{Col("DepartmentId")}={P}DepartmentId AND i.{Col("TrackingMode")}={(int)InventoryTrackingMode.Bulk} AND t.{Col(location)} IS NOT NULL";
			await ExecuteAsync($"INSERT INTO {Tbl("InventoryStocks")} ({columns}) SELECT {id},{P}DepartmentId,1,{P}Now,{P}Now,NULL,NULL,{disabled},{disabled},s.{Col("ItemId")},s.{Col("LocationId")},s.{Col("LotId")},SUM(s.{Col("Quantity")}) FROM ({LedgerSide("FromLocationId", true)} UNION ALL {LedgerSide("ToLocationId", false)}) s GROUP BY s.{Col("ItemId")},s.{Col("LocationId")},s.{Col("LotId")}", parameters);
		}

		public async Task<bool> HasLegacyMigrationAsync(int departmentId) => await ScalarAsync<int>(
			$"SELECT COUNT(*) FROM {Tbl("InventoryOperations")} WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("RequestId")}={P}RequestId AND {Col("State")}=2", new { DepartmentId = departmentId, RequestId = LegacyMigrationRequestId }) > 0;
	}
}
