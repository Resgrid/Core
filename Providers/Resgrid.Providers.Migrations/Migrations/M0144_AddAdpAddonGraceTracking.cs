using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
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
	public class M0144_AddAdpAddonGraceTracking : Migration
	{
		private const string Table = "DepartmentDataProtectionPolicies";

		public override void Up()
		{
			if (!Schema.Table(Table).Column("AddonPaidThroughOn").Exists())
				Alter.Table(Table).AddColumn("AddonPaidThroughOn").AsDateTime2().Nullable();

			if (!Schema.Table(Table).Column("AddonBillingMode").Exists())
				Alter.Table(Table).AddColumn("AddonBillingMode").AsInt32().Nullable();

			if (!Schema.Table(Table).Column("AddonDunningStartedOn").Exists())
				Alter.Table(Table).AddColumn("AddonDunningStartedOn").AsDateTime2().Nullable();

			if (!Schema.Table(Table).Column("AddonGraceEndsOn").Exists())
				Alter.Table(Table).AddColumn("AddonGraceEndsOn").AsDateTime2().Nullable();

			if (!Schema.Table(Table).Column("AddonGraceDaysOverride").Exists())
				Alter.Table(Table).AddColumn("AddonGraceDaysOverride").AsInt32().Nullable();
		}

		public override void Down()
		{
			// Reversible: none of this is member data or key material. Losing it costs the grace
			// floor, so a lapse would fall back to the provider's end date — which is why the
			// columns exist, but not a reason to refuse a rollback of an unreleased feature.
			foreach (var column in new[] { "AddonPaidThroughOn", "AddonBillingMode", "AddonDunningStartedOn",
					"AddonGraceEndsOn", "AddonGraceDaysOverride" })
			{
				if (Schema.Table(Table).Column(column).Exists())
					Delete.Column(column).FromTable(Table);
			}
		}
	}
}
