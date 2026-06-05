using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Reporting/analytics support: a pre-aggregated daily rollup table for heavy analytics
	/// (response times, UHU, participation) plus covering indexes on the source tables that the
	/// live dashboard aggregates GROUP BY / filter on. Index creation is guarded so it is safe if a
	/// matching index already exists.
	/// </summary>
	[Migration(73)]
	public class M0073_AddingReportingIndexes : Migration
	{
		public override void Up()
		{
			// Pre-aggregated daily rollup (DepartmentId null => system-wide row).
			Create.Table("ReportingDailyRollup")
				.WithColumn("ReportingDailyRollupId").AsInt64().NotNullable().PrimaryKey().Identity()
				.WithColumn("DepartmentId").AsInt32().Nullable()
				.WithColumn("BucketDateUtc").AsDateTime2().NotNullable()
				.WithColumn("Metric").AsString(128).NotNullable()
				.WithColumn("Dimension").AsString(256).Nullable()
				.WithColumn("ItemCount").AsInt64().NotNullable().WithDefaultValue(0)
				.WithColumn("SumValue").AsDecimal(18, 4).Nullable()
				.WithColumn("MinValue").AsDecimal(18, 4).Nullable()
				.WithColumn("MaxValue").AsDecimal(18, 4).Nullable()
				.WithColumn("P50").AsDecimal(18, 4).Nullable()
				.WithColumn("P90").AsDecimal(18, 4).Nullable()
				.WithColumn("CreatedOnUtc").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);

			Create.Index("IX_ReportingDailyRollup_Dept_Date_Metric")
				.OnTable("ReportingDailyRollup")
				.OnColumn("DepartmentId").Ascending()
				.OnColumn("BucketDateUtc").Ascending()
				.OnColumn("Metric").Ascending();

			// Source-table covering indexes for the live dashboard aggregates.
			if (!Schema.Table("Calls").Index("IX_Calls_DepartmentId_LoggedOn").Exists())
				Create.Index("IX_Calls_DepartmentId_LoggedOn").OnTable("Calls")
					.OnColumn("DepartmentId").Ascending()
					.OnColumn("LoggedOn").Ascending();

			if (!Schema.Table("Calls").Index("IX_Calls_DepartmentId_Type").Exists())
				Create.Index("IX_Calls_DepartmentId_Type").OnTable("Calls")
					.OnColumn("DepartmentId").Ascending()
					.OnColumn("Type").Ascending();

			if (!Schema.Table("Calls").Index("IX_Calls_DepartmentId_Priority").Exists())
				Create.Index("IX_Calls_DepartmentId_Priority").OnTable("Calls")
					.OnColumn("DepartmentId").Ascending()
					.OnColumn("Priority").Ascending();

			if (!Schema.Table("Calls").Index("IX_Calls_DepartmentId_State").Exists())
				Create.Index("IX_Calls_DepartmentId_State").OnTable("Calls")
					.OnColumn("DepartmentId").Ascending()
					.OnColumn("State").Ascending();

			if (!Schema.Table("ActionLogs").Index("IX_ActionLogs_Dept_User_Timestamp").Exists())
				Create.Index("IX_ActionLogs_Dept_User_Timestamp").OnTable("ActionLogs")
					.OnColumn("DepartmentId").Ascending()
					.OnColumn("UserId").Ascending()
					.OnColumn("Timestamp").Descending();

			if (!Schema.Table("UnitStates").Index("IX_UnitStates_Unit_Timestamp").Exists())
				Create.Index("IX_UnitStates_Unit_Timestamp").OnTable("UnitStates")
					.OnColumn("UnitId").Ascending()
					.OnColumn("Timestamp").Descending();

			if (!Schema.Table("Units").Index("IX_Units_DepartmentId").Exists())
				Create.Index("IX_Units_DepartmentId").OnTable("Units")
					.OnColumn("DepartmentId").Ascending();

			if (!Schema.Table("DepartmentMembers").Index("IX_DepartmentMembers_DepartmentId").Exists())
				Create.Index("IX_DepartmentMembers_DepartmentId").OnTable("DepartmentMembers")
					.OnColumn("DepartmentId").Ascending();
		}

		public override void Down()
		{
			if (Schema.Table("DepartmentMembers").Index("IX_DepartmentMembers_DepartmentId").Exists())
				Delete.Index("IX_DepartmentMembers_DepartmentId").OnTable("DepartmentMembers");
			if (Schema.Table("Units").Index("IX_Units_DepartmentId").Exists())
				Delete.Index("IX_Units_DepartmentId").OnTable("Units");
			if (Schema.Table("UnitStates").Index("IX_UnitStates_Unit_Timestamp").Exists())
				Delete.Index("IX_UnitStates_Unit_Timestamp").OnTable("UnitStates");
			if (Schema.Table("ActionLogs").Index("IX_ActionLogs_Dept_User_Timestamp").Exists())
				Delete.Index("IX_ActionLogs_Dept_User_Timestamp").OnTable("ActionLogs");
			if (Schema.Table("Calls").Index("IX_Calls_DepartmentId_State").Exists())
				Delete.Index("IX_Calls_DepartmentId_State").OnTable("Calls");
			if (Schema.Table("Calls").Index("IX_Calls_DepartmentId_Priority").Exists())
				Delete.Index("IX_Calls_DepartmentId_Priority").OnTable("Calls");
			if (Schema.Table("Calls").Index("IX_Calls_DepartmentId_Type").Exists())
				Delete.Index("IX_Calls_DepartmentId_Type").OnTable("Calls");
			if (Schema.Table("Calls").Index("IX_Calls_DepartmentId_LoggedOn").Exists())
				Delete.Index("IX_Calls_DepartmentId_LoggedOn").OnTable("Calls");

			Delete.Table("ReportingDailyRollup");
		}
	}
}
