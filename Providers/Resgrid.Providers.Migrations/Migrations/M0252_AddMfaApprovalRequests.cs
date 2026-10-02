using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Passkey plan Phase 1, slice 11: Responder approval requests (plan section 5.6). The match number is stored only as a
	/// hash bound to its request; no assertion, token or push payload is kept.
	/// </summary>
	[Migration(252)]
	public class M0252_AddMfaApprovalRequests : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("MfaApprovalRequests").Exists())
				Create.Table("MfaApprovalRequests")
					.WithColumn("MfaApprovalRequestId").AsString(36).PrimaryKey().NotNullable()
					.WithColumn("UserId").AsString(128).NotNullable()
					.WithColumn("RequesterKind").AsInt32().NotNullable()
					.WithColumn("RequesterId").AsString(128).NotNullable()
					.WithColumn("ClientApplication").AsInt32().NotNullable()
					.WithColumn("InstallationLabel").AsString(256).Nullable()
					.WithColumn("SharedMode").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("DepartmentId").AsInt32().Nullable()
					.WithColumn("Purpose").AsInt32().NotNullable()
					.WithColumn("Operation").AsString(64).Nullable()
					.WithColumn("LockVersion").AsInt64().Nullable()
					.WithColumn("AuthenticationGeneration").AsInt64().NotNullable()
					.WithColumn("MatchNumberHash").AsBinary(32).NotNullable()
					.WithColumn("OriginRegion").AsString(256).Nullable()
					.WithColumn("State").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Version").AsInt64().NotNullable().WithDefaultValue(1)
					.WithColumn("Attempts").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("MaxAttempts").AsInt32().NotNullable()
					.WithColumn("CreatedOnUtc").AsDateTime().NotNullable()
					.WithColumn("ExpiresOnUtc").AsDateTime().NotNullable()
					.WithColumn("DecidedOnUtc").AsDateTime().Nullable()
					.WithColumn("ConsumedOnUtc").AsDateTime().Nullable()
					.WithColumn("EndReason").AsInt32().Nullable()
					.WithColumn("ApproverSessionId").AsString(128).Nullable()
					.WithColumn("ApproverPasskeyId").AsString(36).Nullable();

			// At most one pending request per user (plan section 5.6), absorbed in the insert statement.
			if (!Schema.Table("MfaApprovalRequests").Index("UX_MfaApprovalRequests_PendingUser").Exists())
				Execute.Sql("CREATE UNIQUE NONCLUSTERED INDEX UX_MfaApprovalRequests_PendingUser ON MfaApprovalRequests (UserId) WHERE State = 0;");

			// The per-user rate limit and suspension read a user's recent requests.
			if (!Schema.Table("MfaApprovalRequests").Index("IX_MfaApprovalRequests_UserCreated").Exists())
				Create.Index("IX_MfaApprovalRequests_UserCreated").OnTable("MfaApprovalRequests")
					.OnColumn("UserId").Ascending()
					.OnColumn("CreatedOnUtc").Descending();
		}

		public override void Down() => throw new System.NotSupportedException("Approval requests are security state; disable the feature instead of dropping it.");
	}
}
