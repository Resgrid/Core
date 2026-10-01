using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Passkey plan Phase 1, slice 17 (section 6.5): the account's recent MFA activity (30-day retention), a Responder
	/// installation that stopped taking approval requests, and where the current authenticator was set up. Tables and
	/// columns are checked before each change, so the isolated database tests can apply this over just the tables they make.
	/// </summary>
	[Migration(255)]
	public class M0255_AddMfaActivityAndApprovalInstallations : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("UserMfaActivity").Exists())
				Create.Table("UserMfaActivity")
					.WithColumn("MfaActivityId").AsString(36).PrimaryKey().NotNullable()
					.WithColumn("UserId").AsString(128).NotNullable()
					.WithColumn("OccurredOnUtc").AsDateTime().NotNullable()
					.WithColumn("Method").AsInt32().NotNullable()
					.WithColumn("Purpose").AsInt32().NotNullable()
					.WithColumn("Successful").AsBoolean().NotNullable()
					.WithColumn("ClientApplication").AsInt32().NotNullable()
					.WithColumn("InstallationLabel").AsString(256).Nullable()
					.WithColumn("SharedMode").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("DepartmentId").AsInt32().Nullable()
					.WithColumn("SessionId").AsString(256).Nullable()
					.WithColumn("ApproverSessionId").AsString(256).Nullable()
					.WithColumn("ReportedOnUtc").AsDateTime().Nullable();

			if (!Schema.Table("UserMfaActivity").Index("IX_UserMfaActivity_UserOccurred").Exists())
				Create.Index("IX_UserMfaActivity_UserOccurred").OnTable("UserMfaActivity")
					.OnColumn("UserId").Ascending()
					.OnColumn("OccurredOnUtc").Descending();

			if (!Schema.Table("UserMfaActivity").Index("IX_UserMfaActivity_Occurred").Exists())
				Create.Index("IX_UserMfaActivity_Occurred").OnTable("UserMfaActivity").OnColumn("OccurredOnUtc").Ascending();

			if (Schema.Table("UserSessions").Exists() && !Schema.Table("UserSessions").Column("ApprovalsDisabledOnUtc").Exists())
				Alter.Table("UserSessions").AddColumn("ApprovalsDisabledOnUtc").AsDateTime2().Nullable();

			if (Schema.Table("UserTotpStates").Exists() && !Schema.Table("UserTotpStates").Column("EnrolledClientApplication").Exists())
				Alter.Table("UserTotpStates").AddColumn("EnrolledClientApplication").AsInt32().Nullable();
			if (Schema.Table("UserTotpStates").Exists() && !Schema.Table("UserTotpStates").Column("EnrolledInstallation").Exists())
				Alter.Table("UserTotpStates").AddColumn("EnrolledInstallation").AsString(256).Nullable();
		}

		public override void Down()
		{
			if (Schema.Table("UserTotpStates").Exists() && Schema.Table("UserTotpStates").Column("EnrolledInstallation").Exists())
				Delete.Column("EnrolledInstallation").FromTable("UserTotpStates");
			if (Schema.Table("UserTotpStates").Exists() && Schema.Table("UserTotpStates").Column("EnrolledClientApplication").Exists())
				Delete.Column("EnrolledClientApplication").FromTable("UserTotpStates");
			if (Schema.Table("UserSessions").Exists() && Schema.Table("UserSessions").Column("ApprovalsDisabledOnUtc").Exists())
				Delete.Column("ApprovalsDisabledOnUtc").FromTable("UserSessions");
			if (Schema.Table("UserMfaActivity").Exists())
				Delete.Table("UserMfaActivity");
		}
	}
}
