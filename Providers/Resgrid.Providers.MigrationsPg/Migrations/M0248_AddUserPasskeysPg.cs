using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Passkey plan Phase 1, slice 7: registered passkeys (plan section 5.1). Public keys and protocol metadata only; no
	/// private key, biometric data, assertion or token. A credential id is unique within its relying party, so concurrent
	/// registration can never attach one credential to two users.
	/// </summary>
	[Migration(248)]
	public class M0248_AddUserPasskeysPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("userpasskeys").Exists())
				Create.Table("userpasskeys")
					.WithColumn("userpasskeyid").AsString(36).PrimaryKey().NotNullable()
					.WithColumn("userid").AsString(128).NotNullable()
					.WithColumn("clientapplication").AsInt32().NotNullable()
					.WithColumn("rpid").AsString(253).NotNullable()
					.WithColumn("credentialid").AsBinary(1023).NotNullable()
					.WithColumn("credentialidhash").AsBinary(32).NotNullable()
					.WithColumn("publickey").AsBinary(int.MaxValue).NotNullable()
					.WithColumn("algorithm").AsInt32().Nullable()
					.WithColumn("userhandle").AsBinary(64).NotNullable()
					.WithColumn("signcount").AsInt64().NotNullable().WithDefaultValue(0)
					.WithColumn("isbackupeligible").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("isbackedup").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("transports").AsString(128).Nullable()
					.WithColumn("aaguid").AsString(36).Nullable()
					.WithColumn("attestationformat").AsString(32).Nullable()
					.WithColumn("displayname").AsString(100).NotNullable()
					.WithColumn("createdonutc").AsDateTime().NotNullable()
					.WithColumn("registrationplatform").AsString(128).Nullable()
					.WithColumn("registrationinstallation").AsString(256).Nullable()
					.WithColumn("registrationuseragentfamily").AsString(128).Nullable()
					.WithColumn("registrationattachment").AsString(16).Nullable()
					.WithColumn("registeredinsharedmode").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("lastusedonutc").AsDateTime().Nullable()
					.WithColumn("lastusedclientapplication").AsInt32().Nullable()
					.WithColumn("lastusedinstallation").AsString(256).Nullable()
					.WithColumn("lastusedinsharedmode").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("approvalenabled").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("revokedonutc").AsDateTime().Nullable()
					.WithColumn("revocationreason").AsInt32().Nullable()
					.WithColumn("revokedbyuserid").AsString(128).Nullable()
					.WithColumn("stateversion").AsInt64().NotNullable().WithDefaultValue(1);

			if (!Schema.Table("userpasskeys").Index("ux_userpasskeys_rpcredential").Exists())
				Create.Index("ux_userpasskeys_rpcredential").OnTable("userpasskeys")
					.OnColumn("rpid").Ascending()
					.OnColumn("credentialidhash").Ascending()
					.WithOptions().Unique();

			if (!Schema.Table("userpasskeys").Index("ix_userpasskeys_userclient").Exists())
				Create.Index("ix_userpasskeys_userclient").OnTable("userpasskeys")
					.OnColumn("userid").Ascending()
					.OnColumn("clientapplication").Ascending();
		}

		public override void Down() => throw new System.NotSupportedException("Registered passkeys are security state; disable the feature instead of dropping it.");
	}
}
