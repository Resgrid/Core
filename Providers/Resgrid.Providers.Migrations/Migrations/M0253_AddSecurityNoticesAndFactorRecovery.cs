using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Passkey plan Phase 1, slice 12: the user-level security notice outbox (plan section 6.4) and restricted factor
	/// recovery transactions (plan section 5.4). A notice holds only what it says; a recovery transaction stores only a
	/// hash of its secret.
	/// </summary>
	[Migration(253)]
	public class M0253_AddSecurityNoticesAndFactorRecovery : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("SecurityNotices").Exists())
				Create.Table("SecurityNotices")
					.WithColumn("SecurityNoticeId").AsString(36).PrimaryKey().NotNullable()
					.WithColumn("UserId").AsString(128).NotNullable()
					.WithColumn("Kind").AsInt32().NotNullable()
					.WithColumn("OccurredOnUtc").AsDateTime().NotNullable()
					.WithColumn("ClientApplication").AsInt32().Nullable()
					.WithColumn("InstallationLabel").AsString(256).Nullable()
					.WithColumn("Region").AsString(256).Nullable()
					.WithColumn("State").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Attempts").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("NextAttemptOnUtc").AsDateTime().NotNullable()
					.WithColumn("LeaseOwner").AsString(128).Nullable()
					.WithColumn("LeaseUntilUtc").AsDateTime().Nullable()
					.WithColumn("SentOnUtc").AsDateTime().Nullable()
					.WithColumn("LastFailure").AsString(64).Nullable()
					.WithColumn("CreatedOnUtc").AsDateTime().NotNullable();

			if (!Schema.Table("SecurityNotices").Index("IX_SecurityNotices_Due").Exists())
				Create.Index("IX_SecurityNotices_Due").OnTable("SecurityNotices")
					.OnColumn("State").Ascending()
					.OnColumn("NextAttemptOnUtc").Ascending();

			if (!Schema.Table("SecurityNotices").Index("IX_SecurityNotices_UserKind").Exists())
				Create.Index("IX_SecurityNotices_UserKind").OnTable("SecurityNotices")
					.OnColumn("UserId").Ascending()
					.OnColumn("Kind").Ascending()
					.OnColumn("CreatedOnUtc").Descending();

			if (!Schema.Table("FactorRecoveryTransactions").Exists())
				Create.Table("FactorRecoveryTransactions")
					.WithColumn("FactorRecoveryTransactionId").AsString(36).PrimaryKey().NotNullable()
					.WithColumn("SecretHash").AsBinary(32).NotNullable()
					.WithColumn("UserId").AsString(128).NotNullable()
					.WithColumn("ClientApplication").AsInt32().NotNullable()
					.WithColumn("FirstFactorMethod").AsInt32().NotNullable()
					.WithColumn("FirstFactorVerifiedOnUtc").AsDateTime().NotNullable()
					.WithColumn("DepartmentSsoConfigId").AsString(128).Nullable()
					.WithColumn("DepartmentId").AsInt32().Nullable()
					.WithColumn("AuthenticationGeneration").AsInt64().NotNullable()
					.WithColumn("CreatedOnUtc").AsDateTime().NotNullable()
					.WithColumn("ExpiresOnUtc").AsDateTime().NotNullable()
					.WithColumn("Attempts").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("MaxAttempts").AsInt32().NotNullable()
					.WithColumn("State").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("CompletedOnUtc").AsDateTime().Nullable();

			if (!Schema.Table("FactorRecoveryTransactions").Index("UX_FactorRecoveryTransactions_SecretHash").Exists())
				Create.Index("UX_FactorRecoveryTransactions_SecretHash").OnTable("FactorRecoveryTransactions")
					.OnColumn("SecretHash").Ascending()
					.WithOptions().Unique();

			if (!Schema.Table("FactorRecoveryTransactions").Index("IX_FactorRecoveryTransactions_User").Exists())
				Create.Index("IX_FactorRecoveryTransactions_User").OnTable("FactorRecoveryTransactions")
					.OnColumn("UserId").Ascending();
		}

		public override void Down() => throw new System.NotSupportedException("Security notices and recovery state are security records; do not drop them.");
	}
}
