using System;
using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(194)]
	public class M0194_AddChecklistScheduling : Migration
	{
		private static string N(string value) => value;
		private static string Q(string value) => "[" + N(value) + "]";
		public override void Up()
		{
			Create.Table(N("ChecklistSchedules"))
				.WithColumn(N("Id")).AsString(36).PrimaryKey()
				.WithColumn(N("DepartmentId")).AsInt32().NotNullable()
				.WithColumn(N("ParentId")).AsString(36).NotNullable()
				.WithColumn(N("Content")).AsString(int.MaxValue).Nullable()
				.WithColumn(N("Revision")).AsInt32().NotNullable()
				.WithColumn(N("CreatedOn")).AsDateTime2().NotNullable()
				.WithColumn(N("UpdatedOn")).AsDateTime2().NotNullable()
				.WithColumn(N("CreatedBy")).AsString(128).NotNullable()
				.WithColumn(N("IsProtected")).AsBoolean().NotNullable()
				.WithColumn(N("VersionId")).AsString(36).NotNullable()
				.WithColumn(N("TargetType")).AsInt32().NotNullable()
				.WithColumn(N("TargetId")).AsString(128).NotNullable()
				.WithColumn(N("TargetGroupId")).AsInt32().Nullable()
				.WithColumn(N("Frequency")).AsInt32().NotNullable()
				.WithColumn(N("TimeZoneId")).AsString(128).NotNullable()
				.WithColumn(N("StartDate")).AsDate().NotNullable()
				.WithColumn(N("EndDate")).AsDate().Nullable()
				.WithColumn(N("ClockMinutes")).AsString(64).NotNullable()
				.WithColumn(N("Weekdays")).AsInt32().NotNullable()
				.WithColumn(N("DayOfMonth")).AsInt32().NotNullable()
				.WithColumn(N("MonthOfYear")).AsInt32().NotNullable()
				.WithColumn(N("WindowMinutes")).AsInt32().NotNullable()
				.WithColumn(N("WorkshiftId")).AsString(36).Nullable()
				.WithColumn(N("IsActive")).AsBoolean().NotNullable()
				.WithColumn(N("IsSuspended")).AsBoolean().NotNullable()
				.WithColumn(N("ActiveFromUtc")).AsDateTime2().NotNullable()
				.WithColumn(N("GeneratedThroughUtc")).AsDateTime2().NotNullable()
				.WithColumn(N("LastSweepUtc")).AsDateTime2().NotNullable();
			Create.Index(N("UX_ChecklistSchedules_TenantId")).OnTable(N("ChecklistSchedules")).OnColumn(N("DepartmentId")).Ascending().OnColumn(N("Id")).Ascending().WithOptions().Unique();
			Create.Index(N("IX_ChecklistSchedules_ParentDate")).OnTable(N("ChecklistSchedules")).OnColumn(N("DepartmentId")).Ascending().OnColumn(N("ParentId")).Ascending().OnColumn(N("CreatedOn")).Descending();
			Create.Index(N("IX_ChecklistSchedules_Active")).OnTable(N("ChecklistSchedules")).OnColumn(N("IsActive")).Ascending().OnColumn(N("DepartmentId")).Ascending();
			Foreign("ChecklistSchedules", "ParentId", "ChecklistDefinitions");
			Foreign("ChecklistSchedules", "VersionId", "ChecklistDefinitionVersions");
			Alter.Table(N("ChecklistOccurrences"))
				.AddColumn(N("ScheduleId")).AsString(36).Nullable()
				.AddColumn(N("ScheduleRevision")).AsInt32().Nullable()
				.AddColumn(N("PeriodStartUtc")).AsDateTime2().Nullable()
				.AddColumn(N("WindowEndUtc")).AsDateTime2().Nullable()
				.AddColumn(N("MissedOn")).AsDateTime2().Nullable();
			Foreign("ChecklistOccurrences", "ScheduleId", "ChecklistSchedules");
			Execute.Sql($"CREATE UNIQUE INDEX {Q("UX_ChecklistOccurrences_Period")} ON {Q("ChecklistOccurrences")} ({Q("DepartmentId")},{Q("ScheduleId")},{Q("ScheduleRevision")},{Q("PeriodStartUtc")}) WHERE {Q("ScheduleId")} IS NOT NULL");
			Create.Index(N("IX_ChecklistOccurrences_Due")).OnTable(N("ChecklistOccurrences")).OnColumn(N("DepartmentId")).Ascending().OnColumn(N("State")).Ascending().OnColumn(N("WindowEndUtc")).Ascending();
		}
		private void Foreign(string child, string column, string parent) => Create.ForeignKey(N("FK_" + child + "_" + column)).FromTable(N(child)).ForeignColumns(N("DepartmentId"), N(column)).ToTable(N(parent)).PrimaryColumns(N("DepartmentId"), N("Id"));
		public override void Down()
		{
			Execute.WithConnection((connection, transaction) =>
			{
				using var command = connection.CreateCommand(); command.Transaction = transaction;
				command.CommandText = $"SELECT (SELECT COUNT(*) FROM {Q("ChecklistSchedules")}) + (SELECT COUNT(*) FROM {Q("ChecklistOccurrences")} WHERE {Q("ScheduleId")} IS NOT NULL)";
				if (Convert.ToInt64(command.ExecuteScalar()) > 0) throw new InvalidOperationException("Scheduling history must be preserved; populated scheduling storage cannot be rolled back.");
			});
			Delete.ForeignKey(N("FK_ChecklistOccurrences_ScheduleId")).OnTable(N("ChecklistOccurrences"));
			Delete.Index(N("UX_ChecklistOccurrences_Period")).OnTable(N("ChecklistOccurrences"));
			Delete.Index(N("IX_ChecklistOccurrences_Due")).OnTable(N("ChecklistOccurrences"));
			foreach (var column in new[] { "ScheduleId", "ScheduleRevision", "PeriodStartUtc", "WindowEndUtc", "MissedOn" }) Delete.Column(N(column)).FromTable(N("ChecklistOccurrences"));
			Delete.Table(N("ChecklistSchedules"));
		}
	}
}
