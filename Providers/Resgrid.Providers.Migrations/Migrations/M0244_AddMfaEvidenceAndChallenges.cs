using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Passkey plan Phase 1, slice 2: single-use WebAuthn challenges and server-side MFA evidence per session. Neither
	/// table holds a password, TOTP code, recovery code, assertion or token.
	/// </summary>
	[Migration(244)]
	public class M0244_AddMfaEvidenceAndChallenges : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("AuthenticationChallenges").Exists())
				Create.Table("AuthenticationChallenges")
					.WithColumn("AuthenticationChallengeId").AsString(36).PrimaryKey().NotNullable()
					.WithColumn("UserId").AsString(128).NotNullable()
					.WithColumn("Purpose").AsInt32().NotNullable()
					.WithColumn("ClientApplication").AsInt32().NotNullable()
					.WithColumn("RpId").AsString(253).NotNullable()
					.WithColumn("ParentKind").AsInt32().NotNullable()
					.WithColumn("ParentId").AsString(160).NotNullable()
					.WithColumn("DepartmentId").AsInt32().Nullable()
					.WithColumn("AuthenticationGeneration").AsInt64().NotNullable()
					.WithColumn("LockVersion").AsInt64().Nullable()
					.WithColumn("OptionsJson").AsString(int.MaxValue).NotNullable()
					.WithColumn("CreatedOnUtc").AsDateTime().NotNullable()
					.WithColumn("ExpiresOnUtc").AsDateTime().NotNullable()
					.WithColumn("Attempts").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("MaxAttempts").AsInt32().NotNullable()
					.WithColumn("State").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ConsumedOnUtc").AsDateTime().Nullable();

			if (!Schema.Table("AuthenticationChallenges").Index("IX_AuthenticationChallenges_UserState").Exists())
				Create.Index("IX_AuthenticationChallenges_UserState").OnTable("AuthenticationChallenges")
					.OnColumn("UserId").Ascending()
					.OnColumn("State").Ascending();

			if (!Schema.Table("AuthenticationChallenges").Index("IX_AuthenticationChallenges_Expires").Exists())
				Create.Index("IX_AuthenticationChallenges_Expires").OnTable("AuthenticationChallenges")
					.OnColumn("ExpiresOnUtc").Ascending();

			if (!Schema.Table("UserSessionMfaEvidence").Exists())
				Create.Table("UserSessionMfaEvidence")
					.WithColumn("MfaEvidenceId").AsString(36).PrimaryKey().NotNullable()
					.WithColumn("UserId").AsString(128).NotNullable()
					.WithColumn("SessionKey").AsString(160).NotNullable()
					.WithColumn("ClientApplication").AsInt32().NotNullable()
					.WithColumn("Kind").AsInt32().NotNullable()
					.WithColumn("Method").AsInt32().NotNullable()
					.WithColumn("Purpose").AsInt32().NotNullable()
					.WithColumn("DepartmentId").AsInt32().Nullable()
					.WithColumn("VerifiedOnUtc").AsDateTime().NotNullable()
					.WithColumn("ExpiresOnUtc").AsDateTime().NotNullable()
					.WithColumn("AuthenticationGeneration").AsInt64().NotNullable()
					.WithColumn("FactorReference").AsString(256).Nullable()
					.WithColumn("RevokedOnUtc").AsDateTime().Nullable();

			if (!Schema.Table("UserSessionMfaEvidence").Index("IX_UserSessionMfaEvidence_Session").Exists())
				Create.Index("IX_UserSessionMfaEvidence_Session").OnTable("UserSessionMfaEvidence")
					.OnColumn("SessionKey").Ascending()
					.OnColumn("Kind").Ascending()
					.OnColumn("VerifiedOnUtc").Descending();

			if (!Schema.Table("UserSessionMfaEvidence").Index("IX_UserSessionMfaEvidence_User").Exists())
				Create.Index("IX_UserSessionMfaEvidence_User").OnTable("UserSessionMfaEvidence")
					.OnColumn("UserId").Ascending();

			if (!Schema.Table("UserSessionMfaEvidence").Index("IX_UserSessionMfaEvidence_Expires").Exists())
				Create.Index("IX_UserSessionMfaEvidence_Expires").OnTable("UserSessionMfaEvidence")
					.OnColumn("ExpiresOnUtc").Ascending();
		}

		public override void Down() => throw new System.NotSupportedException("MFA evidence and challenges are security state; disable the feature instead of dropping it.");
	}
}
