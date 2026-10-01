using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Passkey plan Phase 1, slice 9: server-brokered SSO transactions (plan section 7.7.2) and the SAML IdP SSO URL. Only
	/// hashes of the IdP state, nonce and one-time code are stored, with the IdP PKCE verifier encrypted; no IdP token,
	/// assertion or password.
	/// </summary>
	[Migration(250)]
	public class M0250_AddBrokeredSsoPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("ssologintransactions").Exists())
				Create.Table("ssologintransactions")
					.WithColumn("ssologintransactionid").AsString(36).PrimaryKey().NotNullable()
					.WithColumn("statehash").AsBinary(32).NotNullable()
					.WithColumn("purpose").AsInt32().NotNullable()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("departmentssoconfigid").AsString(128).NotNullable()
					.WithColumn("providertype").AsInt32().NotNullable()
					.WithColumn("clientapplication").AsInt32().NotNullable()
					.WithColumn("platform").AsString(32).Nullable()
					.WithColumn("returntarget").AsString(1024).NotNullable()
					.WithColumn("clientstate").AsString(512).Nullable()
					.WithColumn("codechallenge").AsString(128).NotNullable()
					.WithColumn("noncehash").AsBinary(32).Nullable()
					.WithColumn("encryptedidpcodeverifier").AsString(1024).Nullable()
					.WithColumn("samlrequestid").AsString(128).Nullable()
					.WithColumn("sessionid").AsString(128).Nullable()
					.WithColumn("expecteduserid").AsString(128).Nullable()
					.WithColumn("authenticationgeneration").AsInt64().Nullable()
					.WithColumn("createdonutc").AsDateTime().NotNullable()
					.WithColumn("expiresonutc").AsDateTime().NotNullable()
					.WithColumn("state").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("userid").AsString(128).Nullable()
					.WithColumn("authenticatedonutc").AsDateTime().Nullable()
					.WithColumn("codehash").AsBinary(32).Nullable()
					.WithColumn("codeexpiresonutc").AsDateTime().Nullable()
					.WithColumn("redeemedonutc").AsDateTime().Nullable()
					.WithColumn("failurecode").AsString(64).Nullable();

			if (!Schema.Table("ssologintransactions").Index("ux_ssologintransactions_statehash").Exists())
				Create.Index("ux_ssologintransactions_statehash").OnTable("ssologintransactions")
					.OnColumn("statehash").Ascending()
					.WithOptions().Unique();

			if (!Schema.Table("ssologintransactions").Index("ix_ssologintransactions_expires").Exists())
				Create.Index("ix_ssologintransactions_expires").OnTable("ssologintransactions")
					.OnColumn("expiresonutc").Ascending();

			// The IdP's SAML single sign-on URL for SP-initiated (brokered) AuthnRequests.
			if (!Schema.Table("departmentssoconfigs").Column("idpssourl").Exists())
				Alter.Table("departmentssoconfigs").AddColumn("idpssourl").AsString(1024).Nullable();
		}

		public override void Down() => throw new System.NotSupportedException("Brokered SSO transactions are security state; disable the feature instead of dropping it.");
	}
}
