using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Passkey plan Phase 1, slice 10: provider step-up (federated MFA). The department's mapping and its test result, and
	/// the brokered transaction's step-up binding. No IdP token, assertion or secret.
	/// </summary>
	[Migration(251)]
	public class M0251_AddFederatedMfaMapping : Migration
	{
		public override void Up()
		{
			// Provider step-up mapping and its test-before-enable state (plan section 7.8). Existing rows have no mapping.
			if (!Schema.Table("DepartmentSsoConfigs").Column("FederatedMfaMappingJson").Exists())
				Alter.Table("DepartmentSsoConfigs").AddColumn("FederatedMfaMappingJson").AsString(int.MaxValue).Nullable();

			if (!Schema.Table("DepartmentSsoConfigs").Column("FederatedMfaMappingVersion").Exists())
				Alter.Table("DepartmentSsoConfigs").AddColumn("FederatedMfaMappingVersion").AsInt64().NotNullable().WithDefaultValue(0);

			if (!Schema.Table("DepartmentSsoConfigs").Column("FederatedMfaTestedVersion").Exists())
				Alter.Table("DepartmentSsoConfigs").AddColumn("FederatedMfaTestedVersion").AsInt64().Nullable();

			if (!Schema.Table("DepartmentSsoConfigs").Column("FederatedMfaTestedOnUtc").Exists())
				Alter.Table("DepartmentSsoConfigs").AddColumn("FederatedMfaTestedOnUtc").AsDateTime().Nullable();

			if (!Schema.Table("DepartmentSsoConfigs").Column("FederatedMfaTestedByUserId").Exists())
				Alter.Table("DepartmentSsoConfigs").AddColumn("FederatedMfaTestedByUserId").AsString(128).Nullable();

			// Brokered step-up binding and the value the mapping counted as MFA ("kind:value"; a value may be 256 characters).
			if (!Schema.Table("SsoLoginTransactions").Column("Operation").Exists())
				Alter.Table("SsoLoginTransactions").AddColumn("Operation").AsString(64).Nullable();

			if (!Schema.Table("SsoLoginTransactions").Column("LoginTransactionId").Exists())
				Alter.Table("SsoLoginTransactions").AddColumn("LoginTransactionId").AsString(36).Nullable();

			if (!Schema.Table("SsoLoginTransactions").Column("FederatedMappingVersion").Exists())
				Alter.Table("SsoLoginTransactions").AddColumn("FederatedMappingVersion").AsInt64().Nullable();

			if (!Schema.Table("SsoLoginTransactions").Column("FederatedMfaValue").Exists())
				Alter.Table("SsoLoginTransactions").AddColumn("FederatedMfaValue").AsString(512).Nullable();
		}

		public override void Down() => throw new System.NotSupportedException("Provider step-up state is security configuration; disable the feature instead of dropping it.");
	}
}
