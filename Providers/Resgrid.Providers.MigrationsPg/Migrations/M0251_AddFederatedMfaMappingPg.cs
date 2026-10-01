using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Passkey plan Phase 1, slice 10: provider step-up (federated MFA). The department's mapping and its test result, and
	/// the brokered transaction's step-up binding. No IdP token, assertion or secret.
	/// </summary>
	[Migration(251)]
	public class M0251_AddFederatedMfaMappingPg : Migration
	{
		public override void Up()
		{
			// Provider step-up mapping and its test-before-enable state (plan section 7.8). Existing rows have no mapping.
			if (!Schema.Table("departmentssoconfigs").Column("federatedmfamappingjson").Exists())
				Alter.Table("departmentssoconfigs").AddColumn("federatedmfamappingjson").AsString(int.MaxValue).Nullable();

			if (!Schema.Table("departmentssoconfigs").Column("federatedmfamappingversion").Exists())
				Alter.Table("departmentssoconfigs").AddColumn("federatedmfamappingversion").AsInt64().NotNullable().WithDefaultValue(0);

			if (!Schema.Table("departmentssoconfigs").Column("federatedmfatestedversion").Exists())
				Alter.Table("departmentssoconfigs").AddColumn("federatedmfatestedversion").AsInt64().Nullable();

			if (!Schema.Table("departmentssoconfigs").Column("federatedmfatestedonutc").Exists())
				Alter.Table("departmentssoconfigs").AddColumn("federatedmfatestedonutc").AsDateTime().Nullable();

			if (!Schema.Table("departmentssoconfigs").Column("federatedmfatestedbyuserid").Exists())
				Alter.Table("departmentssoconfigs").AddColumn("federatedmfatestedbyuserid").AsString(128).Nullable();

			// Brokered step-up binding and the value the mapping counted as MFA ("kind:value"; a value may be 256 characters).
			if (!Schema.Table("ssologintransactions").Column("operation").Exists())
				Alter.Table("ssologintransactions").AddColumn("operation").AsString(64).Nullable();

			if (!Schema.Table("ssologintransactions").Column("logintransactionid").Exists())
				Alter.Table("ssologintransactions").AddColumn("logintransactionid").AsString(36).Nullable();

			if (!Schema.Table("ssologintransactions").Column("federatedmappingversion").Exists())
				Alter.Table("ssologintransactions").AddColumn("federatedmappingversion").AsInt64().Nullable();

			if (!Schema.Table("ssologintransactions").Column("federatedmfavalue").Exists())
				Alter.Table("ssologintransactions").AddColumn("federatedmfavalue").AsString(512).Nullable();
		}

		public override void Down() => throw new System.NotSupportedException("Provider step-up state is security configuration; disable the feature instead of dropping it.");
	}
}
