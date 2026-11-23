using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0329: per-location inventory minimums (par levels). One row per (department, item, location), soft
	/// deleted when removed; the minimum is an operational column and Content stays NULL. Same number, lower-case identifiers.
	/// Guarded for safe retry.
	/// </summary>
	[Migration(329)]
	public class M0329_AddInventoryParLevelsPg : Migration
	{
		private const string Table = "inventoryparlevels";

		public override void Up()
		{
			if (Schema.Table(Table).Exists())
				return;

			Create.Table(Table).WithColumn("id").AsString(36).NotNullable().PrimaryKey()
				.WithColumn("departmentid").AsInt32().NotNullable()
				.WithColumn("revision").AsInt32().NotNullable().WithDefaultValue(1)
				.WithColumn("createdon").AsDateTime2().NotNullable()
				.WithColumn("modifiedon").AsDateTime2().Nullable()
				.WithColumn("createdby").AsString(128).Nullable()
				.WithColumn("content").AsString(int.MaxValue).Nullable()
				.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("itemid").AsString(36).NotNullable()
				.WithColumn("locationid").AsString(36).NotNullable()
				.WithColumn("minimumquantity").AsDecimal(24, 6).NotNullable();

			Create.Index("ux_inventoryparlevels_tenantid").OnTable(Table).OnColumn("departmentid").Ascending().OnColumn("id").Ascending().WithOptions().Unique();
			Create.Index("ux_inventoryparlevels_itemlocation").OnTable(Table).OnColumn("departmentid").Ascending().OnColumn("itemid").Ascending().OnColumn("locationid").Ascending().WithOptions().Unique();
			Create.Index("ix_inventoryparlevels_location").OnTable(Table).OnColumn("departmentid").Ascending().OnColumn("locationid").Ascending();

			Create.ForeignKey("fk_inventoryparlevels_department").FromTable(Table).ForeignColumn("departmentid").ToTable("departments").PrimaryColumn("departmentid");
			Create.ForeignKey("fk_inventoryparlevels_itemid").FromTable(Table).ForeignColumns("departmentid", "itemid").ToTable("inventoryitems").PrimaryColumns("departmentid", "id");
			Create.ForeignKey("fk_inventoryparlevels_locationid").FromTable(Table).ForeignColumns("departmentid", "locationid").ToTable("inventorylocations").PrimaryColumns("departmentid", "id");

			Execute.Sql("ALTER TABLE inventoryparlevels ADD CONSTRAINT ck_inventoryparlevels_revision CHECK (revision >= 1);");
			Execute.Sql("ALTER TABLE inventoryparlevels ADD CONSTRAINT ck_inventoryparlevels_minimum CHECK (minimumquantity >= 0);");
			Execute.Sql("ALTER TABLE inventoryparlevels ADD CONSTRAINT ck_inventoryparlevels_content CHECK (content IS NULL);");
		}

		public override void Down()
		{
			if (Schema.Table(Table).Exists())
				Delete.Table(Table);
		}
	}
}
