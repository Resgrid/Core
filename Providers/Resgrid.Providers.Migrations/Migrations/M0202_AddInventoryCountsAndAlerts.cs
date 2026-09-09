using System.Linq;
using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(202)]
	public class M0202_AddInventoryCountsAndAlerts : Migration
	{
		private static string N(string value) => value;
		private static readonly string[] Tables = { "InventoryCounts", "InventoryCountItems", "InventoryAlerts", "InventoryAlertDeliveries" };
		private const string TransactionItemIndex = "UX_InventoryTransactions_CountTenantItemId";
		private const string IssuanceItemIndex = "UX_InventoryIssuances_AlertTenantItemId";

		public override void Up()
		{
			foreach (var name in Tables)
			{
				var table = Create.Table(N(name)).WithColumn(N("Id")).AsString(36).NotNullable().PrimaryKey()
					.WithColumn(N("DepartmentId")).AsInt32().NotNullable()
					.WithColumn(N("Revision")).AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn(N("CreatedOn")).AsDateTime2().NotNullable()
					.WithColumn(N("ModifiedOn")).AsDateTime2().Nullable()
					.WithColumn(N("CreatedBy")).AsString(128).Nullable()
					.WithColumn(N("Content")).AsString(int.MaxValue).Nullable()
					.WithColumn(N("IsProtected")).AsBoolean().NotNullable().WithDefaultValue(false);
				switch (name)
				{
					case "InventoryCounts":
						table.WithColumn(N("IsDeleted")).AsBoolean().NotNullable().WithDefaultValue(false)
							.WithColumn(N("LocationId")).AsString(36).Nullable()
							.WithColumn(N("Status")).AsInt32().NotNullable().WithDefaultValue(0)
							.WithColumn(N("SnapshotFingerprint")).AsString(64).NotNullable()
							.WithColumn(N("SnapshotOn")).AsDateTime2().NotNullable()
							.WithColumn(N("CompletedOn")).AsDateTime2().Nullable()
							.WithColumn(N("OperationId")).AsString(36).Nullable();
						break;
					case "InventoryCountItems":
						table.WithColumn(N("CountId")).AsString(36).NotNullable()
							.WithColumn(N("ItemId")).AsString(36).NotNullable()
							.WithColumn(N("LocationId")).AsString(36).NotNullable()
							.WithColumn(N("LotId")).AsString(36).Nullable()
							.WithColumn(N("AssetId")).AsString(36).Nullable()
							.WithColumn(N("ExpectedQuantity")).AsDecimal(24, 6).NotNullable()
							.WithColumn(N("CountedQuantity")).AsDecimal(24, 6).Nullable()
							.WithColumn(N("TransactionId")).AsString(36).Nullable();
						break;
					case "InventoryAlerts":
						table.WithColumn(N("AlertType")).AsInt32().NotNullable()
							.WithColumn(N("DedupKey")).AsString(64).NotNullable()
							.WithColumn(N("ItemId")).AsString(36).NotNullable()
							.WithColumn(N("LocationId")).AsString(36).Nullable()
							.WithColumn(N("LotId")).AsString(36).Nullable()
							.WithColumn(N("AssetId")).AsString(36).Nullable()
							.WithColumn(N("IssuanceId")).AsString(36).Nullable()
							.WithColumn(N("Status")).AsInt32().NotNullable().WithDefaultValue(0)
							.WithColumn(N("OpenedOn")).AsDateTime2().NotNullable()
							.WithColumn(N("ResolvedOn")).AsDateTime2().Nullable()
							.WithColumn(N("DueOn")).AsDateTime2().Nullable()
							.WithColumn(N("Quantity")).AsDecimal(24, 6).Nullable();
						break;
					case "InventoryAlertDeliveries":
						table.WithColumn(N("AlertId")).AsString(36).NotNullable()
							.WithColumn(N("UserId")).AsString(128).NotNullable()
							.WithColumn(N("State")).AsInt32().NotNullable().WithDefaultValue(0)
							.WithColumn(N("NextAttemptOn")).AsDateTime2().NotNullable()
							.WithColumn(N("LeaseUntil")).AsDateTime2().Nullable()
							.WithColumn(N("ClaimToken")).AsString(36).Nullable()
							.WithColumn(N("AttemptCount")).AsInt32().NotNullable().WithDefaultValue(0)
							.WithColumn(N("HandedOffOn")).AsDateTime2().Nullable();
						break;
				}
				Index(name, "TenantId", true, "DepartmentId", "Id");
				Check(name, "Revision", "Revision >= 1");
				Create.ForeignKey(N("FK_" + name + "_Department")).FromTable(N(name)).ForeignColumn(N("DepartmentId"))
					.ToTable(N("Departments")).PrimaryColumn(N("DepartmentId"));
			}
			Index("InventoryCountItems", "TenantItemId", true, "DepartmentId", "ItemId", "Id");
			// Owned parent keys enforce the item as well as the tenant on historical backlinks.
			Index("InventoryTransactions", "CountTenantItemId", true, "DepartmentId", "ItemId", "Id");
			Index("InventoryIssuances", "AlertTenantItemId", true, "DepartmentId", "ItemId", "Id");
			Link("InventoryCounts", "LocationId", "InventoryLocations");
			Link("InventoryCounts", "OperationId", "InventoryOperations");
			Link("InventoryCountItems", "CountId", "InventoryCounts");
			foreach (var table in new[] { "InventoryCountItems", "InventoryAlerts" })
			{
				Link(table, "ItemId", "InventoryItems");
				Link(table, "LocationId", "InventoryLocations");
				ItemLink(table, "LotId", "InventoryLots");
				ItemLink(table, "AssetId", "InventoryAssets");
			}
			ItemLink("InventoryCountItems", "TransactionId", "InventoryTransactions");
			ItemLink("InventoryAlerts", "IssuanceId", "InventoryIssuances");
			Link("InventoryAlertDeliveries", "AlertId", "InventoryAlerts");
			// User identities are global; current department membership is checked before delivery.
			Create.ForeignKey(N("FK_InventoryAlertDeliveries_User")).FromTable(N("InventoryAlertDeliveries")).ForeignColumn(N("UserId"))
				.ToTable(N("AspNetUsers")).PrimaryColumn(N("Id"));

			Check("InventoryCounts", "Status", "Status BETWEEN 0 AND 3");
			Check("InventoryCounts", "Fingerprint", "LEN(SnapshotFingerprint) = 64");
			// Expected stock may be negative. An unentered observation remains NULL, not zero.
			Check("InventoryCountItems", "CountedQuantity", "CountedQuantity IS NULL OR CountedQuantity >= 0");
			Check("InventoryCountItems", "SerializedQuantity", "AssetId IS NULL OR (ExpectedQuantity IN (0,1) AND (CountedQuantity IS NULL OR CountedQuantity IN (0,1)))");
			Check("InventoryAlerts", "AlertType", "AlertType BETWEEN 0 AND 3");
			Check("InventoryAlerts", "Status", "Status IN (0,1)");
			Check("InventoryAlerts", "DedupKey", "LEN(DedupKey) = 64");
			Check("InventoryAlerts", "Content", "Content IS NULL");
			Check("InventoryAlertDeliveries", "State", "State BETWEEN 0 AND 3");
			Check("InventoryAlertDeliveries", "Attempts", "AttemptCount >= 0");
			Check("InventoryAlertDeliveries", "Content", "Content IS NULL");

			Index("InventoryCounts", "ScopeStatus", false, "DepartmentId", "LocationId", "Status", "SnapshotOn");
			Index("InventoryCountItems", "Count", false, "DepartmentId", "CountId", "Id");
			Index("InventoryAlerts", "ActiveFeed", false, "DepartmentId", "Status", "AlertType", "OpenedOn");
			Index("InventoryAlerts", "Item", false, "DepartmentId", "ItemId", "Status");
			Index("InventoryAlertDeliveries", "Recipient", true, "DepartmentId", "AlertId", "UserId");
			Index("InventoryAlertDeliveries", "Due", false, "DepartmentId", "State", "NextAttemptOn", "LeaseUntil");
			SpecialIndexes();

			// Each nonzero variance is tied to exactly one count line; the ledger remains immutable.
			Alter.Table(N("InventoryTransactions")).AddColumn(N("CountItemId")).AsString(36).Nullable();
			ItemLink("InventoryTransactions", "CountItemId", "InventoryCountItems");
			Check("InventoryTransactions", "CountReference", "CountItemId IS NULL OR (ReferenceType = 7 AND TransactionType = 7)");
			Execute.Sql("CREATE UNIQUE INDEX UX_InventoryTransactions_CountItem ON InventoryTransactions(DepartmentId,CountItemId) WHERE CountItemId IS NOT NULL;");
		}

		private void Link(string table, string column, string parent) =>
			Create.ForeignKey(N("FK_" + table + "_" + column)).FromTable(N(table)).ForeignColumns(N("DepartmentId"), N(column))
				.ToTable(N(parent)).PrimaryColumns(N("DepartmentId"), N("Id"));

		private void ItemLink(string table, string column, string parent) =>
			Create.ForeignKey(N("FK_" + table + "_" + column)).FromTable(N(table)).ForeignColumns(N("DepartmentId"), N("ItemId"), N(column))
				.ToTable(N(parent)).PrimaryColumns(N("DepartmentId"), N("ItemId"), N("Id"));

		private void Index(string table, string suffix, bool unique, params string[] columns)
		{
			var index = Create.Index(N((unique ? "UX_" : "IX_") + table + "_" + suffix)).OnTable(N(table)).OnColumn(N(columns[0])).Ascending();
			for (var i = 1; i < columns.Length; i++) index = index.OnColumn(N(columns[i])).Ascending();
			if (unique) index.WithOptions().Unique();
		}
		private void Check(string table, string suffix, string expression) =>
			Execute.Sql("ALTER TABLE " + N(table) + " ADD CONSTRAINT " + N("CK_" + table + "_" + suffix) + " CHECK (" + N(expression) + ");");

		private void SpecialIndexes()
		{
			Execute.Sql("ALTER TABLE InventoryCountItems ADD LotKey AS ISNULL(LotId, '00000000-0000-0000-0000-000000000000') PERSISTED, AssetKey AS ISNULL(AssetId, '00000000-0000-0000-0000-000000000000') PERSISTED; CREATE UNIQUE INDEX UX_InventoryCountItems_Position ON InventoryCountItems(DepartmentId,CountId,ItemId,LocationId,LotKey,AssetKey);");
			Execute.Sql("CREATE UNIQUE INDEX UX_InventoryCountItems_Transaction ON InventoryCountItems(DepartmentId,TransactionId) WHERE TransactionId IS NOT NULL;");
			Execute.Sql("CREATE UNIQUE INDEX UX_InventoryAlerts_Open ON InventoryAlerts(DepartmentId,DedupKey) WHERE Status = 0;");
		}

		public override void Down()
		{
			// Count observations and notification handoff evidence must survive an attempted code rollback.
			Execute.Sql("IF " + string.Join(" OR ", Tables.Select(t => "EXISTS (SELECT 1 FROM " + N(t) + ")"))
				+ " OR EXISTS (SELECT 1 FROM InventoryTransactions WHERE CountItemId IS NOT NULL) THROW 51000, 'Inventory count and alert data must be exported and removed through the authorized retention process before rollback.', 1;");
			Delete.ForeignKey(N("FK_InventoryTransactions_CountItemId")).OnTable(N("InventoryTransactions"));
			Delete.Index(N("UX_InventoryTransactions_CountItem")).OnTable(N("InventoryTransactions"));
			Execute.Sql("ALTER TABLE " + N("InventoryTransactions") + " DROP CONSTRAINT " + N("CK_InventoryTransactions_CountReference") + ";");
			Delete.Column(N("CountItemId")).FromTable(N("InventoryTransactions"));
			foreach (var table in Tables.Reverse()) Delete.Table(N(table));
			Delete.Index(N(TransactionItemIndex)).OnTable(N("InventoryTransactions"));
			Delete.Index(N(IssuanceItemIndex)).OnTable(N("InventoryIssuances"));
		}
	}
}
