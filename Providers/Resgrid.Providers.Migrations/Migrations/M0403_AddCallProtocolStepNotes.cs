using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Round 8 (ADP for Protocols + Training/LMS, product decision 2026-11-25): CallProtocolStepNotes, the free-text note of a step-through event
	/// (P3) in its own row, so the step log on CallProtocols stays operational and appendable by clients that never decrypt while the note is
	/// sealed under ADP catalog 39 (Calls family). String key (assigned before the insert, so a protected department's note is sealed before it
	/// is written), IsProtected marker, and a filtered unique index on (CallProtocolId, ClientStepId) that makes an offline replay find its row.
	/// Department-owned with no foreign keys; ProtocolDepartmentCleanup clears it. Existence-guarded.
	/// </summary>
	[Migration(403)]
	public class M0403_AddCallProtocolStepNotes : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("CallProtocolStepNotes").Exists())
			{
				Create.Table("CallProtocolStepNotes")
					.WithColumn("CallProtocolStepNoteId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("CallId").AsInt32().NotNullable()
					.WithColumn("CallProtocolId").AsInt32().NotNullable()
					.WithColumn("StepId").AsString(128).NotNullable()
					.WithColumn("ClientStepId").AsString(64).Nullable()
					.WithColumn("UserId").AsString(128).Nullable()
					.WithColumn("Note").AsString(int.MaxValue).Nullable()
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("CreatedOn").AsDateTime2().NotNullable();
			}

			// The step logs of a call protocol resolve their notes by id within the department.
			if (!Schema.Table("CallProtocolStepNotes").Index("IX_CallProtocolStepNotes_CallProtocol").Exists())
				Create.Index("IX_CallProtocolStepNotes_CallProtocol").OnTable("CallProtocolStepNotes")
					.OnColumn("DepartmentId").Ascending().OnColumn("CallProtocolId").Ascending();

			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_CallProtocolStepNotes_ClientStep' AND object_id = OBJECT_ID('dbo.CallProtocolStepNotes')) " +
				"CREATE UNIQUE INDEX [UX_CallProtocolStepNotes_ClientStep] ON [dbo].[CallProtocolStepNotes] ([CallProtocolId], [ClientStepId]) WHERE [ClientStepId] IS NOT NULL;");
		}

		public override void Down()
		{
			if (Schema.Table("CallProtocolStepNotes").Exists())
				Delete.Table("CallProtocolStepNotes");
		}
	}
}
