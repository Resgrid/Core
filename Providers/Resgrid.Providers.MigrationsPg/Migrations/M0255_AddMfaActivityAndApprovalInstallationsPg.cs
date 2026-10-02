using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Passkey plan Phase 1, slice 17 (section 6.5): the account's recent MFA activity (30-day retention), a Responder
	/// installation that stopped taking approval requests, and where the current authenticator was set up. Tables and
	/// columns are checked before each change, so the isolated database tests can apply this over just the tables they make.
	/// </summary>
	[Migration(255)]
	public class M0255_AddMfaActivityAndApprovalInstallationsPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("usermfaactivity").Exists())
				Create.Table("usermfaactivity")
					.WithColumn("mfaactivityid").AsString(36).PrimaryKey().NotNullable()
					.WithColumn("userid").AsString(128).NotNullable()
					.WithColumn("occurredonutc").AsDateTime().NotNullable()
					.WithColumn("method").AsInt32().NotNullable()
					.WithColumn("purpose").AsInt32().NotNullable()
					.WithColumn("successful").AsBoolean().NotNullable()
					.WithColumn("clientapplication").AsInt32().NotNullable()
					.WithColumn("installationlabel").AsString(256).Nullable()
					.WithColumn("sharedmode").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("departmentid").AsInt32().Nullable()
					.WithColumn("sessionid").AsString(256).Nullable()
					.WithColumn("approversessionid").AsString(256).Nullable()
					.WithColumn("reportedonutc").AsDateTime().Nullable();

			if (!Schema.Table("usermfaactivity").Index("ix_usermfaactivity_useroccurred").Exists())
				Create.Index("ix_usermfaactivity_useroccurred").OnTable("usermfaactivity")
					.OnColumn("userid").Ascending()
					.OnColumn("occurredonutc").Descending();

			if (!Schema.Table("usermfaactivity").Index("ix_usermfaactivity_occurred").Exists())
				Create.Index("ix_usermfaactivity_occurred").OnTable("usermfaactivity").OnColumn("occurredonutc").Ascending();

			if (Schema.Table("usersessions").Exists() && !Schema.Table("usersessions").Column("approvalsdisabledonutc").Exists())
				Alter.Table("usersessions").AddColumn("approvalsdisabledonutc").AsDateTime2().Nullable();

			if (Schema.Table("usertotpstates").Exists() && !Schema.Table("usertotpstates").Column("enrolledclientapplication").Exists())
				Alter.Table("usertotpstates").AddColumn("enrolledclientapplication").AsInt32().Nullable();
			if (Schema.Table("usertotpstates").Exists() && !Schema.Table("usertotpstates").Column("enrolledinstallation").Exists())
				Alter.Table("usertotpstates").AddColumn("enrolledinstallation").AsString(256).Nullable();
		}

		public override void Down()
		{
			if (Schema.Table("usertotpstates").Exists() && Schema.Table("usertotpstates").Column("enrolledinstallation").Exists())
				Delete.Column("enrolledinstallation").FromTable("usertotpstates");
			if (Schema.Table("usertotpstates").Exists() && Schema.Table("usertotpstates").Column("enrolledclientapplication").Exists())
				Delete.Column("enrolledclientapplication").FromTable("usertotpstates");
			if (Schema.Table("usersessions").Exists() && Schema.Table("usersessions").Column("approvalsdisabledonutc").Exists())
				Delete.Column("approvalsdisabledonutc").FromTable("usersessions");
			if (Schema.Table("usermfaactivity").Exists())
				Delete.Table("usermfaactivity");
		}
	}
}
