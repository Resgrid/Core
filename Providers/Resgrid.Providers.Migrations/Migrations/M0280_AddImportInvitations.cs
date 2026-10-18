using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Department Import invitations (wizard plan section 15.4). Invites gains create-without-send: Kind (0 classic, sent at
	/// creation; 1 an imported account's invitation, which sets a password on the dormant account; 2 an invitation to join
	/// for an existing Resgrid account), the dormant account it activates, the workspace that created it, and the batched
	/// sender's state (0 sent, 1 pending, 2 failed, 3 revoked), earliest send time, attempts and the recipient's language.
	/// </summary>
	[Migration(280)]
	public class M0280_AddImportInvitations : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("Invites").Column("Kind").Exists())
				Alter.Table("Invites")
					.AddColumn("Kind").AsInt32().NotNullable().WithDefaultValue(0)
					.AddColumn("TargetUserId").AsString(128).Nullable()
					.AddColumn("WorkspaceId").AsString(36).Nullable()
					.AddColumn("SendState").AsInt32().NotNullable().WithDefaultValue(0)
					.AddColumn("SendAfter").AsDateTime2().Nullable()
					.AddColumn("SendAttempts").AsInt32().NotNullable().WithDefaultValue(0)
					.AddColumn("Locale").AsString(16).Nullable();

			if (!Schema.Table("Invites").Index("IX_Invites_SendState_SendAfter").Exists())
				Create.Index("IX_Invites_SendState_SendAfter").OnTable("Invites")
					.OnColumn("SendState").Ascending()
					.OnColumn("SendAfter").Ascending();

			if (!Schema.Table("Invites").Index("IX_Invites_DepartmentId_WorkspaceId").Exists())
				Create.Index("IX_Invites_DepartmentId_WorkspaceId").OnTable("Invites")
					.OnColumn("DepartmentId").Ascending()
					.OnColumn("WorkspaceId").Ascending();
		}

		public override void Down()
		{
			if (Schema.Table("Invites").Index("IX_Invites_DepartmentId_WorkspaceId").Exists())
				Delete.Index("IX_Invites_DepartmentId_WorkspaceId").OnTable("Invites");
			if (Schema.Table("Invites").Index("IX_Invites_SendState_SendAfter").Exists())
				Delete.Index("IX_Invites_SendState_SendAfter").OnTable("Invites");

			foreach (var column in new[] { "Locale", "SendAttempts", "SendAfter", "SendState", "WorkspaceId", "TargetUserId", "Kind" })
				if (Schema.Table("Invites").Column(column).Exists())
					Delete.Column(column).FromTable("Invites");
		}
	}
}
