using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Passkey plan Phase 1, slice 12: the user-level security notice outbox (plan section 6.4) and restricted factor
	/// recovery transactions (plan section 5.4). A notice holds only what it says; a recovery transaction stores only a
	/// hash of its secret.
	/// </summary>
	[Migration(253)]
	public class M0253_AddSecurityNoticesAndFactorRecoveryPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("securitynotices").Exists())
				Create.Table("securitynotices")
					.WithColumn("securitynoticeid").AsString(36).PrimaryKey().NotNullable()
					.WithColumn("userid").AsString(128).NotNullable()
					.WithColumn("kind").AsInt32().NotNullable()
					.WithColumn("occurredonutc").AsDateTime().NotNullable()
					.WithColumn("clientapplication").AsInt32().Nullable()
					.WithColumn("installationlabel").AsString(256).Nullable()
					.WithColumn("region").AsString(256).Nullable()
					.WithColumn("state").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("attempts").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("nextattemptonutc").AsDateTime().NotNullable()
					.WithColumn("leaseowner").AsString(128).Nullable()
					.WithColumn("leaseuntilutc").AsDateTime().Nullable()
					.WithColumn("sentonutc").AsDateTime().Nullable()
					.WithColumn("lastfailure").AsString(64).Nullable()
					.WithColumn("createdonutc").AsDateTime().NotNullable();

			if (!Schema.Table("securitynotices").Index("ix_securitynotices_due").Exists())
				Create.Index("ix_securitynotices_due").OnTable("securitynotices")
					.OnColumn("state").Ascending()
					.OnColumn("nextattemptonutc").Ascending();

			if (!Schema.Table("securitynotices").Index("ix_securitynotices_userkind").Exists())
				Create.Index("ix_securitynotices_userkind").OnTable("securitynotices")
					.OnColumn("userid").Ascending()
					.OnColumn("kind").Ascending()
					.OnColumn("createdonutc").Descending();

			if (!Schema.Table("factorrecoverytransactions").Exists())
				Create.Table("factorrecoverytransactions")
					.WithColumn("factorrecoverytransactionid").AsString(36).PrimaryKey().NotNullable()
					.WithColumn("secrethash").AsBinary(32).NotNullable()
					.WithColumn("userid").AsString(128).NotNullable()
					.WithColumn("clientapplication").AsInt32().NotNullable()
					.WithColumn("firstfactormethod").AsInt32().NotNullable()
					.WithColumn("firstfactorverifiedonutc").AsDateTime().NotNullable()
					.WithColumn("departmentssoconfigid").AsString(128).Nullable()
					.WithColumn("departmentid").AsInt32().Nullable()
					.WithColumn("authenticationgeneration").AsInt64().NotNullable()
					.WithColumn("createdonutc").AsDateTime().NotNullable()
					.WithColumn("expiresonutc").AsDateTime().NotNullable()
					.WithColumn("attempts").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("maxattempts").AsInt32().NotNullable()
					.WithColumn("state").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("completedonutc").AsDateTime().Nullable();

			if (!Schema.Table("factorrecoverytransactions").Index("ux_factorrecoverytransactions_secrethash").Exists())
				Create.Index("ux_factorrecoverytransactions_secrethash").OnTable("factorrecoverytransactions")
					.OnColumn("secrethash").Ascending()
					.WithOptions().Unique();

			if (!Schema.Table("factorrecoverytransactions").Index("ix_factorrecoverytransactions_user").Exists())
				Create.Index("ix_factorrecoverytransactions_user").OnTable("factorrecoverytransactions")
					.OnColumn("userid").Ascending();
		}

		public override void Down() => throw new System.NotSupportedException("Security notices and recovery state are security records; do not drop them.");
	}
}
