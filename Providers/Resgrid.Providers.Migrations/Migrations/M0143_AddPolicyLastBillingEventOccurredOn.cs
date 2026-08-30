using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Orders ADP addon billing events so a stale redelivery cannot be re-applied (plan 17.2/17.3).
	///
	/// M0142 records the id of the last event applied, which stops an immediate duplicate. It cannot
	/// stop a LATE one: cancel, then renew (which withdraws the scheduled offboarding and takes over
	/// the single id slot), then the provider redelivers the cancel — its id no longer matches, so
	/// the handler would schedule the offboarding the renewal had just withdrawn.
	///
	/// Recording when the applied event occurred at the provider gives the handler an ordering to
	/// compare against, and anything older than the watermark is ignored.
	/// </summary>
	[Migration(143)]
	public class M0143_AddPolicyLastBillingEventOccurredOn : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("DepartmentDataProtectionPolicies").Column("LastBillingEventOccurredOn").Exists())
				Alter.Table("DepartmentDataProtectionPolicies")
					.AddColumn("LastBillingEventOccurredOn").AsDateTime2().Nullable();
		}

		public override void Down()
		{
			// Purely reversible: the column carries no member data and no key material. Losing it
			// only costs the ordering guard, and M0142's id check still stops a plain duplicate.
			if (Schema.Table("DepartmentDataProtectionPolicies").Column("LastBillingEventOccurredOn").Exists())
				Delete.Column("LastBillingEventOccurredOn").FromTable("DepartmentDataProtectionPolicies");
		}
	}
}
