using FluentMigrator;
using FluentMigrator.SqlServer;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Reporting/analytics support: a pre-aggregated daily rollup table for heavy analytics
	/// (response times, UHU, participation) plus covering indexes on the source tables that the
	/// live dashboard aggregates GROUP BY / filter on. Index creation is guarded so it is safe if a
	/// matching index already exists.
	/// </summary>
	[Migration(73, TransactionBehavior.None)]
	public class M0073_AddingReportingIndexes : Migration
	{
		public override void Up()
		{
			// Pre-aggregated daily rollup (DepartmentId null => system-wide row).
			// TransactionBehavior.None means each statement self-commits, so every create below is
			// guarded with an existence check to stay safe on a re-run after a partial apply
			// (e.g. when a later large index build times out and the migration is retried).
			if (!Schema.Table("ReportingDailyRollup").Exists())
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

			if (!Schema.Table("ReportingDailyRollup").Index("IX_ReportingDailyRollup_Dept_Date_Metric").Exists())
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
				// Calls.Type is nvarchar(max), which SQL Server cannot use as an index key column
				// (error 1919), and existing data reaches ~1189 chars so it cannot be narrowed to a
				// keyable width without truncation. Key on DepartmentId and carry Type as an INCLUDE
				// (non-key) column -- LOB columns are allowed there -- which still covers the
				// dashboard's per-department GROUP BY Type aggregation.
				Create.Index("IX_Calls_DepartmentId_Type").OnTable("Calls")
					.OnColumn("DepartmentId").Ascending()
					.Include("Type");

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

			if (Schema.Table("ReportingDailyRollup").Exists())
				Delete.Table("ReportingDailyRollup");
		}
	}
}
