using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Passkey plan Phase 1, slice 11: Responder approval requests (plan section 5.6). The match number is stored only as a
	/// hash bound to its request; no assertion, token or push payload is kept.
	/// </summary>
	[Migration(252)]
	public class M0252_AddMfaApprovalRequestsPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("mfaapprovalrequests").Exists())
				Create.Table("mfaapprovalrequests")
					.WithColumn("mfaapprovalrequestid").AsString(36).PrimaryKey().NotNullable()
					.WithColumn("userid").AsString(128).NotNullable()
					.WithColumn("requesterkind").AsInt32().NotNullable()
					.WithColumn("requesterid").AsString(128).NotNullable()
					.WithColumn("clientapplication").AsInt32().NotNullable()
					.WithColumn("installationlabel").AsString(256).Nullable()
					.WithColumn("sharedmode").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("departmentid").AsInt32().Nullable()
					.WithColumn("purpose").AsInt32().NotNullable()
					.WithColumn("operation").AsString(64).Nullable()
					.WithColumn("lockversion").AsInt64().Nullable()
					.WithColumn("authenticationgeneration").AsInt64().NotNullable()
					.WithColumn("matchnumberhash").AsBinary(32).NotNullable()
					.WithColumn("originregion").AsString(256).Nullable()
					.WithColumn("state").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("version").AsInt64().NotNullable().WithDefaultValue(1)
					.WithColumn("attempts").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("maxattempts").AsInt32().NotNullable()
					.WithColumn("createdonutc").AsDateTime().NotNullable()
					.WithColumn("expiresonutc").AsDateTime().NotNullable()
					.WithColumn("decidedonutc").AsDateTime().Nullable()
					.WithColumn("consumedonutc").AsDateTime().Nullable()
					.WithColumn("endreason").AsInt32().Nullable()
					.WithColumn("approversessionid").AsString(128).Nullable()
					.WithColumn("approverpasskeyid").AsString(36).Nullable();

			// At most one pending request per user (plan section 5.6), absorbed in the insert statement.
			Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_mfaapprovalrequests_pendinguser ON mfaapprovalrequests (userid) WHERE state = 0;");

			// The per-user rate limit and suspension read a user's recent requests.
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_mfaapprovalrequests_usercreated ON mfaapprovalrequests (userid, createdonutc DESC);");
		}

		public override void Down() => throw new System.NotSupportedException("Approval requests are security state; disable the feature instead of dropping it.");
	}
}
