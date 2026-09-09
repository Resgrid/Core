using System;
using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(192)]
	public class M0192_ProtectChecklistOutcomes : Migration
	{
		private static string N(string value) => value;
		private static string Q(string value) => "[" + N(value) + "]";
		public override void Up()
		{
			foreach (var slot in new[] { ("ChecklistCompletions", "Score"), ("ChecklistCompletions", "Passed"), ("ChecklistCompletionItems", "IsFailure") })
				if (!Schema.Table(N(slot.Item1)).Column(N("Protected" + slot.Item2 + "Envelope")).Exists())
					Alter.Table(N(slot.Item1)).AddColumn(N("Protected" + slot.Item2 + "Envelope")).AsString(int.MaxValue).Nullable();
			Alter.Column(N("Passed")).OnTable(N("ChecklistCompletions")).AsBoolean().Nullable();
			Alter.Column(N("IsFailure")).OnTable(N("ChecklistCompletionItems")).AsBoolean().Nullable();
		}
		public override void Down()
		{
			// Never discard encrypted outcomes or invent a false result during rollback.
			Execute.WithConnection((connection, transaction) =>
			{
				using var command = connection.CreateCommand(); command.Transaction = transaction;
				command.CommandText = $"SELECT (SELECT COUNT(*) FROM {Q("ChecklistCompletions")} WHERE {Q("ProtectedScoreEnvelope")} IS NOT NULL OR {Q("ProtectedPassedEnvelope")} IS NOT NULL OR {Q("Passed")} IS NULL) + (SELECT COUNT(*) FROM {Q("ChecklistCompletionItems")} WHERE {Q("ProtectedIsFailureEnvelope")} IS NOT NULL OR {Q("IsFailure")} IS NULL)";
				if (Convert.ToInt64(command.ExecuteScalar()) != 0)
					throw new InvalidOperationException("Decrypt checklist outcomes through the authorized ADP process before rolling back migration 192.");
			});
			Alter.Column(N("Passed")).OnTable(N("ChecklistCompletions")).AsBoolean().NotNullable();
			Alter.Column(N("IsFailure")).OnTable(N("ChecklistCompletionItems")).AsBoolean().NotNullable();
			Delete.Column(N("ProtectedScoreEnvelope")).FromTable(N("ChecklistCompletions"));
			Delete.Column(N("ProtectedPassedEnvelope")).FromTable(N("ChecklistCompletions"));
			Delete.Column(N("ProtectedIsFailureEnvelope")).FromTable(N("ChecklistCompletionItems"));
		}
	}
}
