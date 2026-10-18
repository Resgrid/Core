using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Department Import configuration (see the SQL Server M0281): importconfigurationchanges, the administrator's decided
	/// values for the Phase 2 configuration domains, one row per item and workspace. citext keys and strings, DepartmentId
	/// on every row, no foreign key to departments.
	/// </summary>
	[Migration(283)]
	public class M0283_AddImportConfigurationChangesPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("importconfigurationchanges").Exists())
			{
				Create.Table("importconfigurationchanges")
					.WithColumn("id").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("workspaceid").AsCustom("citext").NotNullable()
					.WithColumn("itemkey").AsCustom("citext").NotNullable()
					.WithColumn("origin").AsInt32().NotNullable()
					.WithColumn("originkey").AsCustom("citext").Nullable()
					.WithColumn("value").AsCustom("text").Nullable()
					.WithColumn("state").AsInt32().NotNullable()
					.WithColumn("approvedrunid").AsCustom("citext").Nullable()
					.WithColumn("appliedrunid").AsCustom("citext").Nullable()
					.WithColumn("failurecode").AsCustom("citext").Nullable()
					.WithColumn("staffauthored").AsBoolean().NotNullable()
					.WithColumn("revision").AsInt32().NotNullable()
					.WithColumn("createdby").AsCustom("citext").Nullable()
					.WithColumn("updatedby").AsCustom("citext").Nullable()
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("updatedon").AsDateTime2().NotNullable()
					.WithColumn("appliedon").AsDateTime2().Nullable();
			}

			if (!Schema.Table("importconfigurationchanges").Index("ux_importconfigurationchanges_workspaceid_itemkey").Exists())
			{
				Create.Index("ux_importconfigurationchanges_workspaceid_itemkey").OnTable("importconfigurationchanges")
					.OnColumn("departmentid").Ascending()
					.OnColumn("workspaceid").Ascending()
					.OnColumn("itemkey").Ascending()
					.WithOptions().Unique();
			}

			if (!Schema.Table("importconfigurationchanges").Index("ix_importconfigurationchanges_approvedrunid").Exists())
			{
				Create.Index("ix_importconfigurationchanges_approvedrunid").OnTable("importconfigurationchanges")
					.OnColumn("departmentid").Ascending()
					.OnColumn("approvedrunid").Ascending();
			}
		}

		public override void Down()
		{
			if (Schema.Table("importconfigurationchanges").Exists())
				Delete.Table("importconfigurationchanges");
		}
	}
}
