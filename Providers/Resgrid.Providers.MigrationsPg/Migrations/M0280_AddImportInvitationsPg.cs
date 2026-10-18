using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Department Import invitations (see the SQL Server M0278): invites.kind, targetuserid, workspaceid and the batched
	/// sender's sendstate, sendafter, sendattempts and locale.
	/// </summary>
	[Migration(280)]
	public class M0280_AddImportInvitationsPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("invites").Column("kind").Exists())
				Alter.Table("invites")
					.AddColumn("kind").AsInt32().NotNullable().WithDefaultValue(0)
					.AddColumn("targetuserid").AsCustom("citext").Nullable()
					.AddColumn("workspaceid").AsCustom("citext").Nullable()
					.AddColumn("sendstate").AsInt32().NotNullable().WithDefaultValue(0)
					.AddColumn("sendafter").AsDateTime2().Nullable()
					.AddColumn("sendattempts").AsInt32().NotNullable().WithDefaultValue(0)
					.AddColumn("locale").AsCustom("citext").Nullable();

			if (!Schema.Table("invites").Index("ix_invites_sendstate_sendafter").Exists())
				Create.Index("ix_invites_sendstate_sendafter").OnTable("invites")
					.OnColumn("sendstate").Ascending()
					.OnColumn("sendafter").Ascending();

			if (!Schema.Table("invites").Index("ix_invites_departmentid_workspaceid").Exists())
				Create.Index("ix_invites_departmentid_workspaceid").OnTable("invites")
					.OnColumn("departmentid").Ascending()
					.OnColumn("workspaceid").Ascending();
		}

		public override void Down()
		{
			if (Schema.Table("invites").Index("ix_invites_departmentid_workspaceid").Exists())
				Delete.Index("ix_invites_departmentid_workspaceid").OnTable("invites");
			if (Schema.Table("invites").Index("ix_invites_sendstate_sendafter").Exists())
				Delete.Index("ix_invites_sendstate_sendafter").OnTable("invites");

			foreach (var column in new[] { "locale", "sendattempts", "sendafter", "sendstate", "workspaceid", "targetuserid", "kind" })
				if (Schema.Table("invites").Column(column).Exists())
					Delete.Column(column).FromTable("invites");
		}
	}
}
