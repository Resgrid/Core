using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Passkey plan Phase 1, slice 7: registered passkeys (plan section 5.1). Public keys and protocol metadata only; no
	/// private key, biometric data, assertion or token. A credential id is unique within its relying party, so concurrent
	/// registration can never attach one credential to two users.
	/// </summary>
	[Migration(248)]
	public class M0248_AddUserPasskeys : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("UserPasskeys").Exists())
				Create.Table("UserPasskeys")
					.WithColumn("UserPasskeyId").AsString(36).PrimaryKey().NotNullable()
					.WithColumn("UserId").AsString(128).NotNullable()
					.WithColumn("ClientApplication").AsInt32().NotNullable()
					.WithColumn("RpId").AsString(253).NotNullable()
					.WithColumn("CredentialId").AsBinary(1023).NotNullable()
					.WithColumn("CredentialIdHash").AsBinary(32).NotNullable()
					.WithColumn("PublicKey").AsBinary(int.MaxValue).NotNullable()
					.WithColumn("Algorithm").AsInt32().Nullable()
					.WithColumn("UserHandle").AsBinary(64).NotNullable()
					.WithColumn("SignCount").AsInt64().NotNullable().WithDefaultValue(0)
					.WithColumn("IsBackupEligible").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("IsBackedUp").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("Transports").AsString(128).Nullable()
					.WithColumn("Aaguid").AsString(36).Nullable()
					.WithColumn("AttestationFormat").AsString(32).Nullable()
					.WithColumn("DisplayName").AsString(100).NotNullable()
					.WithColumn("CreatedOnUtc").AsDateTime().NotNullable()
					.WithColumn("RegistrationPlatform").AsString(128).Nullable()
					.WithColumn("RegistrationInstallation").AsString(256).Nullable()
					.WithColumn("RegistrationUserAgentFamily").AsString(128).Nullable()
					.WithColumn("RegistrationAttachment").AsString(16).Nullable()
					.WithColumn("RegisteredInSharedMode").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("LastUsedOnUtc").AsDateTime().Nullable()
					.WithColumn("LastUsedClientApplication").AsInt32().Nullable()
					.WithColumn("LastUsedInstallation").AsString(256).Nullable()
					.WithColumn("LastUsedInSharedMode").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ApprovalEnabled").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RevokedOnUtc").AsDateTime().Nullable()
					.WithColumn("RevocationReason").AsInt32().Nullable()
					.WithColumn("RevokedByUserId").AsString(128).Nullable()
					.WithColumn("StateVersion").AsInt64().NotNullable().WithDefaultValue(1);

			if (!Schema.Table("UserPasskeys").Index("UX_UserPasskeys_RpCredential").Exists())
				Create.Index("UX_UserPasskeys_RpCredential").OnTable("UserPasskeys")
					.OnColumn("RpId").Ascending()
					.OnColumn("CredentialIdHash").Ascending()
					.WithOptions().Unique();

			if (!Schema.Table("UserPasskeys").Index("IX_UserPasskeys_UserClient").Exists())
				Create.Index("IX_UserPasskeys_UserClient").OnTable("UserPasskeys")
					.OnColumn("UserId").Ascending()
					.OnColumn("ClientApplication").Ascending();
		}

		public override void Down() => throw new System.NotSupportedException("Registered passkeys are security state; disable the feature instead of dropping it.");
	}
}
