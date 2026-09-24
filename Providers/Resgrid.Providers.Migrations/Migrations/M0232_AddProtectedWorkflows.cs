using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// ADP Protected Workflows (push model): the department opt-in on the egress policy, per-workflow protected
	/// releases (field allow-list, pinned host and credential, attestation, approval, expiry), and the append-only,
	/// per-department hash-chained disclosure log. Everything ships inert: every department's toggle is off and no
	/// release exists. WorkflowProtectedReleases.WorkflowId is deliberately not a foreign key — a deleted workflow's
	/// release is revoked and kept, and disclosures are retained with the department's audit data.
	/// Runs outside a migration transaction so ONLINE index builds do not hold schema locks; every statement is
	/// existence-guarded for a safe retry.
	/// </summary>
	[Migration(232, TransactionBehavior.None)]
	public class M0232_AddProtectedWorkflows : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
IF COL_LENGTH('DepartmentProtectedDataEgressPolicies', 'ProtectedWorkflowsEnabled') IS NULL
	ALTER TABLE [DepartmentProtectedDataEgressPolicies] ADD [ProtectedWorkflowsEnabled] bit NOT NULL
		CONSTRAINT [DF_DepartmentProtectedDataEgressPolicies_ProtectedWorkflowsEnabled] DEFAULT (0);
IF COL_LENGTH('DepartmentProtectedDataEgressPolicies', 'ProtectedWorkflowsAckVersion') IS NULL
	ALTER TABLE [DepartmentProtectedDataEgressPolicies] ADD [ProtectedWorkflowsAckVersion] nvarchar(64) NULL;
IF COL_LENGTH('DepartmentProtectedDataEgressPolicies', 'ProtectedWorkflowsAckByUserId') IS NULL
	ALTER TABLE [DepartmentProtectedDataEgressPolicies] ADD [ProtectedWorkflowsAckByUserId] nvarchar(128) NULL;
IF COL_LENGTH('DepartmentProtectedDataEgressPolicies', 'ProtectedWorkflowsAckOn') IS NULL
	ALTER TABLE [DepartmentProtectedDataEgressPolicies] ADD [ProtectedWorkflowsAckOn] datetime2 NULL;
IF COL_LENGTH('DepartmentProtectedDataEgressPolicies', 'ProtectedWorkflowsRequireSecondApprover') IS NULL
	ALTER TABLE [DepartmentProtectedDataEgressPolicies] ADD [ProtectedWorkflowsRequireSecondApprover] bit NOT NULL
		CONSTRAINT [DF_DepartmentProtectedDataEgressPolicies_ProtectedWorkflowsRequireSecondApprover] DEFAULT (0);
IF COL_LENGTH('DepartmentProtectedDataEgressPolicies', 'ProtectedWorkflowsRelaxRequestedByUserId') IS NULL
	ALTER TABLE [DepartmentProtectedDataEgressPolicies] ADD [ProtectedWorkflowsRelaxRequestedByUserId] nvarchar(128) NULL;
