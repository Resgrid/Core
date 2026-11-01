using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Widget-based home dashboard (home-dashboard-redesign-plan.md §5.2): members' saved boards and department templates.
	/// PostgreSQL twin of the SQL Server migration; lower-case identifiers, citext keys and user ids, existence-guarded.
	/// Seeds the Dashboard.Modern flag OFF.
	/// </summary>
	[Migration(307)]
	public class M0307_AddHomeDashboardsPg : Migration
	{
		private const string FlagKey = "Dashboard.Modern";

		public override void Up()
		{
			if (!Schema.Table("userdashboards").Exists())
			{
				Create.Table("userdashboards")
					.WithColumn("userdashboardid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("userid").AsCustom("citext").NotNullable()
					.WithColumn("name").AsCustom("citext").Nullable()
					.WithColumn("sortorder").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("layoutjson").AsCustom("text").Nullable()
					.WithColumn("schemaversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("templateid").AsCustom("citext").Nullable()
					.WithColumn("useclassic").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("updatedon").AsDateTime2().NotNullable()
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1);
				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ix_userdashboards_departmentusersort ON userdashboards (departmentid, userid, sortorder);");
			}

			if (!Schema.Table("departmentdashboardtemplates").Exists())
			{
				Create.Table("departmentdashboardtemplates")
					.WithColumn("departmentdashboardtemplateid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("name").AsCustom("citext").NotNullable()
					.WithColumn("description").AsCustom("text").Nullable()
					.WithColumn("audience").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("targetid").AsInt32().Nullable()
					.WithColumn("priority").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("layoutjson").AsCustom("text").Nullable()
					.WithColumn("schemaversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("isdefault").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("createdbyuserid").AsCustom("citext").Nullable()
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("updatedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("updatedon").AsDateTime2().NotNullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_departmentdashboardtemplates_department ON departmentdashboardtemplates (departmentid);");
				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ix_departmentdashboardtemplates_default ON departmentdashboardtemplates (departmentid) WHERE isdefault;");
			}

			Execute.Sql(
				"INSERT INTO featureflags (flagkey, name, description, category, isenabledglobally) " +
				"SELECT '" + FlagKey + "', " +
				"'Modern Home Dashboard', " +
				"'The widget-based home dashboard: per-member boards saved to the account, department templates, widgets for every module and add-on. Members can switch back to the classic dashboard. Seeded off; enable globally or per-department to roll out.', " +
				"'General', false " +
				"WHERE NOT EXISTS (SELECT 1 FROM featureflags WHERE flagkey = '" + FlagKey + "');");
		}

		public override void Down()
		{
			Delete.FromTable("FeatureFlags".ToLower()).Row(new { flagkey = FlagKey });

			if (Schema.Table("departmentdashboardtemplates").Exists())
				Delete.Table("departmentdashboardtemplates");

			if (Schema.Table("userdashboards").Exists())
				Delete.Table("userdashboards");
		}
	}
}
