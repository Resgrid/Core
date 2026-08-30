using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
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
	public class M0142_AddPolicyLastBillingEventIdPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("departmentdataprotectionpolicies").Column("lastbillingeventid").Exists())
				Alter.Table("departmentdataprotectionpolicies")
					.AddColumn("lastbillingeventid").AsCustom("citext").Nullable();
		}

		public override void Down()
		{
			if (Schema.Table("departmentdataprotectionpolicies").Column("lastbillingeventid").Exists())
				Delete.Column("lastbillingeventid").FromTable("departmentdataprotectionpolicies");
		}
	}
}
