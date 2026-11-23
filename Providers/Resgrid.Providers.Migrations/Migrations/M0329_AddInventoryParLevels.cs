using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Per-location minimums for inventory items (par levels, 2026-11-22 fix): low stock was only checked against the item's
	/// department-wide ReorderPoint/MinLevel, so a station that ran out while the warehouse was full raised nothing. One row per
	/// (department, item, location), soft deleted when removed and revived when set again. The minimum is an operational column,
	/// never Content: the table is not in InventoryTables.ProtectedContent and Content must stay NULL. Tenant-scoped foreign keys to
	/// the item and the location, like every other inventory child table. Guarded for safe retry.
	/// </summary>
	[Migration(329)]
	public class M0329_AddInventoryParLevels : Migration
	{
		private const string Table = "InventoryParLevels";

		public override void Up()
		{
			if (Schema.Table(Table).Exists())
				return;

			Create.Table(Table).WithColumn("Id").AsString(36).NotNullable().PrimaryKey()
				.WithColumn("DepartmentId").AsInt32().NotNullable()
				.WithColumn("Revision").AsInt32().NotNullable().WithDefaultValue(1)
				.WithColumn("CreatedOn").AsDateTime2().NotNullable()
				.WithColumn("ModifiedOn").AsDateTime2().Nullable()
				.WithColumn("CreatedBy").AsString(128).Nullable()
				.WithColumn("Content").AsString(int.MaxValue).Nullable()
				.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("ItemId").AsString(36).NotNullable()
				.WithColumn("LocationId").AsString(36).NotNullable()
				.WithColumn("MinimumQuantity").AsDecimal(24, 6).NotNullable();

			Create.Index("UX_InventoryParLevels_TenantId").OnTable(Table).OnColumn("DepartmentId").Ascending().OnColumn("Id").Ascending().WithOptions().Unique();
			Create.Index("UX_InventoryParLevels_ItemLocation").OnTable(Table).OnColumn("DepartmentId").Ascending().OnColumn("ItemId").Ascending().OnColumn("LocationId").Ascending().WithOptions().Unique();
			Create.Index("IX_InventoryParLevels_Location").OnTable(Table).OnColumn("DepartmentId").Ascending().OnColumn("LocationId").Ascending();

			Create.ForeignKey("FK_InventoryParLevels_Department").FromTable(Table).ForeignColumn("DepartmentId").ToTable("Departments").PrimaryColumn("DepartmentId");
			Create.ForeignKey("FK_InventoryParLevels_ItemId").FromTable(Table).ForeignColumns("DepartmentId", "ItemId").ToTable("InventoryItems").PrimaryColumns("DepartmentId", "Id");
			Create.ForeignKey("FK_InventoryParLevels_LocationId").FromTable(Table).ForeignColumns("DepartmentId", "LocationId").ToTable("InventoryLocations").PrimaryColumns("DepartmentId", "Id");

			Execute.Sql("ALTER TABLE InventoryParLevels ADD CONSTRAINT CK_InventoryParLevels_Revision CHECK (Revision >= 1);");
			Execute.Sql("ALTER TABLE InventoryParLevels ADD CONSTRAINT CK_InventoryParLevels_Minimum CHECK (MinimumQuantity >= 0);");
			Execute.Sql("ALTER TABLE InventoryParLevels ADD CONSTRAINT CK_InventoryParLevels_Content CHECK (Content IS NULL);");
		}

		public override void Down()
		{
			if (Schema.Table(Table).Exists())
				Delete.Table(Table);
		}
	}
}
