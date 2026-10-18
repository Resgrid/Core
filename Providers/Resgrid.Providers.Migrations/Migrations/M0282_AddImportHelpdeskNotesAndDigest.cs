using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Department Import support (wizard plan sections 17.2 and 17.5): staff replies mirrored to the request's Freshdesk ticket as
	/// private notes through the worker 48 outbox (ImportMessages.HelpdeskState, HelpdeskAttempts, HelpdeskNextAttemptOn), and
	/// the daily digest of staff value views to every administrator (ImportWorkspaces.StaffViewDigestSentOn, plus an index to
	/// find the views across departments).
	/// </summary>
	[Migration(282)]
	public class M0282_AddImportHelpdeskNotesAndDigest : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("ImportMessages").Column("HelpdeskState").Exists())
				Alter.Table("ImportMessages")
					.AddColumn("HelpdeskState").AsInt32().NotNullable().WithDefaultValue(0)
					.AddColumn("HelpdeskAttempts").AsInt32().NotNullable().WithDefaultValue(0)
					.AddColumn("HelpdeskNextAttemptOn").AsDateTime2().Nullable();

			if (!Schema.Table("ImportMessages").Index("IX_ImportMessages_HelpdeskState_HelpdeskNextAttemptOn").Exists())
				Create.Index("IX_ImportMessages_HelpdeskState_HelpdeskNextAttemptOn").OnTable("ImportMessages")
					.OnColumn("HelpdeskState").Ascending()
					.OnColumn("HelpdeskNextAttemptOn").Ascending();

			if (!Schema.Table("ImportWorkspaces").Column("StaffViewDigestSentOn").Exists())
				Alter.Table("ImportWorkspaces").AddColumn("StaffViewDigestSentOn").AsDateTime2().Nullable();

			if (!Schema.Table("ImportEvents").Index("IX_ImportEvents_Action_CreatedOn").Exists())
				Create.Index("IX_ImportEvents_Action_CreatedOn").OnTable("ImportEvents")
					.OnColumn("Action").Ascending()
					.OnColumn("CreatedOn").Ascending();
		}

		public override void Down()
		{
			if (Schema.Table("ImportEvents").Index("IX_ImportEvents_Action_CreatedOn").Exists())
				Delete.Index("IX_ImportEvents_Action_CreatedOn").OnTable("ImportEvents");
			if (Schema.Table("ImportWorkspaces").Column("StaffViewDigestSentOn").Exists())
				Delete.Column("StaffViewDigestSentOn").FromTable("ImportWorkspaces");
			if (Schema.Table("ImportMessages").Index("IX_ImportMessages_HelpdeskState_HelpdeskNextAttemptOn").Exists())
				Delete.Index("IX_ImportMessages_HelpdeskState_HelpdeskNextAttemptOn").OnTable("ImportMessages");
			foreach (var column in new[] { "HelpdeskNextAttemptOn", "HelpdeskAttempts", "HelpdeskState" })
				if (Schema.Table("ImportMessages").Column(column).Exists())
					Delete.Column(column).FromTable("ImportMessages");
		}
	}
}
