using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Passkey plan Phase 1, slice 2: single-use WebAuthn challenges and server-side MFA evidence per session. Neither
	/// table holds a password, TOTP code, recovery code, assertion or token.
	/// </summary>
	[Migration(244)]
	public class M0244_AddMfaEvidenceAndChallengesPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("authenticationchallenges").Exists())
				Create.Table("authenticationchallenges")
					.WithColumn("authenticationchallengeid").AsString(36).PrimaryKey().NotNullable()
					.WithColumn("userid").AsString(128).NotNullable()
					.WithColumn("purpose").AsInt32().NotNullable()
					.WithColumn("clientapplication").AsInt32().NotNullable()
					.WithColumn("rpid").AsString(253).NotNullable()
					.WithColumn("parentkind").AsInt32().NotNullable()
					.WithColumn("parentid").AsString(160).NotNullable()
					.WithColumn("departmentid").AsInt32().Nullable()
					.WithColumn("authenticationgeneration").AsInt64().NotNullable()
					.WithColumn("lockversion").AsInt64().Nullable()
					.WithColumn("optionsjson").AsString(int.MaxValue).NotNullable()
					.WithColumn("createdonutc").AsDateTime().NotNullable()
					.WithColumn("expiresonutc").AsDateTime().NotNullable()
					.WithColumn("attempts").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("maxattempts").AsInt32().NotNullable()
					.WithColumn("state").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("consumedonutc").AsDateTime().Nullable();

			if (!Schema.Table("authenticationchallenges").Index("ix_authenticationchallenges_userstate").Exists())
				Create.Index("ix_authenticationchallenges_userstate").OnTable("authenticationchallenges")
					.OnColumn("userid").Ascending()
					.OnColumn("state").Ascending();

			if (!Schema.Table("authenticationchallenges").Index("ix_authenticationchallenges_expires").Exists())
				Create.Index("ix_authenticationchallenges_expires").OnTable("authenticationchallenges")
					.OnColumn("expiresonutc").Ascending();

			if (!Schema.Table("usersessionmfaevidence").Exists())
				Create.Table("usersessionmfaevidence")
					.WithColumn("mfaevidenceid").AsString(36).PrimaryKey().NotNullable()
					.WithColumn("userid").AsString(128).NotNullable()
					.WithColumn("sessionkey").AsString(160).NotNullable()
					.WithColumn("clientapplication").AsInt32().NotNullable()
					.WithColumn("kind").AsInt32().NotNullable()
					.WithColumn("method").AsInt32().NotNullable()
					.WithColumn("purpose").AsInt32().NotNullable()
					.WithColumn("departmentid").AsInt32().Nullable()
					.WithColumn("verifiedonutc").AsDateTime().NotNullable()
					.WithColumn("expiresonutc").AsDateTime().NotNullable()
					.WithColumn("authenticationgeneration").AsInt64().NotNullable()
					.WithColumn("factorreference").AsString(256).Nullable()
					.WithColumn("revokedonutc").AsDateTime().Nullable();

			if (!Schema.Table("usersessionmfaevidence").Index("ix_usersessionmfaevidence_session").Exists())
				Create.Index("ix_usersessionmfaevidence_session").OnTable("usersessionmfaevidence")
					.OnColumn("sessionkey").Ascending()
					.OnColumn("kind").Ascending()
					.OnColumn("verifiedonutc").Descending();

			if (!Schema.Table("usersessionmfaevidence").Index("ix_usersessionmfaevidence_user").Exists())
				Create.Index("ix_usersessionmfaevidence_user").OnTable("usersessionmfaevidence")
					.OnColumn("userid").Ascending();

			if (!Schema.Table("usersessionmfaevidence").Index("ix_usersessionmfaevidence_expires").Exists())
				Create.Index("ix_usersessionmfaevidence_expires").OnTable("usersessionmfaevidence")
					.OnColumn("expiresonutc").Ascending();
		}

		public override void Down() => throw new System.NotSupportedException("MFA evidence and challenges are security state; disable the feature instead of dropping it.");
	}
}
