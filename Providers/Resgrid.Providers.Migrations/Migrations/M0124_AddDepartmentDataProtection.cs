using System;
using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Advanced Data Protection (ADP) Phase 1 schemas: durable per-department protection policy
	/// (DepartmentDataProtectionPolicies.State is the single data-safety truth), wrapped department
	/// key versions (never plaintext key material), resumable bulk-migration cursors, independent
	/// per-channel egress policy, and department-owned member sensitive data moved off the global
	/// UserProfile row. All tables ship inert while every department is Disabled.
	/// Runs outside a migration transaction so ONLINE index builds do not hold schema locks until
	/// commit; every statement is existence-guarded for safe retry.
	/// </summary>
	[Migration(124, TransactionBehavior.None)]
	public class M0124_AddDepartmentDataProtection : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("DepartmentDataProtectionPolicies").Exists())
				Create.Table("DepartmentDataProtectionPolicies")
					.WithColumn("DepartmentDataProtectionPolicyId").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("State").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("CatalogVersion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ActiveMigrationKind").AsInt32().Nullable()
					.WithColumn("StepUpWindowMinutes").AsInt32().NotNullable().WithDefaultValue(15)
					.WithColumn("StepUpWindowReason").AsString(int.MaxValue).Nullable()
					.WithColumn("PolicyEpoch").AsInt64().NotNullable().WithDefaultValue(0L)
					.WithColumn("MinimumClientVersionsJson").AsString(int.MaxValue).Nullable()
					.WithColumn("AcknowledgementsJson").AsString(int.MaxValue).Nullable()
					.WithColumn("AcknowledgedByUserId").AsString(128).Nullable()
					.WithColumn("AcknowledgedOn").AsDateTime2().Nullable()
					.WithColumn("EnrollmentFlagEvaluationJson").AsString(int.MaxValue).Nullable()
					.WithColumn("AddonBillingReference").AsString(256).Nullable()
					.WithColumn("MigrationWindowStartLocal").AsString(5).Nullable()
					.WithColumn("MigrationWindowEndLocal").AsString(5).Nullable()
					.WithColumn("MigrationWindowTimeZone").AsString(128).Nullable()
					.WithColumn("OffboardingEffectiveOn").AsDateTime2().Nullable()
					.WithColumn("OffboardingSource").AsInt32().Nullable()
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("CreatedByUserId").AsString(128).Nullable()
					.WithColumn("UpdatedOn").AsDateTime2().Nullable()
					.WithColumn("UpdatedByUserId").AsString(128).Nullable();

			// One policy row per department.
			Execute.Sql(SqlServerOnlineIndex.Create("UX_DepartmentDataProtectionPolicies_DepartmentId",
				"DepartmentDataProtectionPolicies", new[] { "[DepartmentId] ASC" }, unique: true));

			if (!Schema.Table("DepartmentDataProtectionKeys").Exists())
				Create.Table("DepartmentDataProtectionKeys")
					.WithColumn("DepartmentDataProtectionKeyId").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("Version").AsInt32().NotNullable()
					.WithColumn("WrappedKey").AsString(int.MaxValue).NotNullable()
					.WithColumn("ProviderType").AsString(64).NotNullable()
					.WithColumn("ProviderKeyReference").AsString(256).NotNullable()
					.WithColumn("ProviderKeyVersion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("ActivatedOn").AsDateTime2().Nullable()
					.WithColumn("RetiredOn").AsDateTime2().Nullable();

			// Envelope headers reference (DepartmentId, Version); that pair must be unique.
			Execute.Sql(SqlServerOnlineIndex.Create("UX_DepartmentDataProtectionKeys_Department_Version",
				"DepartmentDataProtectionKeys", new[] { "[DepartmentId] ASC", "[Version] ASC" }, unique: true));

			Execute.Sql(SqlServerOnlineIndex.Create("IX_DepartmentDataProtectionKeys_Department_Status",
				"DepartmentDataProtectionKeys", new[] { "[DepartmentId] ASC", "[Status] ASC" }));

			if (!Schema.Table("DepartmentDataProtectionMigrations").Exists())
				Create.Table("DepartmentDataProtectionMigrations")
					.WithColumn("DepartmentDataProtectionMigrationId").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("Kind").AsInt32().NotNullable()
					.WithColumn("CatalogVersion").AsInt32().NotNullable()
					.WithColumn("TargetKeyVersion").AsInt32().Nullable()
					.WithColumn("TargetTable").AsString(128).NotNullable()
					.WithColumn("Cursor").AsString(256).Nullable()
					.WithColumn("RowsTotal").AsInt64().NotNullable().WithDefaultValue(0L)
					.WithColumn("RowsProcessed").AsInt64().NotNullable().WithDefaultValue(0L)
					.WithColumn("RowsAlreadyProtected").AsInt64().NotNullable().WithDefaultValue(0L)
					.WithColumn("RowsAnomalous").AsInt64().NotNullable().WithDefaultValue(0L)
					.WithColumn("VerificationState").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Attempts").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("LastErrorCode").AsString(64).Nullable()
					.WithColumn("CorrelationId").AsString(128).Nullable()
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("StartedOn").AsDateTime2().Nullable()
					.WithColumn("CheckpointedOn").AsDateTime2().Nullable()
					.WithColumn("CompletedOn").AsDateTime2().Nullable();

			// One active cursor row per table per run kind; completed history rows are unconstrained.
			Execute.Sql(SqlServerOnlineIndex.Create("UX_DepartmentDataProtectionMigrations_Active",
				"DepartmentDataProtectionMigrations", new[] { "[DepartmentId] ASC", "[Kind] ASC", "[TargetTable] ASC" },
				unique: true, filter: "[CompletedOn] IS NULL"));

			Execute.Sql(SqlServerOnlineIndex.Create("IX_DepartmentDataProtectionMigrations_Department_Kind",
				"DepartmentDataProtectionMigrations", new[] { "[DepartmentId] ASC", "[Kind] ASC", "[CompletedOn] ASC" }));

			if (!Schema.Table("DepartmentProtectedDataEgressPolicies").Exists())
				Create.Table("DepartmentProtectedDataEgressPolicies")
					.WithColumn("DepartmentProtectedDataEgressPolicyId").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("PushMode").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("EmailMode").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("SmsMode").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("VoiceMode").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("PinChallengeExpiryMinutes").AsInt32().NotNullable().WithDefaultValue(5)
					.WithColumn("PinMaxAttempts").AsInt32().NotNullable().WithDefaultValue(3)
					.WithColumn("PinLockoutMinutes").AsInt32().NotNullable().WithDefaultValue(15)
					.WithColumn("AcknowledgementVersion").AsString(64).Nullable()
					.WithColumn("AcknowledgedByUserId").AsString(128).Nullable()
					.WithColumn("AcknowledgedOn").AsDateTime2().Nullable()
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("UpdatedOn").AsDateTime2().Nullable()
					.WithColumn("UpdatedByUserId").AsString(128).Nullable();

			Execute.Sql(SqlServerOnlineIndex.Create("UX_DepartmentProtectedDataEgressPolicies_DepartmentId",
				"DepartmentProtectedDataEgressPolicies", new[] { "[DepartmentId] ASC" }, unique: true));

			if (!Schema.Table("DepartmentMemberSensitiveData").Exists())
				Create.Table("DepartmentMemberSensitiveData")
					.WithColumn("DepartmentMemberSensitiveDataId").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("UserId").AsString(128).NotNullable()
					.WithColumn("ProtectionId").AsString(64).NotNullable()
					.WithColumn("IdentificationNumber").AsString(int.MaxValue).Nullable()
					.WithColumn("EmergencyContactName").AsString(int.MaxValue).Nullable()
					.WithColumn("EmergencyContactPhone").AsString(int.MaxValue).Nullable()
					.WithColumn("Notes").AsString(int.MaxValue).Nullable()
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedCatalogVersion").AsInt32().Nullable()
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("UpdatedOn").AsDateTime2().Nullable();

			Execute.Sql(SqlServerOnlineIndex.Create("UX_DepartmentMemberSensitiveData_Department_User",
				"DepartmentMemberSensitiveData", new[] { "[DepartmentId] ASC", "[UserId] ASC" }, unique: true));

			Execute.Sql(SqlServerOnlineIndex.Create("IX_DepartmentMemberSensitiveData_UserId",
				"DepartmentMemberSensitiveData", new[] { "[UserId] ASC" }));
		}

		public override void Down()
		{
			// Down drops inert Phase 1 schema. NEVER run this against a department whose durable state
			// has left Disabled — DepartmentDataProtectionKeys rows are the only path to that
			// department's ciphertext.
			if (Schema.Table("DepartmentMemberSensitiveData").Exists())
				Delete.Table("DepartmentMemberSensitiveData");
			if (Schema.Table("DepartmentProtectedDataEgressPolicies").Exists())
				Delete.Table("DepartmentProtectedDataEgressPolicies");
			if (Schema.Table("DepartmentDataProtectionMigrations").Exists())
				Delete.Table("DepartmentDataProtectionMigrations");
			if (Schema.Table("DepartmentDataProtectionKeys").Exists())
				Delete.Table("DepartmentDataProtectionKeys");
			if (Schema.Table("DepartmentDataProtectionPolicies").Exists())
				Delete.Table("DepartmentDataProtectionPolicies");
		}
	}
}
