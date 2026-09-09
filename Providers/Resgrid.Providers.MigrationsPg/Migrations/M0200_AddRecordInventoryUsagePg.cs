using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(200)]
	public class M0200_AddRecordInventoryUsagePg : Migration
	{
		private const string Table = "RecordInventoryUsages";
		private static string N(string value) => value.ToLowerInvariant();

		public override void Up()
		{
			Create.Table(N(Table))
				.WithColumn(N("Id")).AsString(36).NotNullable().PrimaryKey()
				.WithColumn(N("DepartmentId")).AsInt32().NotNullable()
				.WithColumn(N("Revision")).AsInt32().NotNullable().WithDefaultValue(1)
				.WithColumn(N("CreatedOn")).AsDateTime2().NotNullable()
				.WithColumn(N("ModifiedOn")).AsDateTime2().Nullable()
				.WithColumn(N("CreatedBy")).AsString(128).Nullable()
				.WithColumn(N("Content")).AsString(int.MaxValue).Nullable()
				.WithColumn(N("IsProtected")).AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn(N("SourceType")).AsInt32().NotNullable()
				.WithColumn(N("SourceId")).AsString(128).NotNullable()
				.WithColumn(N("RecordKind")).AsInt32().Nullable()
				.WithColumn(N("CallId")).AsInt32().Nullable()
				.WithColumn(N("RmsRevisionId")).AsString(36).Nullable()
				.WithColumn(N("ItemId")).AsString(36).NotNullable()
				.WithColumn(N("AssetId")).AsString(36).Nullable()
				.WithColumn(N("LotId")).AsString(36).Nullable()
				.WithColumn(N("SourceLocationId")).AsString(36).NotNullable()
				.WithColumn(N("Quantity")).AsDecimal(24, 6).NotNullable()
				.WithColumn(N("UsageType")).AsInt32().NotNullable()
				.WithColumn(N("TransactionId")).AsString(36).NotNullable()
				.WithColumn(N("ReversesUsageId")).AsString(36).Nullable();

			Index("TenantId", true, "DepartmentId", "Id");
			Index("Transaction", true, "DepartmentId", "TransactionId");
			Index("Source", false, "DepartmentId", "SourceType", "SourceId", "RecordKind");
			Create.ForeignKey(N("FK_" + Table + "_Department")).FromTable(N(Table)).ForeignColumn(N("DepartmentId"))
				.ToTable(N("Departments")).PrimaryColumn(N("DepartmentId"));
			Link("ItemId", "InventoryItems");
			Link("SourceLocationId", "InventoryLocations");
			Link("TransactionId", "InventoryTransactions");
			Link("ReversesUsageId", Table);
			Create.ForeignKey(N("FK_" + Table + "_ItemAsset")).FromTable(N(Table)).ForeignColumns(N("DepartmentId"), N("ItemId"), N("AssetId"))
				.ToTable(N("InventoryAssets")).PrimaryColumns(N("DepartmentId"), N("ItemId"), N("Id"));
			Create.ForeignKey(N("FK_" + Table + "_ItemLot")).FromTable(N(Table)).ForeignColumns(N("DepartmentId"), N("ItemId"), N("LotId"))
				.ToTable(N("InventoryLots")).PrimaryColumns(N("DepartmentId"), N("ItemId"), N("Id"));
			// The source, call and revision are authorized soft references to independently retained records.
			Check("Revision", "Revision >= 1");
			Check("Quantity", "Quantity > 0");
			Check("UsageType", "UsageType BETWEEN 0 AND 3");
			Check("SourceKind", "(SourceType = 0 AND RecordKind IS NULL) OR (SourceType = 1 AND RecordKind IS NOT NULL AND RecordKind IN (1,2))");
			Check("Reversal", "ReversesUsageId IS NULL OR ReversesUsageId <> Id");
			Execute.Sql("CREATE UNIQUE INDEX ux_recordinventoryusages_reversal ON recordinventoryusages(departmentid,reversesusageid) WHERE reversesusageid IS NOT NULL;");
		}

		private void Link(string column, string parent) =>
			Create.ForeignKey(N("FK_" + Table + "_" + column)).FromTable(N(Table)).ForeignColumns(N("DepartmentId"), N(column))
				.ToTable(N(parent)).PrimaryColumns(N("DepartmentId"), N("Id"));

		private void Index(string suffix, bool unique, params string[] columns)
		{
			var index = Create.Index(N((unique ? "UX_" : "IX_") + Table + "_" + suffix)).OnTable(N(Table)).OnColumn(N(columns[0])).Ascending();
			for (var i = 1; i < columns.Length; i++) index = index.OnColumn(N(columns[i])).Ascending();
			if (unique) index.WithOptions().Unique();
		}

		private void Check(string suffix, string expression) =>
			Execute.Sql("ALTER TABLE " + N(Table) + " ADD CONSTRAINT " + N("CK_" + Table + "_" + suffix) + " CHECK (" + N(expression) + ");");

		public override void Down()
		{
			// Never erase the source-to-ledger provenance as a side effect of rolling back code.
			Execute.Sql("DO $guard$ BEGIN IF EXISTS (SELECT 1 FROM recordinventoryusages) THEN RAISE EXCEPTION 'Inventory usage must be exported and removed through the authorized retention process before rollback.'; END IF; END $guard$;");
			Delete.Table(N(Table));
		}
	}
}
