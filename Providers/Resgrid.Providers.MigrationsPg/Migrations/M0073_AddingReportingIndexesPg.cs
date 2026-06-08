using FluentMigrator;
using FluentMigrator.Postgres;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL variant of the reporting/analytics support: pre-aggregated daily rollup table plus
	/// covering indexes on the source tables. All identifiers are lowercased per the PG convention.
	/// Index creation is guarded so it is safe if a matching index already exists.
	/// </summary>
	[Migration(73)]
	public class M0073_AddingReportingIndexesPg : Migration
	{
		public override void Up()
		{
			// Pre-aggregated daily rollup (DepartmentId null => system-wide row).
			Create.Table("ReportingDailyRollup".ToLower())
				.WithColumn("ReportingDailyRollupId".ToLower()).AsInt64().NotNullable().PrimaryKey().Identity()
				.WithColumn("DepartmentId".ToLower()).AsInt32().Nullable()
				.WithColumn("BucketDateUtc".ToLower()).AsDateTime2().NotNullable()
				.WithColumn("Metric".ToLower()).AsCustom("citext").NotNullable()
				.WithColumn("Dimension".ToLower()).AsCustom("citext").Nullable()
				.WithColumn("ItemCount".ToLower()).AsInt64().NotNullable().WithDefaultValue(0)
				.WithColumn("SumValue".ToLower()).AsDecimal(18, 4).Nullable()
				.WithColumn("MinValue".ToLower()).AsDecimal(18, 4).Nullable()
				.WithColumn("MaxValue".ToLower()).AsDecimal(18, 4).Nullable()
				.WithColumn("P50".ToLower()).AsDecimal(18, 4).Nullable()
				.WithColumn("P90".ToLower()).AsDecimal(18, 4).Nullable()
				.WithColumn("CreatedOnUtc".ToLower()).AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);

			Create.Index("IX_ReportingDailyRollup_Dept_Date_Metric".ToLower())
				.OnTable("ReportingDailyRollup".ToLower())
				.OnColumn("DepartmentId".ToLower()).Ascending()
				.OnColumn("BucketDateUtc".ToLower()).Ascending()
				.OnColumn("Metric".ToLower()).Ascending();

			// Source-table covering indexes for the live dashboard aggregates.
			if (!Schema.Table("Calls".ToLower()).Index("IX_Calls_DepartmentId_LoggedOn".ToLower()).Exists())
				Create.Index("IX_Calls_DepartmentId_LoggedOn".ToLower()).OnTable("Calls".ToLower())
					.OnColumn("DepartmentId".ToLower()).Ascending()
					.OnColumn("LoggedOn".ToLower()).Ascending();

			if (!Schema.Table("Calls".ToLower()).Index("IX_Calls_DepartmentId_Type".ToLower()).Exists())
				// Mirrors the SQL Server variant: carry Type as an INCLUDE (non-key) column instead
				// of an index key, keeping the index shape symmetric across dialects and avoiding a
				// key on a free-form text column whose values reach ~1189 chars. Still covers the
				// dashboard's per-department GROUP BY Type aggregation.
				Create.Index("IX_Calls_DepartmentId_Type".ToLower()).OnTable("Calls".ToLower())
					.OnColumn("DepartmentId".ToLower()).Ascending()
					.Include("Type".ToLower());

			if (!Schema.Table("Calls".ToLower()).Index("IX_Calls_DepartmentId_Priority".ToLower()).Exists())
				Create.Index("IX_Calls_DepartmentId_Priority".ToLower()).OnTable("Calls".ToLower())
					.OnColumn("DepartmentId".ToLower()).Ascending()
					.OnColumn("Priority".ToLower()).Ascending();

			if (!Schema.Table("Calls".ToLower()).Index("IX_Calls_DepartmentId_State".ToLower()).Exists())
				Create.Index("IX_Calls_DepartmentId_State".ToLower()).OnTable("Calls".ToLower())
					.OnColumn("DepartmentId".ToLower()).Ascending()
					.OnColumn("State".ToLower()).Ascending();

			if (!Schema.Table("ActionLogs".ToLower()).Index("IX_ActionLogs_Dept_User_Timestamp".ToLower()).Exists())
				Create.Index("IX_ActionLogs_Dept_User_Timestamp".ToLower()).OnTable("ActionLogs".ToLower())
					.OnColumn("DepartmentId".ToLower()).Ascending()
					.OnColumn("UserId".ToLower()).Ascending()
					.OnColumn("Timestamp".ToLower()).Descending();

			if (!Schema.Table("UnitStates".ToLower()).Index("IX_UnitStates_Unit_Timestamp".ToLower()).Exists())
				Create.Index("IX_UnitStates_Unit_Timestamp".ToLower()).OnTable("UnitStates".ToLower())
					.OnColumn("UnitId".ToLower()).Ascending()
					.OnColumn("Timestamp".ToLower()).Descending();

			if (!Schema.Table("Units".ToLower()).Index("IX_Units_DepartmentId".ToLower()).Exists())
				Create.Index("IX_Units_DepartmentId".ToLower()).OnTable("Units".ToLower())
					.OnColumn("DepartmentId".ToLower()).Ascending();

			if (!Schema.Table("DepartmentMembers".ToLower()).Index("IX_DepartmentMembers_DepartmentId".ToLower()).Exists())
				Create.Index("IX_DepartmentMembers_DepartmentId".ToLower()).OnTable("DepartmentMembers".ToLower())
					.OnColumn("DepartmentId".ToLower()).Ascending();
		}

		public override void Down()
		{
			if (Schema.Table("DepartmentMembers".ToLower()).Index("IX_DepartmentMembers_DepartmentId".ToLower()).Exists())
				Delete.Index("IX_DepartmentMembers_DepartmentId".ToLower()).OnTable("DepartmentMembers".ToLower());
			if (Schema.Table("Units".ToLower()).Index("IX_Units_DepartmentId".ToLower()).Exists())
				Delete.Index("IX_Units_DepartmentId".ToLower()).OnTable("Units".ToLower());
			if (Schema.Table("UnitStates".ToLower()).Index("IX_UnitStates_Unit_Timestamp".ToLower()).Exists())
				Delete.Index("IX_UnitStates_Unit_Timestamp".ToLower()).OnTable("UnitStates".ToLower());
			if (Schema.Table("ActionLogs".ToLower()).Index("IX_ActionLogs_Dept_User_Timestamp".ToLower()).Exists())
				Delete.Index("IX_ActionLogs_Dept_User_Timestamp".ToLower()).OnTable("ActionLogs".ToLower());
			if (Schema.Table("Calls".ToLower()).Index("IX_Calls_DepartmentId_State".ToLower()).Exists())
				Delete.Index("IX_Calls_DepartmentId_State".ToLower()).OnTable("Calls".ToLower());
			if (Schema.Table("Calls".ToLower()).Index("IX_Calls_DepartmentId_Priority".ToLower()).Exists())
				Delete.Index("IX_Calls_DepartmentId_Priority".ToLower()).OnTable("Calls".ToLower());
			if (Schema.Table("Calls".ToLower()).Index("IX_Calls_DepartmentId_Type".ToLower()).Exists())
				Delete.Index("IX_Calls_DepartmentId_Type".ToLower()).OnTable("Calls".ToLower());
			if (Schema.Table("Calls".ToLower()).Index("IX_Calls_DepartmentId_LoggedOn".ToLower()).Exists())
				Delete.Index("IX_Calls_DepartmentId_LoggedOn".ToLower()).OnTable("Calls".ToLower());

			Delete.Table("ReportingDailyRollup".ToLower());
		}
	}
}
