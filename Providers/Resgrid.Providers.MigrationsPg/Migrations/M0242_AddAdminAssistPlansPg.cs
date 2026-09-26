using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(242)]
	public class M0242_AddAdminAssistPlansPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("adminassistplans").Exists())
				Create.Table("adminassistplans")
					.WithColumn("id").AsString(36).PrimaryKey().NotNullable()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("userid").AsString(128).NotNullable()
					.WithColumn("createdonutc").AsDateTime().NotNullable()
					.WithColumn("updatedonutc").AsDateTime().NotNullable()
					.WithColumn("closedonutc").AsDateTime().Nullable()
					.WithColumn("revision").AsInt64().NotNullable()
					.WithColumn("status").AsString(32).NotNullable()
					.WithColumn("shared").AsBoolean().WithDefaultValue(false).NotNullable()
					.WithColumn("deleted").AsBoolean().WithDefaultValue(false).NotNullable()
					.WithColumn("content").AsString(int.MaxValue).NotNullable()
					.WithColumn("isprotected").AsBoolean().WithDefaultValue(false).NotNullable()
					.WithColumn("protectedcatalogversion").AsInt32().Nullable();
			if (!Schema.Table("adminassistplans").Index("ix_adminassistplans_scope").Exists())
				Create.Index("ix_adminassistplans_scope").OnTable("adminassistplans").OnColumn("departmentid").Ascending().OnColumn("updatedonutc").Descending();
		}
		public override void Down() => throw new System.NotSupportedException("Disable plans; protected proposals follow the approved data lifecycle.");
	}
}
