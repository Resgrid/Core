using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Widget-based home dashboard (home-dashboard-redesign-plan.md §5.2): members' saved boards (UserDashboards, one row per
	/// board per member per department; a member who never customizes has none) and department templates
	/// (DepartmentDashboardTemplates). Layouts are JSON. No foreign keys; the department purge removes the rows. Seeds the
	/// Dashboard.Modern flag OFF. Existence-guarded for safe retry.
	/// </summary>
	[Migration(307)]
	public class M0307_AddHomeDashboards : Migration
	{
		private const string FlagKey = "Dashboard.Modern";

		public override void Up()
		{
			if (!Schema.Table("UserDashboards").Exists())
			{
				Create.Table("UserDashboards")
					.WithColumn("UserDashboardId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("UserId").AsString(128).NotNullable()
					.WithColumn("Name").AsString(100).Nullable()
					.WithColumn("SortOrder").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("LayoutJson").AsString(int.MaxValue).Nullable()
					.WithColumn("SchemaVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("TemplateId").AsString(36).Nullable()
					.WithColumn("UseClassic").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("UpdatedOn").AsDateTime2().NotNullable()
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1);
				Execute.Sql("CREATE UNIQUE NONCLUSTERED INDEX IX_UserDashboards_DepartmentUserSort ON UserDashboards (DepartmentId, UserId, SortOrder);");
			}

			if (!Schema.Table("DepartmentDashboardTemplates").Exists())
			{
				Create.Table("DepartmentDashboardTemplates")
					.WithColumn("DepartmentDashboardTemplateId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("Name").AsString(100).NotNullable()
					.WithColumn("Description").AsString(500).Nullable()
					.WithColumn("Audience").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("TargetId").AsInt32().Nullable()
					.WithColumn("Priority").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("LayoutJson").AsString(int.MaxValue).Nullable()
					.WithColumn("SchemaVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("IsDefault").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("CreatedByUserId").AsString(128).Nullable()
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("UpdatedByUserId").AsString(128).Nullable()
					.WithColumn("UpdatedOn").AsDateTime2().NotNullable();
				Execute.Sql("CREATE NONCLUSTERED INDEX IX_DepartmentDashboardTemplates_Department ON DepartmentDashboardTemplates (DepartmentId);");
				Execute.Sql("CREATE UNIQUE NONCLUSTERED INDEX IX_DepartmentDashboardTemplates_Default ON DepartmentDashboardTemplates (DepartmentId) WHERE IsDefault = 1;");
			}

			// Seeded OFF: every member keeps the classic dashboard until the flag is enabled globally or per department.
			Execute.Sql(
				"IF NOT EXISTS (SELECT 1 FROM [FeatureFlags] WHERE [FlagKey] = '" + FlagKey + "') " +
				"INSERT INTO [FeatureFlags] ([FlagKey], [Name], [Description], [Category], [IsEnabledGlobally]) " +
				"VALUES ('" + FlagKey + "', " +
				"'Modern Home Dashboard', " +
				"'The widget-based home dashboard: per-member boards saved to the account, department templates, widgets for every module and add-on. Members can switch back to the classic dashboard. Seeded off; enable globally or per-department to roll out.', " +
				"'General', 0);");
		}

		public override void Down()
		{
			Delete.FromTable("FeatureFlags").Row(new { FlagKey = FlagKey });

			if (Schema.Table("DepartmentDashboardTemplates").Exists())
				Delete.Table("DepartmentDashboardTemplates");

			if (Schema.Table("UserDashboards").Exists())
				Delete.Table("UserDashboards");
		}
	}
}
