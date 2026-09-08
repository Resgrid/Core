using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Contact pre-incident plans (Contacts plan Phase A, A1; registry M0183, taken as the next physical
	/// number under the no-gaps rule): the 1:1 structured NFPA 1620 pre-plan per Contact and its repeating
	/// typed premise hazards. Both tables carry the ADP IsProtected row marker: the text columns are catalog v12 fields
	/// (ProtectedFieldCatalog.ContactPreplansCatalogVersion) and are enveloped in a protected department. Guarded for safe retry.
	/// </summary>
	[Migration(183)]
	public class M0183_AddContactPreplans : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("ContactPreplans").Exists())
			{
				Create.Table("ContactPreplans")
					.WithColumn("ContactPreplanId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ContactId").AsString(128).NotNullable()
					.WithColumn("ConstructionType").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("RoofType").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("OccupancyType").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("OccupancyNotes").AsString(int.MaxValue).Nullable()
					.WithColumn("OccupancyHours").AsString(500).Nullable()
					.WithColumn("OccupantLoad").AsInt32().Nullable()
					.WithColumn("HasOccupantsNeedingAssistance").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("OccupantsNeedingAssistanceNotes").AsString(int.MaxValue).Nullable()
					.WithColumn("GasShutoffLocation").AsString(1000).Nullable()
					.WithColumn("ElectricShutoffLocation").AsString(1000).Nullable()
					.WithColumn("WaterShutoffLocation").AsString(1000).Nullable()
					.WithColumn("UtilityNotes").AsString(int.MaxValue).Nullable()
					.WithColumn("KnoxBoxLocation").AsString(1000).Nullable()
					.WithColumn("GateCode").AsString(1000).Nullable()
					.WithColumn("AlarmPanelLocation").AsString(1000).Nullable()
					.WithColumn("AlarmCompany").AsString(500).Nullable()
					.WithColumn("AlarmCompanyPhone").AsString(100).Nullable()
					.WithColumn("AccessNotes").AsString(int.MaxValue).Nullable()
					.WithColumn("NearestHydrantLocation").AsString(1000).Nullable()
					.WithColumn("RequiredFireFlowGpm").AsInt32().Nullable()
					.WithColumn("WaterSupplyNotes").AsString(int.MaxValue).Nullable()
					.WithColumn("EmergencyContactName").AsString(500).Nullable()
					.WithColumn("EmergencyContactPhone").AsString(100).Nullable()
					.WithColumn("SecondaryContactName").AsString(500).Nullable()
					.WithColumn("SecondaryContactPhone").AsString(100).Nullable()
					.WithColumn("HazmatOnSite").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("GeneralHazardNotes").AsString(int.MaxValue).Nullable()
					.WithColumn("TacticalSummary").AsString(int.MaxValue).Nullable()
					.WithColumn("LastReviewedOn").AsDateTime2().Nullable()
					.WithColumn("ReviewedByUserId").AsString(128).Nullable()
					.WithColumn("NextReviewDue").AsDateTime2().Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable()
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false);

				Create.Index("IX_ContactPreplans_Department").OnTable("ContactPreplans")
					.OnColumn("DepartmentId").Ascending().OnColumn("IsDeleted").Ascending();

				// One live pre-plan per contact; soft-deleted rows do not block a replacement.
				Execute.Sql("CREATE UNIQUE INDEX [UX_ContactPreplans_Contact_Live] ON [ContactPreplans] ([ContactId]) WHERE [IsDeleted] = 0;");
			}

			if (!Schema.Table("ContactPreplanHazards").Exists())
			{
				Create.Table("ContactPreplanHazards")
					.WithColumn("ContactPreplanHazardId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("ContactPreplanId").AsString(36).NotNullable()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ContactId").AsString(128).NotNullable()
					.WithColumn("HazardType").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Severity").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Title").AsString(500).NotNullable()
					.WithColumn("Description").AsString(int.MaxValue).Nullable()
					.WithColumn("LocationDescription").AsString(1000).Nullable()
					.WithColumn("GpsCoordinates").AsString(100).Nullable()
					.WithColumn("ShouldAlert").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable()
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false);

				Create.Index("IX_ContactPreplanHazards_Preplan").OnTable("ContactPreplanHazards")
					.OnColumn("ContactPreplanId").Ascending().OnColumn("IsDeleted").Ascending();
				Create.Index("IX_ContactPreplanHazards_Contact").OnTable("ContactPreplanHazards")
					.OnColumn("DepartmentId").Ascending().OnColumn("ContactId").Ascending().OnColumn("IsDeleted").Ascending();
			}
		}

		public override void Down()
		{
			if (Schema.Table("ContactPreplanHazards").Exists())
				Delete.Table("ContactPreplanHazards");
			if (Schema.Table("ContactPreplans").Exists())
				Delete.Table("ContactPreplans");
		}
	}
}
