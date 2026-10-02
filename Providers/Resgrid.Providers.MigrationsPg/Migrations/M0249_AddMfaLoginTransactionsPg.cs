using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Passkey plan Phase 1, slice 8: restricted login MFA transactions (plan section 5.2), and the second factor on each session. Only SHA-256 hashes of the
	/// transaction secret and the one-use completion code are stored; no password, code, assertion or token.
	/// </summary>
	[Migration(249)]
	public class M0249_AddMfaLoginTransactionsPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("mfalogintransactions").Exists())
				Create.Table("mfalogintransactions")
					.WithColumn("mfalogintransactionid").AsString(36).PrimaryKey().NotNullable()
					.WithColumn("secrethash").AsBinary(32).NotNullable()
					.WithColumn("userid").AsString(128).NotNullable()
					.WithColumn("departmentid").AsInt32().Nullable()
					.WithColumn("clientapplication").AsInt32().NotNullable()
					.WithColumn("clientid").AsString(128).Nullable()
					.WithColumn("firstfactormethod").AsInt32().NotNullable()
					.WithColumn("firstfactorverifiedonutc").AsDateTime().NotNullable()
					.WithColumn("departmentssoconfigid").AsString(128).Nullable()
					.WithColumn("authenticationgeneration").AsInt64().NotNullable()
					.WithColumn("mfapolicyversion").AsInt64().NotNullable().WithDefaultValue(0)
					.WithColumn("scopes").AsString(512).Nullable()
					.WithColumn("createdonutc").AsDateTime().NotNullable()
					.WithColumn("expiresonutc").AsDateTime().NotNullable()
					.WithColumn("attempts").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("maxattempts").AsInt32().NotNullable()
					.WithColumn("state").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("completionmethod").AsInt32().Nullable()
					.WithColumn("completionfactorreference").AsString(256).Nullable()
					.WithColumn("completionverifiedonutc").AsDateTime().Nullable()
					.WithColumn("isrecovery").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("completioncodehash").AsBinary(32).Nullable()
					.WithColumn("completionexpiresonutc").AsDateTime().Nullable()
					.WithColumn("redeemedonutc").AsDateTime().Nullable();

			if (!Schema.Table("mfalogintransactions").Index("ux_mfalogintransactions_secrethash").Exists())
				Create.Index("ux_mfalogintransactions_secrethash").OnTable("mfalogintransactions")
					.OnColumn("secrethash").Ascending()
					.WithOptions().Unique();

			if (!Schema.Table("mfalogintransactions").Index("ix_mfalogintransactions_user").Exists())
				Create.Index("ix_mfalogintransactions_user").OnTable("mfalogintransactions")
					.OnColumn("userid").Ascending();

			if (!Schema.Table("mfalogintransactions").Index("ix_mfalogintransactions_expires").Exists())
				Create.Index("ix_mfalogintransactions_expires").OnTable("mfalogintransactions")
					.OnColumn("expiresonutc").Ascending();

			// The second factor a session's sign-in verified, apart from its first-factor AuthenticationMethod (plan
			// section 5.3), so removing a passkey can end the sessions that signed in with it.
			if (!Schema.Table("usersessions").Column("loginmfamethod").Exists())
				Alter.Table("usersessions").AddColumn("loginmfamethod").AsInt32().Nullable();

			if (!Schema.Table("usersessions").Column("loginmfafactorreference").Exists())
				Alter.Table("usersessions").AddColumn("loginmfafactorreference").AsString(256).Nullable();
		}

		public override void Down() => throw new System.NotSupportedException("Login transactions are security state; disable the feature instead of dropping it.");
	}
}
