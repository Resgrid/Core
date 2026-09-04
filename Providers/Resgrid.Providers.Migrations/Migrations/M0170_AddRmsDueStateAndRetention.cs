using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Due-state tracking and legal holds (RMS plan section 4.7 and RMS-3, registry M0170).
	/// <list type="bullet">
	/// <item>RmsRecordDueStates — one row per (Record, obligation). The plan requires that the "at most once per
	/// record/due-state transition" guarantee be carried by a persisted row rather than inferred from the last
	/// worker run, so worker 42 compares against LastEmittedState and never against a timestamp.</item>
	/// <item>RmsRecordLegalHolds — holds by Record, by definition, or by date range. A hold only ever prevents a
	/// purge; it never deletes or edits, and a refused purge is audited.</item>
	/// </list>
	/// Existence-guarded for safe retry.
	/// </summary>
	[Migration(170)]
	public class M0170_AddRmsDueStateAndRetention : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("RmsRecordDueStates").Exists())
			{
				Create.Table("RmsRecordDueStates")
					.WithColumn("RmsRecordDueStateId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("RecordId").AsString(36).NotNullable()
					.WithColumn("RecordKind").AsInt32().NotNullable()
					.WithColumn("Obligation").AsInt32().NotNullable()
					.WithColumn("DueOn").AsDateTime2().Nullable()
					.WithColumn("LastEmittedState").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("LastEmittedOn").AsDateTime2().Nullable()
					.WithColumn("ResponsibleUserId").AsString(128).Nullable()
					.WithColumn("OverdueCount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(1L);

				// One row per obligation per Record: the uniqueness is what makes the emission guarantee hold.
				Execute.Sql("CREATE UNIQUE NONCLUSTERED INDEX UX_RmsRecordDueStates_Record_Obligation ON RmsRecordDueStates (DepartmentId, RecordId, Obligation);");
				Create.Index("IX_RmsRecordDueStates_Department_Due").OnTable("RmsRecordDueStates")
					.OnColumn("DepartmentId").Ascending().OnColumn("DueOn").Ascending();
			}

			if (!Schema.Table("RmsRecordLegalHolds").Exists())
			{
				Create.Table("RmsRecordLegalHolds")
					.WithColumn("RmsRecordLegalHoldId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("RecordId").AsString(36).Nullable()
					.WithColumn("DefinitionKey").AsString(100).Nullable()
					.WithColumn("PeriodStart").AsDateTime2().Nullable()
					.WithColumn("PeriodEnd").AsDateTime2().Nullable()
					.WithColumn("Reason").AsString(50).Nullable()
					.WithColumn("ReferenceNumber").AsString(100).Nullable()
					.WithColumn("Notes").AsString(int.MaxValue).Nullable()
					.WithColumn("PlacedByUserId").AsString(128).Nullable()
					.WithColumn("PlacedOn").AsDateTime2().NotNullable()
					.WithColumn("ReleasedByUserId").AsString(128).Nullable()
					.WithColumn("ReleasedOn").AsDateTime2().Nullable()
					.WithColumn("ReleaseNotes").AsString(int.MaxValue).Nullable()
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(1L);

				Create.Index("IX_RmsRecordLegalHolds_Department_Released").OnTable("RmsRecordLegalHolds")
					.OnColumn("DepartmentId").Ascending().OnColumn("ReleasedOn").Ascending();
				Create.Index("IX_RmsRecordLegalHolds_Department_Record").OnTable("RmsRecordLegalHolds")
					.OnColumn("DepartmentId").Ascending().OnColumn("RecordId").Ascending();
			}

			// A purged Record keeps its row, its number and its history and loses only its content (plan 4.9), so
			// the tombstone needs a column that says so; "empty" and "purged" must not look the same.
			if (Schema.Table("RmsOperationalRecords").Exists() && !Schema.Table("RmsOperationalRecords").Column("PurgedOn").Exists())
				Alter.Table("RmsOperationalRecords").AddColumn("PurgedOn").AsDateTime2().Nullable();

			if (Schema.Table("RmsIncidentReports").Exists() && !Schema.Table("RmsIncidentReports").Column("PurgedOn").Exists())
				Alter.Table("RmsIncidentReports").AddColumn("PurgedOn").AsDateTime2().Nullable();
		}

		public override void Down()
		{
			foreach (var table in new[] { "RmsRecordLegalHolds", "RmsRecordDueStates" })
			{
				if (Schema.Table(table).Exists())
					Delete.Table(table);
			}
		}
	}
}
