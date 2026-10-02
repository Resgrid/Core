using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Passkey plan Phase 1, slice 9: server-brokered SSO transactions (plan section 7.7.2) and the SAML IdP SSO URL. Only
	/// hashes of the IdP state, nonce and one-time code are stored, with the IdP PKCE verifier encrypted; no IdP token,
	/// assertion or password.
	/// </summary>
	[Migration(250)]
	public class M0250_AddBrokeredSso : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("SsoLoginTransactions").Exists())
				Create.Table("SsoLoginTransactions")
					.WithColumn("SsoLoginTransactionId").AsString(36).PrimaryKey().NotNullable()
					.WithColumn("StateHash").AsBinary(32).NotNullable()
					.WithColumn("Purpose").AsInt32().NotNullable()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("DepartmentSsoConfigId").AsString(128).NotNullable()
					.WithColumn("ProviderType").AsInt32().NotNullable()
					.WithColumn("ClientApplication").AsInt32().NotNullable()
					.WithColumn("Platform").AsString(32).Nullable()
					.WithColumn("ReturnTarget").AsString(1024).NotNullable()
					.WithColumn("ClientState").AsString(512).Nullable()
					.WithColumn("CodeChallenge").AsString(128).NotNullable()
					.WithColumn("NonceHash").AsBinary(32).Nullable()
					.WithColumn("EncryptedIdpCodeVerifier").AsString(1024).Nullable()
					.WithColumn("SamlRequestId").AsString(128).Nullable()
					.WithColumn("SessionId").AsString(128).Nullable()
					.WithColumn("ExpectedUserId").AsString(128).Nullable()
					.WithColumn("AuthenticationGeneration").AsInt64().Nullable()
					.WithColumn("CreatedOnUtc").AsDateTime().NotNullable()
					.WithColumn("ExpiresOnUtc").AsDateTime().NotNullable()
					.WithColumn("State").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("UserId").AsString(128).Nullable()
					.WithColumn("AuthenticatedOnUtc").AsDateTime().Nullable()
					.WithColumn("CodeHash").AsBinary(32).Nullable()
					.WithColumn("CodeExpiresOnUtc").AsDateTime().Nullable()
					.WithColumn("RedeemedOnUtc").AsDateTime().Nullable()
					.WithColumn("FailureCode").AsString(64).Nullable();

			if (!Schema.Table("SsoLoginTransactions").Index("UX_SsoLoginTransactions_StateHash").Exists())
				Create.Index("UX_SsoLoginTransactions_StateHash").OnTable("SsoLoginTransactions")
					.OnColumn("StateHash").Ascending()
					.WithOptions().Unique();

			if (!Schema.Table("SsoLoginTransactions").Index("IX_SsoLoginTransactions_Expires").Exists())
				Create.Index("IX_SsoLoginTransactions_Expires").OnTable("SsoLoginTransactions")
					.OnColumn("ExpiresOnUtc").Ascending();

			// The IdP's SAML single sign-on URL for SP-initiated (brokered) AuthnRequests.
			if (!Schema.Table("DepartmentSsoConfigs").Column("IdpSsoUrl").Exists())
				Alter.Table("DepartmentSsoConfigs").AddColumn("IdpSsoUrl").AsString(1024).Nullable();
		}

		public override void Down() => throw new System.NotSupportedException("Brokered SSO transactions are security state; disable the feature instead of dropping it.");
	}
}