IF COL_LENGTH('DepartmentProtectedDataEgressPolicies', 'ProtectedWorkflowsRelaxRequestedOn') IS NULL
	ALTER TABLE [DepartmentProtectedDataEgressPolicies] ADD [ProtectedWorkflowsRelaxRequestedOn] datetime2 NULL;");

			if (!Schema.Table("WorkflowProtectedReleases").Exists())
				Create.Table("WorkflowProtectedReleases")
					.WithColumn("WorkflowProtectedReleaseId").AsString(128).NotNullable().PrimaryKey()
					.WithColumn("WorkflowId").AsString(128).NotNullable()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("State").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("SuspendedReason").AsString(64).Nullable()
					.WithColumn("AllowedFieldIds").AsString(int.MaxValue).Nullable()
					.WithColumn("DestinationScheme").AsString(8).Nullable()
					.WithColumn("DestinationHost").AsString(255).Nullable()
					.WithColumn("TokenHost").AsString(255).Nullable()
					.WithColumn("WorkflowCredentialId").AsString(128).Nullable()
					.WithColumn("AuthMethod").AsString(32).Nullable()
					.WithColumn("AllowsRestricted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RestrictedAckVersion").AsString(64).Nullable()
					.WithColumn("RestrictedAckByUserId").AsString(128).Nullable()
					.WithColumn("AllowsPart2").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("Part2AckVersion").AsString(64).Nullable()
					.WithColumn("Part2AckByUserId").AsString(128).Nullable()
					.WithColumn("ConfigFingerprint").AsString(64).Nullable()
					.WithColumn("RecipientType").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("RecipientName").AsString(200).Nullable()
					.WithColumn("Purpose").AsString(500).Nullable()
					.WithColumn("AckVersion").AsString(64).Nullable()
					.WithColumn("RequestedByUserId").AsString(128).Nullable()
					.WithColumn("RequestedOn").AsDateTime2().Nullable()
					.WithColumn("ApprovedByUserId").AsString(128).Nullable()
					.WithColumn("ApprovedOn").AsDateTime2().Nullable()
					.WithColumn("ExpiresOn").AsDateTime2().Nullable()
					.WithColumn("ExpiryNoticeSentDays").AsInt32().Nullable()
					.WithColumn("RevokedByUserId").AsString(128).Nullable()
					.WithColumn("RevokedOn").AsDateTime2().Nullable()
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("UpdatedOn").AsDateTime2().Nullable()
					.WithColumn("Version").AsInt32().NotNullable().WithDefaultValue(1);

			// One current (non-revoked) release per workflow; revoked rows are history.
			Execute.Sql(SqlServerOnlineIndex.Create("UX_WorkflowProtectedReleases_Workflow_Current",
				"WorkflowProtectedReleases", new[] { "[WorkflowId] ASC" }, unique: true, filter: "[State] <> 5"));
			Execute.Sql(SqlServerOnlineIndex.Create("IX_WorkflowProtectedReleases_Workflow_Created",
				"WorkflowProtectedReleases", new[] { "[WorkflowId] ASC", "[CreatedOn] DESC" }));
			Execute.Sql(SqlServerOnlineIndex.Create("IX_WorkflowProtectedReleases_DepartmentId",
				"WorkflowProtectedReleases", new[] { "[DepartmentId] ASC" }));
			Execute.Sql(SqlServerOnlineIndex.Create("IX_WorkflowProtectedReleases_State",
				"WorkflowProtectedReleases", new[] { "[State] ASC" }));
			Execute.Sql(SqlServerOnlineIndex.Create("IX_WorkflowProtectedReleases_WorkflowCredentialId",
				"WorkflowProtectedReleases", new[] { "[WorkflowCredentialId] ASC" }));

			if (!Schema.Table("ProtectedWorkflowDisclosures").Exists())
				Create.Table("ProtectedWorkflowDisclosures")
					.WithColumn("ProtectedWorkflowDisclosureId").AsString(128).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ChainSequence").AsInt64().NotNullable()
					.WithColumn("RecordType").AsString(32).NotNullable()
					.WithColumn("EventType").AsString(64).Nullable()
					.WithColumn("ActorUserId").AsString(128).Nullable()
					.WithColumn("WorkflowId").AsString(128).Nullable()
					.WithColumn("WorkflowRunId").AsString(128).Nullable()
					.WithColumn("WorkflowStepId").AsString(128).Nullable()
					.WithColumn("WorkflowProtectedReleaseId").AsString(128).Nullable()
					.WithColumn("EntityType").AsString(32).Nullable()
					.WithColumn("EntityId").AsString(64).Nullable()
					.WithColumn("FieldIds").AsString(int.MaxValue).Nullable()
					.WithColumn("DestinationHost").AsString(255).Nullable()
					.WithColumn("PayloadSha256").AsString(64).Nullable()
					.WithColumn("PayloadBytes").AsInt32().Nullable()
					.WithColumn("HttpStatus").AsInt32().Nullable()
					.WithColumn("Outcome").AsString(32).Nullable()
					.WithColumn("IsTest").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("BrokerRequestId").AsString(64).Nullable()
					.WithColumn("Detail").AsString(500).Nullable()
					.WithColumn("ContentType").AsString(64).Nullable()
					.WithColumn("CapturedKeys").AsString(1000).Nullable()
					.WithColumn("OccurredOn").AsDateTime2().NotNullable()
					.WithColumn("PrevHash").AsString(64).NotNullable()
					.WithColumn("Hash").AsString(64).NotNullable();

			// The chain position is unique per department: a concurrent append loses the race and re-links.
			Execute.Sql(SqlServerOnlineIndex.Create("UX_ProtectedWorkflowDisclosures_Department_Sequence",
				"ProtectedWorkflowDisclosures", new[] { "[DepartmentId] ASC", "[ChainSequence] ASC" }, unique: true));
			Execute.Sql(SqlServerOnlineIndex.Create("IX_ProtectedWorkflowDisclosures_Department_Workflow",
				"ProtectedWorkflowDisclosures", new[] { "[DepartmentId] ASC", "[WorkflowId] ASC", "[OccurredOn] DESC" }));
			Execute.Sql(SqlServerOnlineIndex.Create("IX_ProtectedWorkflowDisclosures_Department_Entity",
				"ProtectedWorkflowDisclosures", new[] { "[DepartmentId] ASC", "[EntityId] ASC" }));
		}

		public override void Down()
		{
			// Down drops the disclosure chain, which is audit data; never run it against a department that has used
			// Protected Workflows unless that audit trail has been exported and retained elsewhere.
			if (Schema.Table("ProtectedWorkflowDisclosures").Exists())
				Delete.Table("ProtectedWorkflowDisclosures");
			if (Schema.Table("WorkflowProtectedReleases").Exists())
				Delete.Table("WorkflowProtectedReleases");

			Execute.Sql(@"
IF COL_LENGTH('DepartmentProtectedDataEgressPolicies', 'ProtectedWorkflowsRelaxRequestedOn') IS NOT NULL
	ALTER TABLE [DepartmentProtectedDataEgressPolicies] DROP COLUMN [ProtectedWorkflowsRelaxRequestedOn];
IF COL_LENGTH('DepartmentProtectedDataEgressPolicies', 'ProtectedWorkflowsRelaxRequestedByUserId') IS NOT NULL
	ALTER TABLE [DepartmentProtectedDataEgressPolicies] DROP COLUMN [ProtectedWorkflowsRelaxRequestedByUserId];
IF COL_LENGTH('DepartmentProtectedDataEgressPolicies', 'ProtectedWorkflowsRequireSecondApprover') IS NOT NULL
BEGIN
	ALTER TABLE [DepartmentProtectedDataEgressPolicies] DROP CONSTRAINT [DF_DepartmentProtectedDataEgressPolicies_ProtectedWorkflowsRequireSecondApprover];
	ALTER TABLE [DepartmentProtectedDataEgressPolicies] DROP COLUMN [ProtectedWorkflowsRequireSecondApprover];
END
IF COL_LENGTH('DepartmentProtectedDataEgressPolicies', 'ProtectedWorkflowsAckOn') IS NOT NULL
	ALTER TABLE [DepartmentProtectedDataEgressPolicies] DROP COLUMN [ProtectedWorkflowsAckOn];
IF COL_LENGTH('DepartmentProtectedDataEgressPolicies', 'ProtectedWorkflowsAckByUserId') IS NOT NULL
	ALTER TABLE [DepartmentProtectedDataEgressPolicies] DROP COLUMN [ProtectedWorkflowsAckByUserId];
IF COL_LENGTH('DepartmentProtectedDataEgressPolicies', 'ProtectedWorkflowsAckVersion') IS NOT NULL
	ALTER TABLE [DepartmentProtectedDataEgressPolicies] DROP COLUMN [ProtectedWorkflowsAckVersion];
IF COL_LENGTH('DepartmentProtectedDataEgressPolicies', 'ProtectedWorkflowsEnabled') IS NOT NULL
BEGIN
	ALTER TABLE [DepartmentProtectedDataEgressPolicies] DROP CONSTRAINT [DF_DepartmentProtectedDataEgressPolicies_ProtectedWorkflowsEnabled];
	ALTER TABLE [DepartmentProtectedDataEgressPolicies] DROP COLUMN [ProtectedWorkflowsEnabled];
END");
		}
	}
}
