using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Passkey plan Phase 1 (TOTP and recovery hardening): one-time TOTP time steps and hashed, single-use recovery
	/// codes. Neither table holds a TOTP seed or a plaintext recovery code. Code hashes are bytea (case-sensitive).
	/// </summary>
	[Migration(243)]
	public class M0243_AddMfaFactorStatePg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("usertotpstates").Exists())
				Create.Table("usertotpstates")
					.WithColumn("userid").AsString(128).PrimaryKey().NotNullable()
					.WithColumn("lastacceptedtimestep").AsInt64().NotNullable()
					.WithColumn("lastacceptedonutc").AsDateTime().NotNullable()
					.WithColumn("enrolledonutc").AsDateTime().Nullable();

			if (!Schema.Table("userrecoverycodes").Exists())
				Create.Table("userrecoverycodes")
					.WithColumn("userrecoverycodeid").AsString(36).PrimaryKey().NotNullable()
					.WithColumn("userid").AsString(128).NotNullable()
					.WithColumn("codehash").AsBinary(32).NotNullable()
					.WithColumn("hashversion").AsInt32().NotNullable()
					.WithColumn("createdonutc").AsDateTime().NotNullable()
					.WithColumn("usedonutc").AsDateTime().Nullable();

			if (!Schema.Table("userrecoverycodes").Index("ux_userrecoverycodes_userhash").Exists())
				Create.Index("ux_userrecoverycodes_userhash").OnTable("userrecoverycodes")
					.OnColumn("userid").Ascending()
					.OnColumn("codehash").Ascending()
					.WithOptions().Unique();
		}

		public override void Down() => throw new System.NotSupportedException("MFA factor state is security state; disable the feature instead of dropping it.");
	}
}
