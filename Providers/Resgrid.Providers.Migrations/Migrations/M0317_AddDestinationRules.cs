using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Destination rules (Belgian EMS: suggest the nearest open emergency department for a call; generalised so a call type,
	/// a priority and the travelling unit's type say what a destination must offer, e.g. Burns → Burn Center, Helicopter →
	/// Helipad, Water Tender → Water Source with Drafting). One row per rule: optional conditions (call type, priority, unit
	/// type), allowed destination POI types and required / preferred POI capabilities as comma-separated ids. No foreign keys:
	/// a deleted type or capability just stops matching and UnlinkedDepartmentCleanup removes the rows. Existence-guarded for safe retry.
	/// </summary>
	[Migration(317)]
	public class M0317_AddDestinationRules : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("DestinationRules").Exists())
			{
				Create.Table("DestinationRules")
					.WithColumn("DestinationRuleId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("Name").AsString(100).NotNullable()
					.WithColumn("CallTypeId").AsInt32().Nullable()
					.WithColumn("CallPriority").AsInt32().Nullable()
					.WithColumn("UnitTypeId").AsInt32().Nullable()
					.WithColumn("PoiTypeIds").AsString(4000).Nullable()
					.WithColumn("RequiredCapabilityIds").AsString(4000).Nullable()
					.WithColumn("PreferredCapabilityIds").AsString(4000).Nullable()
					.WithColumn("CreatedByUserId").AsString(128).Nullable()
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("UpdatedByUserId").AsString(128).Nullable()
					.WithColumn("UpdatedOn").AsDateTime2().Nullable();

				Create.Index("IX_DestinationRules_Department").OnTable("DestinationRules").OnColumn("DepartmentId").Ascending();
			}
		}

		public override void Down()
		{
			if (Schema.Table("DestinationRules").Exists())
				Delete.Table("DestinationRules");
		}
	}
}
