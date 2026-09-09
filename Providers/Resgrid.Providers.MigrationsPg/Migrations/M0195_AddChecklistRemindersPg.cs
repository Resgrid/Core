using System;
using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(195)]
	public class M0195_AddChecklistRemindersPg : Migration
	{
		private static string N(string value) => value.ToLowerInvariant();
		private static string Q(string value) => "\"" + N(value) + "\"";
		public override void Up()
		{
			Alter.Table(N("DepartmentChecklistSettings"))
				.AddColumn(N("RemindersEnabled")).AsBoolean().NotNullable().WithDefaultValue(false)
				.AddColumn(N("NotifyBeforeMinutes")).AsInt32().NotNullable().WithDefaultValue(60)
				.AddColumn(N("NotifyMissed")).AsBoolean().NotNullable().WithDefaultValue(true)
				.AddColumn(N("DigestMode")).AsBoolean().NotNullable().WithDefaultValue(true)
				.AddColumn(N("EscalateAfterMinutes")).AsInt32().Nullable()
				.AddColumn(N("RemindersActiveFromUtc")).AsDateTime2().Nullable();
			Create.Table(N("ChecklistReminders"))
				.WithColumn(N("Id")).AsString(36).PrimaryKey()
				.WithColumn(N("DepartmentId")).AsInt32().NotNullable()
				.WithColumn(N("OccurrenceId")).AsString(36).NotNullable()
				.WithColumn(N("RecipientUserId")).AsString(128).NotNullable()
				.WithColumn(N("Kind")).AsInt32().NotNullable()
				.WithColumn(N("Status")).AsInt32().NotNullable()
				.WithColumn(N("CreatedOnUtc")).AsDateTime2().NotNullable()
				.WithColumn(N("NextAttemptUtc")).AsDateTime2().NotNullable()
				.WithColumn(N("Attempts")).AsInt32().NotNullable()
				.WithColumn(N("ClaimToken")).AsString(36).Nullable()
				.WithColumn(N("ClaimUntilUtc")).AsDateTime2().Nullable()
				.WithColumn(N("CompletedOnUtc")).AsDateTime2().Nullable();
			Create.ForeignKey(N("FK_ChecklistReminders_Occurrence")).FromTable(N("ChecklistReminders")).ForeignColumns(N("DepartmentId"), N("OccurrenceId")).ToTable(N("ChecklistOccurrences")).PrimaryColumns(N("DepartmentId"), N("Id"));
			Create.Index(N("UX_ChecklistReminders_Recipient")).OnTable(N("ChecklistReminders")).OnColumn(N("DepartmentId")).Ascending().OnColumn(N("OccurrenceId")).Ascending().OnColumn(N("RecipientUserId")).Ascending().OnColumn(N("Kind")).Ascending().WithOptions().Unique();
			Create.Index(N("IX_ChecklistReminders_Pending")).OnTable(N("ChecklistReminders")).OnColumn(N("DepartmentId")).Ascending().OnColumn(N("Status")).Ascending().OnColumn(N("NextAttemptUtc")).Ascending();
		}
		public override void Down()
		{
			Execute.WithConnection((connection, transaction) =>
			{
				using var command = connection.CreateCommand(); command.Transaction = transaction;
				command.CommandText = $"SELECT (SELECT COUNT(*) FROM {Q("ChecklistReminders")}) + (SELECT COUNT(*) FROM {Q("DepartmentChecklistSettings")} WHERE {Q("RemindersEnabled")}=TRUE OR {Q("NotifyBeforeMinutes")}<>60 OR {Q("NotifyMissed")}=FALSE OR {Q("DigestMode")}=FALSE OR {Q("EscalateAfterMinutes")} IS NOT NULL OR {Q("RemindersActiveFromUtc")} IS NOT NULL)";
				if (Convert.ToInt64(command.ExecuteScalar()) > 0) throw new InvalidOperationException("Reminder settings and delivery history must be preserved; populated reminder storage cannot be rolled back.");
			});
			Delete.Table(N("ChecklistReminders"));
			foreach (var column in new[] { "RemindersEnabled", "NotifyBeforeMinutes", "NotifyMissed", "DigestMode", "EscalateAfterMinutes", "RemindersActiveFromUtc" }) Delete.Column(N(column)).FromTable(N("DepartmentChecklistSettings"));
		}
	}
}
