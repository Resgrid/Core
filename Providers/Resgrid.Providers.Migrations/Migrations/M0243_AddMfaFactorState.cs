using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Passkey plan Phase 1 (TOTP and recovery hardening): one-time TOTP time steps and hashed, single-use recovery
	/// codes. Neither table holds a TOTP seed or a plaintext recovery code.
	/// </summary>
	[Migration(243)]
	public class M0243_AddMfaFactorState : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("UserTotpStates").Exists())
				Create.Table("UserTotpStates")
					.WithColumn("UserId").AsString(128).PrimaryKey().NotNullable()
					.WithColumn("LastAcceptedTimeStep").AsInt64().NotNullable()
					.WithColumn("LastAcceptedOnUtc").AsDateTime().NotNullable()
					.WithColumn("EnrolledOnUtc").AsDateTime().Nullable();

			if (!Schema.Table("UserRecoveryCodes").Exists())
				Create.Table("UserRecoveryCodes")
					.WithColumn("UserRecoveryCodeId").AsString(36).PrimaryKey().NotNullable()
					.WithColumn("UserId").AsString(128).NotNullable()
					.WithColumn("CodeHash").AsBinary(32).NotNullable()
					.WithColumn("HashVersion").AsInt32().NotNullable()
					.WithColumn("CreatedOnUtc").AsDateTime().NotNullable()
					.WithColumn("UsedOnUtc").AsDateTime().Nullable();

			if (!Schema.Table("UserRecoveryCodes").Index("UX_UserRecoveryCodes_UserHash").Exists())
				Create.Index("UX_UserRecoveryCodes_UserHash").OnTable("UserRecoveryCodes")
					.OnColumn("UserId").Ascending()
					.OnColumn("CodeHash").Ascending()
					.WithOptions().Unique();
		}

		public override void Down() => throw new System.NotSupportedException("MFA factor state is security state; disable the feature instead of dropping it.");
	}
}
