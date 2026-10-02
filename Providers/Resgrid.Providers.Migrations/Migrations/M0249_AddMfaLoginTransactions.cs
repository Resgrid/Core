using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Passkey plan Phase 1, slice 8: restricted login MFA transactions (plan section 5.2), and the second factor on each session. Only SHA-256 hashes of the
	/// transaction secret and the one-use completion code are stored; no password, code, assertion or token.
	/// </summary>
	[Migration(249)]
	public class M0249_AddMfaLoginTransactions : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("MfaLoginTransactions").Exists())
				Create.Table("MfaLoginTransactions")
					.WithColumn("MfaLoginTransactionId").AsString(36).PrimaryKey().NotNullable()
					.WithColumn("SecretHash").AsBinary(32).NotNullable()
					.WithColumn("UserId").AsString(128).NotNullable()
					.WithColumn("DepartmentId").AsInt32().Nullable()
					.WithColumn("ClientApplication").AsInt32().NotNullable()
					.WithColumn("ClientId").AsString(128).Nullable()
					.WithColumn("FirstFactorMethod").AsInt32().NotNullable()
					.WithColumn("FirstFactorVerifiedOnUtc").AsDateTime().NotNullable()
					.WithColumn("DepartmentSsoConfigId").AsString(128).Nullable()
					.WithColumn("AuthenticationGeneration").AsInt64().NotNullable()
					.WithColumn("MfaPolicyVersion").AsInt64().NotNullable().WithDefaultValue(0)
					.WithColumn("Scopes").AsString(512).Nullable()
					.WithColumn("CreatedOnUtc").AsDateTime().NotNullable()
					.WithColumn("ExpiresOnUtc").AsDateTime().NotNullable()
					.WithColumn("Attempts").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("MaxAttempts").AsInt32().NotNullable()
					.WithColumn("State").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("CompletionMethod").AsInt32().Nullable()
					.WithColumn("CompletionFactorReference").AsString(256).Nullable()
					.WithColumn("CompletionVerifiedOnUtc").AsDateTime().Nullable()
					.WithColumn("IsRecovery").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("CompletionCodeHash").AsBinary(32).Nullable()
					.WithColumn("CompletionExpiresOnUtc").AsDateTime().Nullable()
					.WithColumn("RedeemedOnUtc").AsDateTime().Nullable();

			if (!Schema.Table("MfaLoginTransactions").Index("UX_MfaLoginTransactions_SecretHash").Exists())
				Create.Index("UX_MfaLoginTransactions_SecretHash").OnTable("MfaLoginTransactions")
					.OnColumn("SecretHash").Ascending()
					.WithOptions().Unique();

			if (!Schema.Table("MfaLoginTransactions").Index("IX_MfaLoginTransactions_User").Exists())
				Create.Index("IX_MfaLoginTransactions_User").OnTable("MfaLoginTransactions")
					.OnColumn("UserId").Ascending();

			if (!Schema.Table("MfaLoginTransactions").Index("IX_MfaLoginTransactions_Expires").Exists())
				Create.Index("IX_MfaLoginTransactions_Expires").OnTable("MfaLoginTransactions")
					.OnColumn("ExpiresOnUtc").Ascending();

			// The second factor a session's sign-in verified, apart from its first-factor AuthenticationMethod (plan
			// section 5.3), so removing a passkey can end the sessions that signed in with it.
			if (!Schema.Table("UserSessions").Column("LoginMfaMethod").Exists())
				Alter.Table("UserSessions").AddColumn("LoginMfaMethod").AsInt32().Nullable();

			if (!Schema.Table("UserSessions").Column("LoginMfaFactorReference").Exists())
				Alter.Table("UserSessions").AddColumn("LoginMfaFactorReference").AsString(256).Nullable();
		}

		public override void Down() => throw new System.NotSupportedException("Login transactions are security state; disable the feature instead of dropping it.");
	}
}
