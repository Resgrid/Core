using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Grace tracking for an ADP addon that has not been paid on time (ADP plan section 17.3).
	///
	/// Before this, a lapse only had the provider's own word for when protection should end, and the
	/// provider's word is shaped entirely around card billing. An invoiced department on NET45 has
	/// not failed a payment at all — no charge was attempted — so a provider-driven end date would
	/// start decrypting a paying customer's data while their invoice is still inside its terms.
	///
	/// The columns record what was paid for, how the department pays, when the current lapse began,
	/// and the resulting floor beneath which offboarding is never scheduled. The floor is computed
	/// once per lapse: recomputing it on every retry webhook would let a permanently failing card
	/// renew its own grace forever.
	/// </summary>
	[Migration(144)]
	public class M0144_AddAdpAddonGraceTrackingPg : Migration
	{
		private const string Table = "departmentdataprotectionpolicies";

		public override void Up()
		{
			if (!Schema.Table(Table).Column("addonpaidthroughon").Exists())
				Alter.Table(Table).AddColumn("addonpaidthroughon").AsDateTime2().Nullable();

			if (!Schema.Table(Table).Column("addonbillingmode").Exists())
				Alter.Table(Table).AddColumn("addonbillingmode").AsInt32().Nullable();

			if (!Schema.Table(Table).Column("addondunningstartedon").Exists())
				Alter.Table(Table).AddColumn("addondunningstartedon").AsDateTime2().Nullable();

			if (!Schema.Table(Table).Column("addongraceendson").Exists())
				Alter.Table(Table).AddColumn("addongraceendson").AsDateTime2().Nullable();

			if (!Schema.Table(Table).Column("addongracedaysoverride").Exists())
				Alter.Table(Table).AddColumn("addongracedaysoverride").AsInt32().Nullable();
		}

		public override void Down()
		{
			// Reversible: none of this is member data or key material. Losing it costs the grace
			// floor, so a lapse would fall back to the provider's end date — which is why the
			// columns exist, but not a reason to refuse a rollback of an unreleased feature.
			foreach (var column in new[] { "addonpaidthroughon", "addonbillingmode", "addondunningstartedon",
					"addongraceendson", "addongracedaysoverride" })
			{
				if (Schema.Table(Table).Column(column).Exists())
					Delete.Column(column).FromTable(Table);
			}
		}
	}
}
