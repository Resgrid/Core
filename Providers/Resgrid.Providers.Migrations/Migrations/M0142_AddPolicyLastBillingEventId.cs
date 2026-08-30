using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Records the last ADP addon billing event applied to a department (plan 17.2/17.3).
	///
	/// Payment providers retry and duplicate webhooks, and they deliver out of order. Without a
	/// record of what has already been applied, a replayed "cancelled" would re-schedule an
	/// offboarding the member had since revoked. Storing the provider's event id makes the handler
	/// idempotent against exactly that.
	/// </summary>
	[Migration(142)]
	public class M0142_AddPolicyLastBillingEventId : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("DepartmentDataProtectionPolicies").Column("LastBillingEventId").Exists())
				Alter.Table("DepartmentDataProtectionPolicies")
					.AddColumn("LastBillingEventId").AsString(256).Nullable();
		}

		public override void Down()
		{
			if (Schema.Table("DepartmentDataProtectionPolicies").Column("LastBillingEventId").Exists())
				Delete.Column("LastBillingEventId").FromTable("DepartmentDataProtectionPolicies");
		}
	}
}
