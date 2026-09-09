using System.Linq;
using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(198)]
	public class M0198_AddInventoryModernizationPg : Migration
	{
		private static string N(string value) => value.ToLowerInvariant();
		private static readonly string[] Tables = { "InventoryCategories", "InventoryItems", "InventoryLocations", "InventoryLots", "InventoryStocks", "InventoryAssets", "InventoryTransactions", "InventoryOperations", "InventoryTransfers", "InventoryTransferItems", "InventoryIssuances", "InventoryKits", "InventoryKitItems" };
		private static readonly (string Table, string Column, string Parent)[] Links =
		{
			("InventoryCategories", "ParentCategoryId", "InventoryCategories"), ("InventoryItems", "CategoryId", "InventoryCategories"),
			("InventoryLocations", "ContainerAssetId", "InventoryAssets"), ("InventoryLocations", "ParentLocationId", "InventoryLocations"),
			("InventoryLots", "ItemId", "InventoryItems"), ("InventoryStocks", "ItemId", "InventoryItems"), ("InventoryStocks", "LocationId", "InventoryLocations"),
			("InventoryAssets", "ItemId", "InventoryItems"), ("InventoryAssets", "CurrentLocationId", "InventoryLocations"),
			("InventoryTransactions", "OperationId", "InventoryOperations"), ("InventoryTransactions", "ItemId", "InventoryItems"),
			("InventoryTransactions", "FromLocationId", "InventoryLocations"), ("InventoryTransactions", "ToLocationId", "InventoryLocations"),
			("InventoryTransactions", "ReversesTransactionId", "InventoryTransactions"), ("InventoryTransactions", "IssuanceId", "InventoryIssuances"),
			("InventoryTransfers", "FromLocationId", "InventoryLocations"), ("InventoryTransfers", "ToLocationId", "InventoryLocations"), ("InventoryTransfers", "OperationId", "InventoryOperations"),
			("InventoryTransferItems", "TransferId", "InventoryTransfers"), ("InventoryTransferItems", "TransactionId", "InventoryTransactions"), ("InventoryTransferItems", "ItemId", "InventoryItems"),
			("InventoryIssuances", "ItemId", "InventoryItems"), ("InventoryIssuances", "LocationId", "InventoryLocations"), ("InventoryIssuances", "ReturnedToLocationId", "InventoryLocations"),
			("InventoryKitItems", "KitId", "InventoryKits"), ("InventoryKitItems", "ItemId", "InventoryItems")
		};
		private static readonly string[] LotChildren = { "InventoryStocks", "InventoryAssets", "InventoryTransactions", "InventoryTransferItems", "InventoryIssuances" };
		private static readonly string[] AssetChildren = { "InventoryTransactions", "InventoryTransferItems", "InventoryIssuances" };

		public override void Up()
		{
			foreach (var name in Tables)
			{
				var table = Create.Table(N(name)).WithColumn(N("Id")).AsString(36).NotNullable();
				if (name == "InventoryTransactions") table.WithColumn(N("EntryId")).AsInt64().PrimaryKey().Identity();
				else table.PrimaryKey();
				table.WithColumn(N("DepartmentId")).AsInt32().NotNullable()
					.WithColumn(N("Revision")).AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn(N("CreatedOn")).AsDateTime2().NotNullable()
					.WithColumn(N("ModifiedOn")).AsDateTime2().Nullable()
					.WithColumn(N("CreatedBy")).AsString(128).Nullable()
					.WithColumn(N("Content")).AsString(int.MaxValue).Nullable()
					.WithColumn(N("IsProtected")).AsBoolean().NotNullable().WithDefaultValue(false);
				if (name != "InventoryTransactions" && name != "InventoryOperations" && name != "InventoryTransferItems")
					table.WithColumn(N("IsDeleted")).AsBoolean().NotNullable().WithDefaultValue(false);
				switch (name)
				{
					case "InventoryCategories": table.WithColumn(N("ParentCategoryId")).AsString(36).Nullable(); break;
					case "InventoryItems":
						table.WithColumn(N("CategoryId")).AsString(36).Nullable().WithColumn(N("TrackingMode")).AsInt32().NotNullable()
							.WithColumn(N("IsKit")).AsBoolean().NotNullable().WithDefaultValue(false)
							.WithColumn(N("RequiresLotTracking")).AsBoolean().NotNullable().WithDefaultValue(false)
							.WithColumn(N("RequiresExpiration")).AsBoolean().NotNullable().WithDefaultValue(false)
							.WithColumn(N("IsControlledSubstance")).AsBoolean().NotNullable().WithDefaultValue(false)
							.WithColumn(N("IsActive")).AsBoolean().NotNullable().WithDefaultValue(true)
							.WithColumn(N("LegacyInventoryTypeId")).AsInt32().Nullable(); break;
					case "InventoryLocations":
						table.WithColumn(N("LocationType")).AsInt32().NotNullable().WithColumn(N("GroupId")).AsInt32().Nullable()
							.WithColumn(N("UnitId")).AsInt32().Nullable().WithColumn(N("UserId")).AsString(128).Nullable()
							.WithColumn(N("ContainerAssetId")).AsString(36).Nullable().WithColumn(N("ParentLocationId")).AsString(36).Nullable()
							.WithColumn(N("IsDefault")).AsBoolean().NotNullable().WithDefaultValue(false); break;
					case "InventoryLots":
						table.WithColumn(N("ItemId")).AsString(36).NotNullable().WithColumn(N("ExpiresOn")).AsDateTime2().Nullable()
							.WithColumn(N("ReceivedOn")).AsDateTime2().NotNullable(); break;
					case "InventoryStocks":
						table.WithColumn(N("ItemId")).AsString(36).NotNullable().WithColumn(N("LocationId")).AsString(36).NotNullable()
							.WithColumn(N("LotId")).AsString(36).Nullable().WithColumn(N("Quantity")).AsDecimal(24, 6).NotNullable(); break;
					case "InventoryAssets":
						table.WithColumn(N("ItemId")).AsString(36).NotNullable().WithColumn(N("LotId")).AsString(36).Nullable()
							.WithColumn(N("Status")).AsInt32().NotNullable().WithColumn(N("CurrentLocationId")).AsString(36).Nullable()
							.WithColumn(N("ExpiresOn")).AsDateTime2().Nullable().WithColumn(N("AcquiredOn")).AsDateTime2().Nullable(); break;
					case "InventoryTransactions":
						table.WithColumn(N("OperationId")).AsString(36).Nullable().WithColumn(N("LineNumber")).AsInt32().NotNullable()
							.WithColumn(N("TransactionType")).AsInt32().NotNullable().WithColumn(N("ItemId")).AsString(36).NotNullable()
							.WithColumn(N("AssetId")).AsString(36).Nullable().WithColumn(N("LotId")).AsString(36).Nullable()
							.WithColumn(N("FromLocationId")).AsString(36).Nullable().WithColumn(N("ToLocationId")).AsString(36).Nullable()
							.WithColumn(N("Quantity")).AsDecimal(24, 6).NotNullable()
							.WithColumn(N("FromQuantityBefore")).AsDecimal(24, 6).Nullable().WithColumn(N("FromQuantityAfter")).AsDecimal(24, 6).Nullable()
							.WithColumn(N("ToQuantityBefore")).AsDecimal(24, 6).Nullable().WithColumn(N("ToQuantityAfter")).AsDecimal(24, 6).Nullable()
							.WithColumn(N("OldStatus")).AsInt32().Nullable().WithColumn(N("NewStatus")).AsInt32().Nullable()
							.WithColumn(N("ReferenceType")).AsInt32().NotNullable().WithColumn(N("ReferenceId")).AsString(128).Nullable()
							.WithColumn(N("ReversesTransactionId")).AsString(36).Nullable().WithColumn(N("IssuanceId")).AsString(36).Nullable()
							.WithColumn(N("LegacyInventoryId")).AsInt32().Nullable().WithColumn(N("OccurredOn")).AsDateTime2().NotNullable(); break;
					case "InventoryOperations": table.WithColumn(N("State")).AsInt32().NotNullable().WithColumn(N("RequestId")).AsString(36).NotNullable().WithColumn(N("WitnessUserId")).AsString(128).Nullable(); break;
					case "InventoryTransfers":
						table.WithColumn(N("FromLocationId")).AsString(36).NotNullable().WithColumn(N("ToLocationId")).AsString(36).NotNullable()
							.WithColumn(N("Status")).AsInt32().NotNullable().WithColumn(N("OperationId")).AsString(36).NotNullable(); break;
					case "InventoryTransferItems":
						table.WithColumn(N("TransferId")).AsString(36).NotNullable().WithColumn(N("TransactionId")).AsString(36).NotNullable()
							.WithColumn(N("ItemId")).AsString(36).NotNullable().WithColumn(N("AssetId")).AsString(36).Nullable()
							.WithColumn(N("LotId")).AsString(36).Nullable().WithColumn(N("Quantity")).AsDecimal(24, 6).NotNullable(); break;
					case "InventoryIssuances":
						table.WithColumn(N("ItemId")).AsString(36).NotNullable().WithColumn(N("AssetId")).AsString(36).Nullable().WithColumn(N("LotId")).AsString(36).Nullable()
							.WithColumn(N("Quantity")).AsDecimal(24, 6).NotNullable().WithColumn(N("ReturnedQuantity")).AsDecimal(24, 6).NotNullable().WithDefaultValue(0)
							.WithColumn(N("IssuedToUserId")).AsString(128).Nullable().WithColumn(N("IssuedToUnitId")).AsInt32().Nullable()
							.WithColumn(N("LocationId")).AsString(36).NotNullable().WithColumn(N("ReturnedToLocationId")).AsString(36).Nullable()
							.WithColumn(N("IssuedOn")).AsDateTime2().NotNullable().WithColumn(N("ExpectedReturnOn")).AsDateTime2().Nullable().WithColumn(N("ReturnedOn")).AsDateTime2().Nullable()
							.WithColumn(N("Status")).AsInt32().NotNullable().WithColumn(N("ReferenceType")).AsInt32().NotNullable().WithColumn(N("ReferenceId")).AsString(128).Nullable(); break;
					case "InventoryKitItems":
						table.WithColumn(N("KitId")).AsString(36).NotNullable().WithColumn(N("ItemId")).AsString(36).NotNullable().WithColumn(N("Quantity")).AsDecimal(24, 6).NotNullable(); break;
				}
				Index(name, "TenantId", true, "DepartmentId", "Id");
				Check(name, "Revision", "Revision >= 1");
				Create.ForeignKey(N("FK_" + name + "_Department")).FromTable(N(name)).ForeignColumn(N("DepartmentId")).ToTable(N("Departments")).PrimaryColumn(N("DepartmentId"));
			}
			Index("InventoryTransactions", "PublicId", true, "Id");
			Index("InventoryLots", "TenantItemId", true, "DepartmentId", "ItemId", "Id");
			Index("InventoryAssets", "TenantItemId", true, "DepartmentId", "ItemId", "Id");
			foreach (var link in Links)
				Create.ForeignKey(N("FK_" + link.Table + "_" + link.Column)).FromTable(N(link.Table)).ForeignColumns(N("DepartmentId"), N(link.Column)).ToTable(N(link.Parent)).PrimaryColumns(N("DepartmentId"), N("Id"));
			foreach (var child in LotChildren)
				Create.ForeignKey(N("FK_" + child + "_ItemLot")).FromTable(N(child)).ForeignColumns(N("DepartmentId"), N("ItemId"), N("LotId")).ToTable(N("InventoryLots")).PrimaryColumns(N("DepartmentId"), N("ItemId"), N("Id"));
			foreach (var child in AssetChildren)
				Create.ForeignKey(N("FK_" + child + "_ItemAsset")).FromTable(N(child)).ForeignColumns(N("DepartmentId"), N("ItemId"), N("AssetId")).ToTable(N("InventoryAssets")).PrimaryColumns(N("DepartmentId"), N("ItemId"), N("Id"));
			Holder("InventoryLocations", "GroupId", "DepartmentGroups", "DepartmentGroupId");
			Holder("InventoryLocations", "UnitId", "Units", "UnitId");
			Holder("InventoryLocations", "UserId", "AspNetUsers", "Id");
			Holder("InventoryIssuances", "IssuedToUnitId", "Units", "UnitId");
			Holder("InventoryIssuances", "IssuedToUserId", "AspNetUsers", "Id");
			Check("InventoryItems", "TrackingMode", "TrackingMode IN (0,1)");
			Check("InventoryAssets", "Status", "Status BETWEEN 0 AND 6");
			Check("InventoryTransactions", "TypeQuantity", "TransactionType BETWEEN 0 AND 9 AND Quantity >= 0 AND (Quantity > 0 OR TransactionType IN (0,9))");
			Check("InventoryTransactions", "Line", "LineNumber >= 0");
			Check("InventoryTransfers", "Locations", "FromLocationId <> ToLocationId");
			Check("InventoryTransferItems", "Quantity", "Quantity > 0");
			Check("InventoryKitItems", "Quantity", "Quantity > 0");
			Check("InventoryCategories", "Parent", "ParentCategoryId IS NULL OR ParentCategoryId <> Id");
			Check("InventoryLocations", "Parent", "ParentLocationId IS NULL OR ParentLocationId <> Id");
			Check("InventoryLocations", "Holder", "(LocationType IN (0,5) AND GroupId IS NULL AND UnitId IS NULL AND UserId IS NULL AND ContainerAssetId IS NULL) OR (LocationType = 1 AND GroupId IS NOT NULL AND UnitId IS NULL AND UserId IS NULL AND ContainerAssetId IS NULL) OR (LocationType = 2 AND GroupId IS NULL AND UnitId IS NOT NULL AND UserId IS NULL AND ContainerAssetId IS NULL) OR (LocationType = 3 AND GroupId IS NULL AND UnitId IS NULL AND UserId IS NOT NULL AND ContainerAssetId IS NULL) OR (LocationType = 4 AND GroupId IS NULL AND UnitId IS NULL AND UserId IS NULL AND ContainerAssetId IS NOT NULL)");
			Check("InventoryIssuances", "Holder", "(IssuedToUserId IS NOT NULL AND IssuedToUnitId IS NULL) OR (IssuedToUserId IS NULL AND IssuedToUnitId IS NOT NULL)");
			Check("InventoryIssuances", "Quantity", "Quantity > 0 AND ReturnedQuantity >= 0 AND ReturnedQuantity <= Quantity");
			Check("InventoryIssuances", "Status", "Status BETWEEN 0 AND 4");
			Index("InventoryOperations", "Request", true, "DepartmentId", "RequestId");
			Index("InventoryTransactions", "History", false, "DepartmentId", "OccurredOn", "EntryId");
			Index("InventoryTransactions", "ItemHistory", false, "DepartmentId", "ItemId", "OccurredOn", "EntryId");
			Index("InventoryTransactions", "AssetHistory", false, "DepartmentId", "AssetId", "OccurredOn", "EntryId");
			Index("InventoryTransactions", "Reference", false, "DepartmentId", "ReferenceType", "ReferenceId", "EntryId");
			Index("InventoryAssets", "LocationStatus", false, "DepartmentId", "CurrentLocationId", "Status");
			Index("InventoryLocations", "Parent", false, "DepartmentId", "ParentLocationId");
			Index("InventoryLots", "Expiration", false, "DepartmentId", "ExpiresOn", "ItemId");
			Index("InventoryIssuances", "UnitHistory", false, "DepartmentId", "IssuedToUnitId", "IssuedOn", "ReturnedOn");
			Index("InventoryIssuances", "PersonStatus", false, "DepartmentId", "IssuedToUserId", "Status", "ExpectedReturnOn");
			Index("InventoryIssuances", "AssetHistory", false, "DepartmentId", "AssetId", "IssuedOn");
			Index("InventoryTransferItems", "Transfer", false, "DepartmentId", "TransferId");
			Index("InventoryTransfers", "History", false, "DepartmentId", "CreatedOn");
			SpecialIndexes();
		}
		private void Holder(string table, string column, string parent, string key)
		{
			// Existing unit tracking provides a tenant key; older minimal databases still get the typed FK.
			if (!Schema.Table(N(parent)).Exists()) return;
			if (parent == "Units" && Schema.Table(N(parent)).Constraint(N("UQ_Units_DepartmentId_UnitId")).Exists())
				Create.ForeignKey(N("FK_" + table + "_Holder_" + column)).FromTable(N(table)).ForeignColumns(N("DepartmentId"), N(column)).ToTable(N(parent)).PrimaryColumns(N("DepartmentId"), N(key));
			else Create.ForeignKey(N("FK_" + table + "_Holder_" + column)).FromTable(N(table)).ForeignColumn(N(column)).ToTable(N(parent)).PrimaryColumn(N(key));
		}
		private void Index(string table, string suffix, bool unique, params string[] columns)
		{
			var index = Create.Index(N((unique ? "UX_" : "IX_") + table + "_" + suffix)).OnTable(N(table)).OnColumn(N(columns[0])).Ascending();
			for (var i = 1; i < columns.Length; i++) index = index.OnColumn(N(columns[i])).Ascending();
			if (unique) index.WithOptions().Unique();
		}
		private void Check(string table, string suffix, string expression) => Execute.Sql("ALTER TABLE " + N(table) + " ADD CONSTRAINT " + N("CK_" + table + "_" + suffix) + " CHECK (" + expression + ");");
		private void SpecialIndexes()
		{
			Execute.Sql("CREATE UNIQUE INDEX ux_inventorystocks_balance ON inventorystocks(departmentid,itemid,locationid,(COALESCE(lotid, '00000000-0000-0000-0000-000000000000'))) WHERE isdeleted = false;");
			Execute.Sql("CREATE UNIQUE INDEX ux_inventoryitems_legacy ON inventoryitems(departmentid,legacyinventorytypeid) WHERE legacyinventorytypeid IS NOT NULL; CREATE UNIQUE INDEX ux_inventorytransactions_legacy ON inventorytransactions(departmentid,legacyinventoryid) WHERE legacyinventoryid IS NOT NULL; CREATE UNIQUE INDEX ux_inventorytransactions_operationline ON inventorytransactions(departmentid,operationid,linenumber) WHERE operationid IS NOT NULL;");
			Execute.Sql("CREATE UNIQUE INDEX ux_inventorylocations_default ON inventorylocations(departmentid) WHERE isdefault = true AND isdeleted = false; CREATE UNIQUE INDEX ux_inventorykititems_item ON inventorykititems(departmentid,kitid,itemid) WHERE isdeleted = false; CREATE UNIQUE INDEX ux_inventoryissuances_outstandingasset ON inventoryissuances(departmentid,assetid) WHERE assetid IS NOT NULL AND isdeleted = false AND status IN (0,2);");
			foreach (var column in new[] { "groupid", "unitid", "userid", "containerassetid" })
				Execute.Sql("CREATE UNIQUE INDEX ux_inventorylocations_" + column + " ON inventorylocations(departmentid," + column + ") WHERE " + column + " IS NOT NULL AND isdeleted = false;");
		}
		public override void Down()
		{
			// An operator must use the authorized retention path; rollback must never erase ledger/equipment evidence.
			Execute.Sql("DO $guard$ BEGIN IF " + string.Join(" OR ", Tables.Select(t => "EXISTS (SELECT 1 FROM " + N(t) + ")")) + " THEN RAISE EXCEPTION 'Inventory data must be exported and removed through the authorized retention process before rollback.'; END IF; END $guard$;");
			foreach (var child in AssetChildren) Delete.ForeignKey(N("FK_" + child + "_ItemAsset")).OnTable(N(child));
			foreach (var child in LotChildren) Delete.ForeignKey(N("FK_" + child + "_ItemLot")).OnTable(N(child));
			foreach (var link in Links) Delete.ForeignKey(N("FK_" + link.Table + "_" + link.Column)).OnTable(N(link.Table));
			foreach (var table in Tables.Reverse()) Delete.Table(N(table));
		}
	}
}
