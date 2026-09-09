using System;
using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(196)]
	public class M0196_CompleteChecklistScheduling : Migration
	{
		private static string N(string value) => value;
		private static string Q(string value) => "[" + N(value) + "]";
		public override void Up()
		{
			Create.Table(N("ChecklistAccessFence")).WithColumn(N("Id")).AsInt32().PrimaryKey();
			Insert.IntoTable(N("ChecklistAccessFence")).Row(new System.Collections.Generic.Dictionary<string, object> { [N("Id")] = 1 });
			Alter.Table(N("ChecklistSchedules")).AddColumn(N("AssignmentType")).AsInt32().NotNullable().WithDefaultValue(0).AddColumn(N("AssignmentId")).AsString(128).Nullable();
			Alter.Table(N("DepartmentChecklistSettings")).AddColumn(N("NotifyAtShiftStart")).AsBoolean().NotNullable().WithDefaultValue(true)
				.AddColumn(N("FixedDigestMinute")).AsInt32().Nullable().AddColumn(N("LastDigestSweepUtc")).AsDateTime2().Nullable().AddColumn(N("DigestActiveFromUtc")).AsDateTime2().Nullable();
			Alter.Table(N("ChecklistReminders")).AddColumn(N("PeriodKey")).AsString(80).NotNullable().WithDefaultValue("");
			Delete.Index(N("UX_ChecklistReminders_Recipient")).OnTable(N("ChecklistReminders"));
			RecipientIndex(true);
		}
		private void RecipientIndex(bool period)
		{
			var index = Create.Index(N("UX_ChecklistReminders_Recipient")).OnTable(N("ChecklistReminders")).OnColumn(N("DepartmentId")).Ascending().OnColumn(N("OccurrenceId")).Ascending().OnColumn(N("RecipientUserId")).Ascending().OnColumn(N("Kind")).Ascending();
			if (period) index = index.OnColumn(N("PeriodKey")).Ascending();
			index.WithOptions().Unique();
		}
		public override void Down()
		{
			Execute.WithConnection((connection, transaction) =>
			{
				using var command = connection.CreateCommand(); command.Transaction = transaction;
				command.CommandText = $"SELECT (SELECT COUNT(*) FROM {Q("ChecklistSchedules")} WHERE {Q("AssignmentType")}<>0) + (SELECT COUNT(*) FROM {Q("ChecklistReminders")} WHERE {Q("PeriodKey")}<>'') + (SELECT COUNT(*) FROM {Q("DepartmentChecklistSettings")} WHERE {Q("DigestActiveFromUtc")} IS NOT NULL OR {Q("NotifyAtShiftStart")} = 0 OR {Q("LastDigestSweepUtc")} IS NOT NULL OR {Q("FixedDigestMinute")} IS NOT NULL)";
				if (Convert.ToInt64(command.ExecuteScalar()) > 0) throw new InvalidOperationException("Assignment and digest history must be preserved before rollback.");
			});
			Delete.Index(N("UX_ChecklistReminders_Recipient")).OnTable(N("ChecklistReminders")); RecipientIndex(false);
			Delete.Column(N("PeriodKey")).FromTable(N("ChecklistReminders"));
			foreach (var column in new[] { "NotifyAtShiftStart", "FixedDigestMinute", "LastDigestSweepUtc", "DigestActiveFromUtc" }) Delete.Column(N(column)).FromTable(N("DepartmentChecklistSettings"));
			Delete.Table(N("ChecklistAccessFence")); Delete.Column(N("AssignmentType")).FromTable(N("ChecklistSchedules")); Delete.Column(N("AssignmentId")).FromTable(N("ChecklistSchedules"));
		}
	}
}
