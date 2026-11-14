using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Destination capabilities and availability (Belgian EMS: send a STEMI patient to the nearest cath lab, a stroke to a
	/// stroke centre, and know when a hospital is on diversion):
	///  * PoiCapabilities: the department's list of what its POIs can offer ("Cath Lab", "Trauma Center").
	///  * PoiCapabilityAssignments: tags a POI with one of them (unique per POI and capability).
	///  * PoiAvailabilities: one current availability report per POI (Limited / Diversion / Closed, capabilities down, note,
	///    expiry); open destinations have none.
	/// DepartmentId is on every row so a department loads its rows in one query and the department purge sweeps them. No
	/// foreign keys: the capability and POI deletes and UnlinkedDepartmentCleanup remove the rows. Existence-guarded for safe retry.
	/// </summary>
	[Migration(315)]
	public class M0315_AddPoiCapabilitiesAndAvailability : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("PoiCapabilities").Exists())
			{
				Create.Table("PoiCapabilities")
					.WithColumn("PoiCapabilityId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("Name").AsString(100).NotNullable()
					.WithColumn("Sort").AsInt32().NotNullable().WithDefaultValue(0);

				Create.Index("IX_PoiCapabilities_Department").OnTable("PoiCapabilities").OnColumn("DepartmentId").Ascending();
			}

			if (!Schema.Table("PoiCapabilityAssignments").Exists())
			{
				Create.Table("PoiCapabilityAssignments")
					.WithColumn("PoiCapabilityAssignmentId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("PoiId").AsInt32().NotNullable()
					.WithColumn("PoiCapabilityId").AsString(36).NotNullable();

				Create.Index("IX_PoiCapabilityAssignments_Department").OnTable("PoiCapabilityAssignments").OnColumn("DepartmentId").Ascending();
				Create.Index("UX_PoiCapabilityAssignments_Poi_Capability").OnTable("PoiCapabilityAssignments")
					.OnColumn("PoiId").Ascending().OnColumn("PoiCapabilityId").Ascending().WithOptions().Unique();
			}

			if (!Schema.Table("PoiAvailabilities").Exists())
			{
				Create.Table("PoiAvailabilities")
					.WithColumn("PoiAvailabilityId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("PoiId").AsInt32().NotNullable()
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("UnavailableCapabilityIds").AsString(4000).Nullable()
					.WithColumn("Note").AsString(500).Nullable()
					.WithColumn("SetByUserId").AsString(128).Nullable()
					.WithColumn("SetOn").AsDateTime2().NotNullable()
					.WithColumn("ExpiresOn").AsDateTime2().Nullable();

				Create.Index("IX_PoiAvailabilities_Department").OnTable("PoiAvailabilities").OnColumn("DepartmentId").Ascending();
				Create.Index("UX_PoiAvailabilities_Poi").OnTable("PoiAvailabilities").OnColumn("PoiId").Ascending().WithOptions().Unique();
			}
		}

		public override void Down()
		{
			if (Schema.Table("PoiAvailabilities").Exists())
				Delete.Table("PoiAvailabilities");

			if (Schema.Table("PoiCapabilityAssignments").Exists())
				Delete.Table("PoiCapabilityAssignments");

			if (Schema.Table("PoiCapabilities").Exists())
				Delete.Table("PoiCapabilities");
		}
	}
}
