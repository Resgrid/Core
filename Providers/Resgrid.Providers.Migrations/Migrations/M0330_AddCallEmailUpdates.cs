using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// CAD email call updates (cad-email-call-updates-plan.md): a dispatch email carrying the CAD call ID of a call Resgrid already
	/// has updates, closes or is noted on that call instead of creating a new one.
	/// - CallEmailUpdates: one row per such email (the call it created or touched, its sent time, the field groups it changed and
	///   a SHA-256 per group of the value the CAD set). Hashes only, no email content, so no protected data. No foreign keys:
	///   UnlinkedDepartmentCleanup removes the rows.
	/// - IntakeChannels.CadUpdates: an intake email channel opts in to the same matching (plan D8).
	/// Existence-guarded for safe retry.
	/// </summary>
	[Migration(330)]
	public class M0330_AddCallEmailUpdates : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("CallEmailUpdates").Exists())
			{
				Create.Table("CallEmailUpdates")
					.WithColumn("CallEmailUpdateId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("CallId").AsInt32().Nullable()
					.WithColumn("CadCallId").AsString(100).NotNullable()
					.WithColumn("Source").AsInt32().NotNullable()
					.WithColumn("IntakeChannelId").AsString(128).Nullable()
					.WithColumn("MessageId").AsString(500).Nullable()
					.WithColumn("SentOn").AsDateTime2().Nullable()
					.WithColumn("ReceivedOn").AsDateTime2().NotNullable()
					.WithColumn("Outcome").AsInt32().NotNullable()
					.WithColumn("ChangedFields").AsString(500).Nullable()
					.WithColumn("FieldHashes").AsString(2000).Nullable();

				Create.Index("IX_CallEmailUpdates_Department_Call").OnTable("CallEmailUpdates")
					.OnColumn("DepartmentId").Ascending()
					.OnColumn("CallId").Ascending();

				Create.Index("IX_CallEmailUpdates_Department_CadCallId").OnTable("CallEmailUpdates")
					.OnColumn("DepartmentId").Ascending()
					.OnColumn("CadCallId").Ascending();
			}

			if (Schema.Table("IntakeChannels").Exists() && !Schema.Table("IntakeChannels").Column("CadUpdates").Exists())
			{
				Alter.Table("IntakeChannels")
					.AddColumn("CadUpdates").AsBoolean().NotNullable().WithDefaultValue(false);
			}
		}

		public override void Down()
		{
			if (Schema.Table("IntakeChannels").Exists() && Schema.Table("IntakeChannels").Column("CadUpdates").Exists())
			{
				// SQL Server keeps the column's default as its own constraint; it must go before the column.
				Execute.Sql(@"DECLARE @constraint sysname;
SELECT @constraint = dc.name FROM sys.default_constraints dc
JOIN sys.columns c ON c.default_object_id = dc.object_id
WHERE dc.parent_object_id = OBJECT_ID('dbo.IntakeChannels') AND c.name = 'CadUpdates';
IF @constraint IS NOT NULL EXEC('ALTER TABLE dbo.IntakeChannels DROP CONSTRAINT [' + @constraint + ']');");
				Delete.Column("CadUpdates").FromTable("IntakeChannels");
			}

			if (Schema.Table("CallEmailUpdates").Exists())
				Delete.Table("CallEmailUpdates");
		}
	}
}
