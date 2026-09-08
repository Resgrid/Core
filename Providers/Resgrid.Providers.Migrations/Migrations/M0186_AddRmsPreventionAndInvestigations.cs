using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// RMS-5 prevention and investigations (RMS plan sections 4.3, 4.4 and 6; registry M0186): the occupancy/property
	/// master with role-separated Contact links, hazards, the ContactPreplan/Contact/POI crosswalk, field provenance and
	/// the per-department structure-write ownership row; inspection programs, adopted code sets, inspections and
	/// violations; hydrants with flow tests and maintenance; permit types, permits and plan reviews; community risk
	/// reduction activities; prevention attachments and number sequences; investigation cases with members, linked
	/// incidents, notes, evidence, an append-only custody chain and referrals. Seeds the seven module feature flags off.
	/// Every table carries DepartmentId and ProtectionId; the cataloged text columns (ADP catalog v13) carry the
	/// IsProtected marker. Guarded for safe retry.
	/// </summary>
	[Migration(186)]
	public class M0186_AddRmsPreventionAndInvestigations : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("RmsOccupancies").Exists())
			{
				Create.Table("RmsOccupancies")
					.WithColumn("RmsOccupancyId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ProtectionId").AsString(36).Nullable()
					.WithColumn("OccupancyNumber").AsString(32).Nullable()
					.WithColumn("Name").AsString(250).NotNullable()
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("MergedIntoOccupancyId").AsString(36).Nullable()
					.WithColumn("AddressText").AsString(500).Nullable()
					.WithColumn("City").AsString(120).Nullable()
					.WithColumn("StateProvince").AsString(120).Nullable()
					.WithColumn("PostalCode").AsString(20).Nullable()
					.WithColumn("Country").AsString(80).Nullable()
					.WithColumn("NormalizedAddress").AsString(500).Nullable()
					.WithColumn("Latitude").AsDecimal(12, 8).Nullable()
					.WithColumn("Longitude").AsDecimal(12, 8).Nullable()
					.WithColumn("ParcelId").AsString(100).Nullable()
					.WithColumn("ConstructionType").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("RoofType").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("OccupancyType").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Stories").AsInt32().Nullable()
					.WithColumn("YearBuilt").AsInt32().Nullable()
					.WithColumn("SquareFeet").AsInt32().Nullable()
					.WithColumn("OccupantLoad").AsInt32().Nullable()
					.WithColumn("OccupancyHours").AsString(250).Nullable()
					.WithColumn("HasOccupantsNeedingAssistance").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("OccupantsNeedingAssistanceNotes").AsString(int.MaxValue).Nullable()
					.WithColumn("SprinklerType").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("HasStandpipe").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("HasFireAlarm").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("FdcLocation").AsString(250).Nullable()
					.WithColumn("GasShutoffLocation").AsString(500).Nullable()
					.WithColumn("ElectricShutoffLocation").AsString(500).Nullable()
					.WithColumn("WaterShutoffLocation").AsString(500).Nullable()
					.WithColumn("UtilityNotes").AsString(int.MaxValue).Nullable()
					.WithColumn("KnoxBoxLocation").AsString(int.MaxValue).Nullable()
					.WithColumn("GateCode").AsString(int.MaxValue).Nullable()
					.WithColumn("AlarmPanelLocation").AsString(int.MaxValue).Nullable()
					.WithColumn("AlarmCompany").AsString(int.MaxValue).Nullable()
					.WithColumn("AlarmCompanyPhone").AsString(int.MaxValue).Nullable()
					.WithColumn("AccessNotes").AsString(int.MaxValue).Nullable()
					.WithColumn("NearestHydrantId").AsString(36).Nullable()
					.WithColumn("RequiredFireFlowGpm").AsInt32().Nullable()
					.WithColumn("WaterSupplyNotes").AsString(int.MaxValue).Nullable()
					.WithColumn("EmergencyContactName").AsString(int.MaxValue).Nullable()
					.WithColumn("EmergencyContactPhone").AsString(int.MaxValue).Nullable()
					.WithColumn("HazmatOnSite").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("GeneralHazardNotes").AsString(int.MaxValue).Nullable()
					.WithColumn("TacticalSummary").AsString(int.MaxValue).Nullable()
					.WithColumn("PoiId").AsInt32().Nullable()
					.WithColumn("LastReviewedOn").AsDateTime2().Nullable()
					.WithColumn("ReviewedByUserId").AsString(128).Nullable()
					.WithColumn("NextReviewDue").AsDateTime2().Nullable()
					.WithColumn("LastInspectedOn").AsDateTime2().Nullable()
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedCatalogVersion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("CreatedByUserId").AsString(128).Nullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable()
					.WithColumn("ModifiedByUserId").AsString(128).Nullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(1L)
					.WithColumn("DeletedOn").AsDateTime2().Nullable();

				Create.Index("IX_RmsOccupancies_Department_Status").OnTable("RmsOccupancies").OnColumn("DepartmentId").Ascending().OnColumn("Status").Ascending().OnColumn("DeletedOn").Ascending();
				Create.Index("IX_RmsOccupancies_Department_Address").OnTable("RmsOccupancies").OnColumn("DepartmentId").Ascending().OnColumn("NormalizedAddress").Ascending();
				Create.Index("IX_RmsOccupancies_Department_Review").OnTable("RmsOccupancies").OnColumn("DepartmentId").Ascending().OnColumn("NextReviewDue").Ascending();
			}
			if (!Schema.Table("RmsOccupancyContactLinks").Exists())
			{
				Create.Table("RmsOccupancyContactLinks")
					.WithColumn("RmsOccupancyContactLinkId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ProtectionId").AsString(36).Nullable()
					.WithColumn("RmsOccupancyId").AsString(36).NotNullable()
					.WithColumn("ContactId").AsString(128).NotNullable()
					.WithColumn("Role").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("IsPrimary").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("CreatedByUserId").AsString(128).Nullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(1L)
					.WithColumn("DeletedOn").AsDateTime2().Nullable();

				Create.Index("IX_RmsOccupancyContactLinks_Occupancy").OnTable("RmsOccupancyContactLinks").OnColumn("DepartmentId").Ascending().OnColumn("RmsOccupancyId").Ascending();
				Create.Index("IX_RmsOccupancyContactLinks_Contact").OnTable("RmsOccupancyContactLinks").OnColumn("DepartmentId").Ascending().OnColumn("ContactId").Ascending();
			}
			if (!Schema.Table("RmsOccupancyHazards").Exists())
			{
				Create.Table("RmsOccupancyHazards")
					.WithColumn("RmsOccupancyHazardId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ProtectionId").AsString(36).Nullable()
					.WithColumn("RmsOccupancyId").AsString(36).NotNullable()
					.WithColumn("HazardType").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Severity").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("Title").AsString(200).NotNullable()
					.WithColumn("Description").AsString(int.MaxValue).Nullable()
					.WithColumn("LocationDescription").AsString(int.MaxValue).Nullable()
					.WithColumn("GpsCoordinates").AsString(int.MaxValue).Nullable()
					.WithColumn("ShouldAlert").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("SourceContactPreplanHazardId").AsString(128).Nullable()
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedCatalogVersion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("CreatedByUserId").AsString(128).Nullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(1L)
					.WithColumn("DeletedOn").AsDateTime2().Nullable();

				Create.Index("IX_RmsOccupancyHazards_Occupancy").OnTable("RmsOccupancyHazards").OnColumn("DepartmentId").Ascending().OnColumn("RmsOccupancyId").Ascending();
			}
			if (!Schema.Table("RmsOccupancyCrosswalks").Exists())
			{
				Create.Table("RmsOccupancyCrosswalks")
					.WithColumn("RmsOccupancyCrosswalkId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ProtectionId").AsString(36).Nullable()
					.WithColumn("RmsOccupancyId").AsString(36).Nullable()
					.WithColumn("SourceKind").AsInt32().NotNullable()
					.WithColumn("SourceId").AsString(128).NotNullable()
					.WithColumn("ContactId").AsString(128).Nullable()
					.WithColumn("SourceDisplayName").AsString(250).Nullable()
					.WithColumn("NormalizedAddress").AsString(500).Nullable()
					.WithColumn("Latitude").AsDecimal(12, 8).Nullable()
					.WithColumn("Longitude").AsDecimal(12, 8).Nullable()
					.WithColumn("MatchConfidence").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("MatchReason").AsString(250).Nullable()
					.WithColumn("SuggestedOccupancyId").AsString(36).Nullable()
					.WithColumn("GroupKey").AsString(64).Nullable()
					.WithColumn("State").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("DecidedOn").AsDateTime2().Nullable()
					.WithColumn("DecidedByUserId").AsString(128).Nullable()
					.WithColumn("InventoriedOn").AsDateTime2().NotNullable()
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(1L);

				Create.Index("IX_RmsOccupancyCrosswalks_Department_State").OnTable("RmsOccupancyCrosswalks").OnColumn("DepartmentId").Ascending().OnColumn("State").Ascending();
				Create.Index("IX_RmsOccupancyCrosswalks_Occupancy").OnTable("RmsOccupancyCrosswalks").OnColumn("DepartmentId").Ascending().OnColumn("RmsOccupancyId").Ascending();
				Create.Index("IX_RmsOccupancyCrosswalks_Contact").OnTable("RmsOccupancyCrosswalks").OnColumn("DepartmentId").Ascending().OnColumn("ContactId").Ascending();
				Create.Index("UX_RmsOccupancyCrosswalks_Source").OnTable("RmsOccupancyCrosswalks").OnColumn("DepartmentId").Ascending().OnColumn("SourceKind").Ascending().OnColumn("SourceId").Ascending().WithOptions().Unique();
			}
			if (!Schema.Table("RmsOccupancyFieldProvenances").Exists())
			{
				Create.Table("RmsOccupancyFieldProvenances")
					.WithColumn("RmsOccupancyFieldProvenanceId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ProtectionId").AsString(36).Nullable()
					.WithColumn("RmsOccupancyId").AsString(36).NotNullable()
					.WithColumn("FieldKey").AsString(100).NotNullable()
					.WithColumn("SourceKind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("SourceId").AsString(128).Nullable()
					.WithColumn("CapturedOn").AsDateTime2().NotNullable()
					.WithColumn("CapturedByUserId").AsString(128).Nullable()
					.WithColumn("ReviewedOn").AsDateTime2().Nullable()
					.WithColumn("ReviewedByUserId").AsString(128).Nullable();

				Create.Index("IX_RmsOccupancyFieldProvenances_Occupancy").OnTable("RmsOccupancyFieldProvenances").OnColumn("DepartmentId").Ascending().OnColumn("RmsOccupancyId").Ascending();
			}
			if (!Schema.Table("RmsOccupancyOwnerships").Exists())
			{
				Create.Table("RmsOccupancyOwnerships")
					.WithColumn("RmsOccupancyOwnershipId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("State").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("InventoriedOn").AsDateTime2().Nullable()
					.WithColumn("SwitchedOn").AsDateTime2().Nullable()
					.WithColumn("SwitchedByUserId").AsString(128).Nullable()
					.WithColumn("Reason").AsString(1000).Nullable()
					.WithColumn("CandidateCount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("LinkedCount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("RejectedCount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(1L);

				Create.Index("UX_RmsOccupancyOwnerships_Department").OnTable("RmsOccupancyOwnerships").OnColumn("DepartmentId").Ascending().WithOptions().Unique();
			}
			if (!Schema.Table("RmsCodeSets").Exists())
			{
				Create.Table("RmsCodeSets")
					.WithColumn("RmsCodeSetId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ProtectionId").AsString(36).Nullable()
					.WithColumn("Name").AsString(200).NotNullable()
					.WithColumn("Edition").AsString(100).Nullable()
					.WithColumn("Jurisdiction").AsString(200).Nullable()
					.WithColumn("IsActive").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("CreatedByUserId").AsString(128).Nullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(1L)
					.WithColumn("DeletedOn").AsDateTime2().Nullable();

				Create.Index("IX_RmsCodeSets_Department").OnTable("RmsCodeSets").OnColumn("DepartmentId").Ascending().OnColumn("IsActive").Ascending();
			}
			if (!Schema.Table("RmsCodeSections").Exists())
			{
				Create.Table("RmsCodeSections")
					.WithColumn("RmsCodeSectionId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ProtectionId").AsString(36).Nullable()
					.WithColumn("RmsCodeSetId").AsString(36).NotNullable()
					.WithColumn("SectionNumber").AsString(64).NotNullable()
					.WithColumn("Title").AsString(300).Nullable()
					.WithColumn("Text").AsString(int.MaxValue).Nullable()
					.WithColumn("DefaultSeverity").AsInt32().NotNullable().WithDefaultValue(2)
					.WithColumn("DefaultCorrectionDays").AsInt32().NotNullable().WithDefaultValue(30)
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(1L)
					.WithColumn("DeletedOn").AsDateTime2().Nullable();

				Create.Index("IX_RmsCodeSections_CodeSet").OnTable("RmsCodeSections").OnColumn("DepartmentId").Ascending().OnColumn("RmsCodeSetId").Ascending();
			}
			if (!Schema.Table("RmsInspectionPrograms").Exists())
			{
				Create.Table("RmsInspectionPrograms")
					.WithColumn("RmsInspectionProgramId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ProtectionId").AsString(36).Nullable()
					.WithColumn("Name").AsString(200).NotNullable()
					.WithColumn("Description").AsString(int.MaxValue).Nullable()
					.WithColumn("OccupancyTypesCsv").AsString(500).Nullable()
					.WithColumn("FrequencyMonths").AsInt32().NotNullable().WithDefaultValue(12)
					.WithColumn("RmsCodeSetId").AsString(36).Nullable()
					.WithColumn("ChecklistJson").AsString(int.MaxValue).Nullable()
					.WithColumn("IsActive").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("CreatedByUserId").AsString(128).Nullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(1L)
					.WithColumn("DeletedOn").AsDateTime2().Nullable();

				Create.Index("IX_RmsInspectionPrograms_Department").OnTable("RmsInspectionPrograms").OnColumn("DepartmentId").Ascending().OnColumn("IsActive").Ascending();
			}
			if (!Schema.Table("RmsInspections").Exists())
			{
				Create.Table("RmsInspections")
					.WithColumn("RmsInspectionId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ProtectionId").AsString(36).Nullable()
					.WithColumn("RmsOccupancyId").AsString(36).NotNullable()
					.WithColumn("RmsInspectionProgramId").AsString(36).Nullable()
					.WithColumn("InspectionNumber").AsString(32).Nullable()
					.WithColumn("State").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("Result").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ScheduledOn").AsDateTime2().Nullable()
					.WithColumn("StartedOn").AsDateTime2().Nullable()
					.WithColumn("CompletedOn").AsDateTime2().Nullable()
					.WithColumn("InspectorUserId").AsString(128).Nullable()
					.WithColumn("ItemsJson").AsString(int.MaxValue).Nullable()
					.WithColumn("Notes").AsString(int.MaxValue).Nullable()
					.WithColumn("SignatureName").AsString(int.MaxValue).Nullable()
					.WithColumn("SignedOn").AsDateTime2().Nullable()
					.WithColumn("ParentInspectionId").AsString(36).Nullable()
					.WithColumn("NoticeIssuedOn").AsDateTime2().Nullable()
					.WithColumn("NoticeReference").AsString(100).Nullable()
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedCatalogVersion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("CreatedByUserId").AsString(128).Nullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(1L)
					.WithColumn("DeletedOn").AsDateTime2().Nullable();

				Create.Index("IX_RmsInspections_Department_State").OnTable("RmsInspections").OnColumn("DepartmentId").Ascending().OnColumn("State").Ascending().OnColumn("ScheduledOn").Ascending();
				Create.Index("IX_RmsInspections_Occupancy").OnTable("RmsInspections").OnColumn("DepartmentId").Ascending().OnColumn("RmsOccupancyId").Ascending().OnColumn("CompletedOn").Ascending();
				Create.Index("IX_RmsInspections_Program").OnTable("RmsInspections").OnColumn("DepartmentId").Ascending().OnColumn("RmsInspectionProgramId").Ascending().OnColumn("State").Ascending();
			}
			if (!Schema.Table("RmsViolations").Exists())
			{
				Create.Table("RmsViolations")
					.WithColumn("RmsViolationId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ProtectionId").AsString(36).Nullable()
					.WithColumn("RmsInspectionId").AsString(36).NotNullable()
					.WithColumn("RmsOccupancyId").AsString(36).NotNullable()
					.WithColumn("RmsCodeSetId").AsString(36).Nullable()
					.WithColumn("RmsCodeSectionId").AsString(36).Nullable()
					.WithColumn("ChecklistItemKey").AsString(100).Nullable()
					.WithColumn("Description").AsString(int.MaxValue).Nullable()
					.WithColumn("Severity").AsInt32().NotNullable().WithDefaultValue(2)
					.WithColumn("CorrectiveAction").AsString(int.MaxValue).Nullable()
					.WithColumn("DueOn").AsDateTime2().Nullable()
					.WithColumn("State").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("CorrectedOn").AsDateTime2().Nullable()
					.WithColumn("VerifiedOn").AsDateTime2().Nullable()
					.WithColumn("VerifiedByUserId").AsString(128).Nullable()
					.WithColumn("ReinspectionId").AsString(36).Nullable()
					.WithColumn("OverdueEmittedOn").AsDateTime2().Nullable()
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedCatalogVersion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("CreatedByUserId").AsString(128).Nullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(1L)
					.WithColumn("DeletedOn").AsDateTime2().Nullable();

				Create.Index("IX_RmsViolations_Department_State_Due").OnTable("RmsViolations").OnColumn("DepartmentId").Ascending().OnColumn("State").Ascending().OnColumn("DueOn").Ascending();
				Create.Index("IX_RmsViolations_Inspection").OnTable("RmsViolations").OnColumn("DepartmentId").Ascending().OnColumn("RmsInspectionId").Ascending();
				Create.Index("IX_RmsViolations_Occupancy").OnTable("RmsViolations").OnColumn("DepartmentId").Ascending().OnColumn("RmsOccupancyId").Ascending().OnColumn("State").Ascending();
			}
			if (!Schema.Table("RmsHydrants").Exists())
			{
				Create.Table("RmsHydrants")
					.WithColumn("RmsHydrantId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ProtectionId").AsString(36).Nullable()
					.WithColumn("HydrantNumber").AsString(64).NotNullable()
					.WithColumn("Type").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("Latitude").AsDecimal(12, 8).NotNullable()
					.WithColumn("Longitude").AsDecimal(12, 8).NotNullable()
					.WithColumn("AddressText").AsString(500).Nullable()
					.WithColumn("OwnerKind").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("OwnerName").AsString(200).Nullable()
					.WithColumn("MainSizeInches").AsDecimal(6,2).Nullable()
					.WithColumn("StaticPressurePsi").AsInt32().Nullable()
					.WithColumn("ResidualPressurePsi").AsInt32().Nullable()
					.WithColumn("FlowGpm").AsInt32().Nullable()
					.WithColumn("FlowClass").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("InService").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("OutOfServiceReason").AsString(500).Nullable()
					.WithColumn("OutOfServiceSince").AsDateTime2().Nullable()
					.WithColumn("LastTestedOn").AsDateTime2().Nullable()
					.WithColumn("LastMaintainedOn").AsDateTime2().Nullable()
					.WithColumn("Notes").AsString(int.MaxValue).Nullable()
					.WithColumn("PoiId").AsInt32().Nullable()
					.WithColumn("Source").AsString(32).Nullable()
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("CreatedByUserId").AsString(128).Nullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(1L)
					.WithColumn("DeletedOn").AsDateTime2().Nullable();

				Create.Index("IX_RmsHydrants_Department_Service").OnTable("RmsHydrants").OnColumn("DepartmentId").Ascending().OnColumn("InService").Ascending().OnColumn("DeletedOn").Ascending();
				Create.Index("IX_RmsHydrants_Department_Position").OnTable("RmsHydrants").OnColumn("DepartmentId").Ascending().OnColumn("Latitude").Ascending().OnColumn("Longitude").Ascending();
			}
			if (!Schema.Table("RmsHydrantFlowTests").Exists())
			{
				Create.Table("RmsHydrantFlowTests")
					.WithColumn("RmsHydrantFlowTestId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ProtectionId").AsString(36).Nullable()
					.WithColumn("RmsHydrantId").AsString(36).NotNullable()
					.WithColumn("TestedOn").AsDateTime2().NotNullable()
					.WithColumn("TestedByUserId").AsString(128).Nullable()
					.WithColumn("StaticPressurePsi").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ResidualPressurePsi").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("PitotPressurePsi").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("OutletDiameterInches").AsDecimal(6,2).NotNullable().WithDefaultValue(0)
					.WithColumn("Coefficient").AsDecimal(6,2).NotNullable().WithDefaultValue(0)
					.WithColumn("FlowGpm").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("FlowClass").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Notes").AsString(int.MaxValue).Nullable()
					.WithColumn("CreatedOn").AsDateTime2().NotNullable();

				Create.Index("IX_RmsHydrantFlowTests_Hydrant").OnTable("RmsHydrantFlowTests").OnColumn("DepartmentId").Ascending().OnColumn("RmsHydrantId").Ascending().OnColumn("TestedOn").Descending();
			}
			if (!Schema.Table("RmsHydrantMaintenances").Exists())
			{
				Create.Table("RmsHydrantMaintenances")
					.WithColumn("RmsHydrantMaintenanceId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ProtectionId").AsString(36).Nullable()
					.WithColumn("RmsHydrantId").AsString(36).NotNullable()
					.WithColumn("PerformedOn").AsDateTime2().NotNullable()
					.WithColumn("PerformedByUserId").AsString(128).Nullable()
					.WithColumn("Kind").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("Notes").AsString(int.MaxValue).Nullable()
					.WithColumn("ReturnedToService").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("CreatedOn").AsDateTime2().NotNullable();

				Create.Index("IX_RmsHydrantMaintenances_Hydrant").OnTable("RmsHydrantMaintenances").OnColumn("DepartmentId").Ascending().OnColumn("RmsHydrantId").Ascending().OnColumn("PerformedOn").Descending();
			}
			if (!Schema.Table("RmsPermitTypes").Exists())
			{
				Create.Table("RmsPermitTypes")
					.WithColumn("RmsPermitTypeId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ProtectionId").AsString(36).Nullable()
					.WithColumn("Name").AsString(200).NotNullable()
					.WithColumn("Code").AsString(32).Nullable()
					.WithColumn("Description").AsString(int.MaxValue).Nullable()
					.WithColumn("DefaultValidityDays").AsInt32().NotNullable().WithDefaultValue(365)
					.WithColumn("RequiresPlanReview").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("FeeAmount").AsDecimal(18, 2).Nullable()
					.WithColumn("ConditionsTemplate").AsString(int.MaxValue).Nullable()
					.WithColumn("IsActive").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("CreatedByUserId").AsString(128).Nullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(1L)
					.WithColumn("DeletedOn").AsDateTime2().Nullable();

				Create.Index("IX_RmsPermitTypes_Department").OnTable("RmsPermitTypes").OnColumn("DepartmentId").Ascending().OnColumn("IsActive").Ascending();
			}
			if (!Schema.Table("RmsPermits").Exists())
			{
				Create.Table("RmsPermits")
					.WithColumn("RmsPermitId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ProtectionId").AsString(36).Nullable()
					.WithColumn("RmsPermitTypeId").AsString(36).NotNullable()
					.WithColumn("RmsOccupancyId").AsString(36).Nullable()
					.WithColumn("PermitNumber").AsString(32).Nullable()
					.WithColumn("ApplicantContactId").AsString(128).Nullable()
					.WithColumn("ApplicantName").AsString(int.MaxValue).Nullable()
					.WithColumn("ApplicantPhone").AsString(int.MaxValue).Nullable()
					.WithColumn("ApplicantEmail").AsString(int.MaxValue).Nullable()
					.WithColumn("Description").AsString(int.MaxValue).Nullable()
					.WithColumn("State").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AppliedOn").AsDateTime2().NotNullable()
					.WithColumn("ReviewedOn").AsDateTime2().Nullable()
					.WithColumn("ReviewedByUserId").AsString(128).Nullable()
					.WithColumn("IssuedOn").AsDateTime2().Nullable()
					.WithColumn("IssuedByUserId").AsString(128).Nullable()
					.WithColumn("EffectiveOn").AsDateTime2().Nullable()
					.WithColumn("ExpiresOn").AsDateTime2().Nullable()
					.WithColumn("Conditions").AsString(int.MaxValue).Nullable()
					.WithColumn("ReviewNotes").AsString(int.MaxValue).Nullable()
					.WithColumn("FeeAmount").AsDecimal(18, 2).Nullable()
					.WithColumn("FeePaidOn").AsDateTime2().Nullable()
					.WithColumn("InvoiceReference").AsString(100).Nullable()
					.WithColumn("DecisionReason").AsString(1000).Nullable()
					.WithColumn("ExpiringEmittedOn").AsDateTime2().Nullable()
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedCatalogVersion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("CreatedByUserId").AsString(128).Nullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(1L)
					.WithColumn("DeletedOn").AsDateTime2().Nullable();

				Create.Index("IX_RmsPermits_Department_State_Expires").OnTable("RmsPermits").OnColumn("DepartmentId").Ascending().OnColumn("State").Ascending().OnColumn("ExpiresOn").Ascending();
				Create.Index("IX_RmsPermits_Occupancy").OnTable("RmsPermits").OnColumn("DepartmentId").Ascending().OnColumn("RmsOccupancyId").Ascending();
			}
			if (!Schema.Table("RmsPlanReviews").Exists())
			{
				Create.Table("RmsPlanReviews")
					.WithColumn("RmsPlanReviewId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ProtectionId").AsString(36).Nullable()
					.WithColumn("RmsPermitId").AsString(36).NotNullable()
					.WithColumn("CycleNumber").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("SubmittedOn").AsDateTime2().NotNullable()
					.WithColumn("ReviewerUserId").AsString(128).Nullable()
					.WithColumn("ReviewedOn").AsDateTime2().Nullable()
					.WithColumn("Outcome").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Comments").AsString(int.MaxValue).Nullable()
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedCatalogVersion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(1L);

				Create.Index("IX_RmsPlanReviews_Permit").OnTable("RmsPlanReviews").OnColumn("DepartmentId").Ascending().OnColumn("RmsPermitId").Ascending().OnColumn("CycleNumber").Ascending();
			}
			if (!Schema.Table("RmsCrrActivities").Exists())
			{
				Create.Table("RmsCrrActivities")
					.WithColumn("RmsCrrActivityId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ProtectionId").AsString(36).Nullable()
					.WithColumn("Kind").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("OccurredOn").AsDateTime2().NotNullable()
					.WithColumn("Title").AsString(250).NotNullable()
					.WithColumn("Description").AsString(int.MaxValue).Nullable()
					.WithColumn("RmsOccupancyId").AsString(36).Nullable()
					.WithColumn("LocationText").AsString(500).Nullable()
					.WithColumn("Latitude").AsDecimal(12, 8).Nullable()
					.WithColumn("Longitude").AsDecimal(12, 8).Nullable()
					.WithColumn("AudienceCount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("SmokeAlarmsInstalled").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("HoursSpent").AsDecimal(8,2).NotNullable().WithDefaultValue(0)
					.WithColumn("StaffUserIdsCsv").AsString(int.MaxValue).Nullable()
					.WithColumn("Outcome").AsString(int.MaxValue).Nullable()
					.WithColumn("NerisSecondaryJson").AsString(int.MaxValue).Nullable()
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("CreatedByUserId").AsString(128).Nullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(1L)
					.WithColumn("DeletedOn").AsDateTime2().Nullable();

				Create.Index("IX_RmsCrrActivities_Department_Occurred").OnTable("RmsCrrActivities").OnColumn("DepartmentId").Ascending().OnColumn("OccurredOn").Ascending();
			}
			if (!Schema.Table("RmsPreventionAttachments").Exists())
			{
				Create.Table("RmsPreventionAttachments")
					.WithColumn("RmsPreventionAttachmentId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ProtectionId").AsString(36).Nullable()
					.WithColumn("ParentKind").AsInt32().NotNullable()
					.WithColumn("ParentId").AsString(36).NotNullable()
					.WithColumn("FileName").AsString(int.MaxValue).Nullable()
					.WithColumn("ContentType").AsString(200).Nullable()
					.WithColumn("ByteSize").AsInt64().NotNullable().WithDefaultValue(0L)
					.WithColumn("Checksum").AsString(128).Nullable()
					.WithColumn("Data").AsBinary(int.MaxValue).Nullable()
					.WithColumn("Description").AsString(int.MaxValue).Nullable()
					.WithColumn("UploadedByUserId").AsString(128).Nullable()
					.WithColumn("UploadedOn").AsDateTime2().NotNullable()
					.WithColumn("ScanState").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("MetadataStripped").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("Classification").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedCatalogVersion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(1L)
					.WithColumn("DeletedOn").AsDateTime2().Nullable();

				Create.Index("IX_RmsPreventionAttachments_Parent").OnTable("RmsPreventionAttachments").OnColumn("DepartmentId").Ascending().OnColumn("ParentKind").Ascending().OnColumn("ParentId").Ascending();
			}
			if (!Schema.Table("RmsPreventionSequences").Exists())
			{
				Create.Table("RmsPreventionSequences")
					.WithColumn("RmsPreventionSequenceId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("Kind").AsString(16).NotNullable()
					.WithColumn("Year").AsInt32().NotNullable()
					.WithColumn("LastValue").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable();

				Create.Index("UX_RmsPreventionSequences_Key").OnTable("RmsPreventionSequences").OnColumn("DepartmentId").Ascending().OnColumn("Kind").Ascending().OnColumn("Year").Ascending().WithOptions().Unique();
			}
			if (!Schema.Table("RmsInvestigationCases").Exists())
			{
				Create.Table("RmsInvestigationCases")
					.WithColumn("RmsInvestigationCaseId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ProtectionId").AsString(36).Nullable()
					.WithColumn("CaseNumber").AsString(32).Nullable()
					.WithColumn("Title").AsString(250).NotNullable()
					.WithColumn("State").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("OpenedOn").AsDateTime2().NotNullable()
					.WithColumn("OpenedByUserId").AsString(128).Nullable()
					.WithColumn("LeadInvestigatorUserId").AsString(128).Nullable()
					.WithColumn("RmsOccupancyId").AsString(36).Nullable()
					.WithColumn("CallId").AsInt32().Nullable()
					.WithColumn("IncidentSummary").AsString(int.MaxValue).Nullable()
					.WithColumn("CauseClassification").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("CauseDetail").AsString(int.MaxValue).Nullable()
					.WithColumn("OriginDescription").AsString(int.MaxValue).Nullable()
					.WithColumn("Findings").AsString(int.MaxValue).Nullable()
					.WithColumn("FindingsAuthorUserId").AsString(128).Nullable()
					.WithColumn("FindingsRecordedOn").AsDateTime2().Nullable()
					.WithColumn("FindingsApprovedOn").AsDateTime2().Nullable()
					.WithColumn("FindingsApprovedByUserId").AsString(128).Nullable()
					.WithColumn("RecommendsIncidentAmendment").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ClosedOn").AsDateTime2().Nullable()
					.WithColumn("ClosedByUserId").AsString(128).Nullable()
					.WithColumn("ClosureReason").AsString(int.MaxValue).Nullable()
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedCatalogVersion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(1L)
					.WithColumn("DeletedOn").AsDateTime2().Nullable();

				Create.Index("IX_RmsInvestigationCases_Department_State").OnTable("RmsInvestigationCases").OnColumn("DepartmentId").Ascending().OnColumn("State").Ascending().OnColumn("OpenedOn").Ascending();
			}
			if (!Schema.Table("RmsInvestigationCaseIncidents").Exists())
			{
				Create.Table("RmsInvestigationCaseIncidents")
					.WithColumn("RmsInvestigationCaseIncidentId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ProtectionId").AsString(36).Nullable()
					.WithColumn("RmsInvestigationCaseId").AsString(36).NotNullable()
					.WithColumn("RecordId").AsString(36).NotNullable()
					.WithColumn("PinnedRevisionId").AsString(36).Nullable()
					.WithColumn("RecordNumber").AsString(64).Nullable()
					.WithColumn("LinkedOn").AsDateTime2().NotNullable()
					.WithColumn("LinkedByUserId").AsString(128).Nullable();

				Create.Index("IX_RmsInvestigationCaseIncidents_Case").OnTable("RmsInvestigationCaseIncidents").OnColumn("DepartmentId").Ascending().OnColumn("RmsInvestigationCaseId").Ascending();
				Create.Index("IX_RmsInvestigationCaseIncidents_Record").OnTable("RmsInvestigationCaseIncidents").OnColumn("DepartmentId").Ascending().OnColumn("RecordId").Ascending();
			}
			if (!Schema.Table("RmsInvestigationCaseMembers").Exists())
			{
				Create.Table("RmsInvestigationCaseMembers")
					.WithColumn("RmsInvestigationCaseMemberId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ProtectionId").AsString(36).Nullable()
					.WithColumn("RmsInvestigationCaseId").AsString(36).NotNullable()
					.WithColumn("UserId").AsString(128).NotNullable()
					.WithColumn("Role").AsInt32().NotNullable().WithDefaultValue(2)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("RemovedOn").AsDateTime2().Nullable()
					.WithColumn("RemovedByUserId").AsString(128).Nullable();

				Create.Index("IX_RmsInvestigationCaseMembers_Case").OnTable("RmsInvestigationCaseMembers").OnColumn("DepartmentId").Ascending().OnColumn("RmsInvestigationCaseId").Ascending();
				Create.Index("IX_RmsInvestigationCaseMembers_User").OnTable("RmsInvestigationCaseMembers").OnColumn("DepartmentId").Ascending().OnColumn("UserId").Ascending().OnColumn("RemovedOn").Ascending();
			}
			if (!Schema.Table("RmsInvestigationNotes").Exists())
			{
				Create.Table("RmsInvestigationNotes")
					.WithColumn("RmsInvestigationNoteId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ProtectionId").AsString(36).Nullable()
					.WithColumn("RmsInvestigationCaseId").AsString(36).NotNullable()
					.WithColumn("Kind").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("OccurredOn").AsDateTime2().NotNullable()
					.WithColumn("AuthorUserId").AsString(128).Nullable()
					.WithColumn("Subject").AsString(int.MaxValue).Nullable()
					.WithColumn("Body").AsString(int.MaxValue).Nullable()
					.WithColumn("IsLocked").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedCatalogVersion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(1L)
					.WithColumn("DeletedOn").AsDateTime2().Nullable();

				Create.Index("IX_RmsInvestigationNotes_Case").OnTable("RmsInvestigationNotes").OnColumn("DepartmentId").Ascending().OnColumn("RmsInvestigationCaseId").Ascending().OnColumn("OccurredOn").Ascending();
			}
			if (!Schema.Table("RmsInvestigationEvidence").Exists())
			{
				Create.Table("RmsInvestigationEvidence")
					.WithColumn("RmsInvestigationEvidenceId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ProtectionId").AsString(36).Nullable()
					.WithColumn("RmsInvestigationCaseId").AsString(36).NotNullable()
					.WithColumn("EvidenceNumber").AsString(32).Nullable()
					.WithColumn("Kind").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("Description").AsString(int.MaxValue).Nullable()
					.WithColumn("CollectedOn").AsDateTime2().NotNullable()
					.WithColumn("CollectedByUserId").AsString(128).Nullable()
					.WithColumn("CollectedFrom").AsString(int.MaxValue).Nullable()
					.WithColumn("State").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("CurrentCustodianUserId").AsString(128).Nullable()
					.WithColumn("CurrentCustodianExternal").AsString(int.MaxValue).Nullable()
					.WithColumn("StorageLocation").AsString(250).Nullable()
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedCatalogVersion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(1L)
					.WithColumn("DeletedOn").AsDateTime2().Nullable();

				Create.Index("IX_RmsInvestigationEvidence_Case").OnTable("RmsInvestigationEvidence").OnColumn("DepartmentId").Ascending().OnColumn("RmsInvestigationCaseId").Ascending();
			}
			if (!Schema.Table("RmsInvestigationCustody").Exists())
			{
				Create.Table("RmsInvestigationCustody")
					.WithColumn("RmsInvestigationCustodyId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ProtectionId").AsString(36).Nullable()
					.WithColumn("RmsInvestigationEvidenceId").AsString(36).NotNullable()
					.WithColumn("Sequence").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("TransferredOn").AsDateTime2().NotNullable()
					.WithColumn("FromUserId").AsString(128).Nullable()
					.WithColumn("FromExternal").AsString(int.MaxValue).Nullable()
					.WithColumn("ToUserId").AsString(128).Nullable()
					.WithColumn("ToExternal").AsString(int.MaxValue).Nullable()
					.WithColumn("Reason").AsString(int.MaxValue).Nullable()
					.WithColumn("ResultingState").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("RecordedByUserId").AsString(128).Nullable()
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedCatalogVersion").AsInt32().NotNullable().WithDefaultValue(0);

				Create.Index("IX_RmsInvestigationCustody_Evidence").OnTable("RmsInvestigationCustody").OnColumn("DepartmentId").Ascending().OnColumn("RmsInvestigationEvidenceId").Ascending().OnColumn("Sequence").Ascending();
			}
			if (!Schema.Table("RmsInvestigationReferrals").Exists())
			{
				Create.Table("RmsInvestigationReferrals")
					.WithColumn("RmsInvestigationReferralId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ProtectionId").AsString(36).Nullable()
					.WithColumn("RmsInvestigationCaseId").AsString(36).NotNullable()
					.WithColumn("Agency").AsString(250).NotNullable()
					.WithColumn("ReferredOn").AsDateTime2().NotNullable()
					.WithColumn("ReferredByUserId").AsString(128).Nullable()
					.WithColumn("Reason").AsString(int.MaxValue).Nullable()
					.WithColumn("ReferenceNumber").AsString(100).Nullable()
					.WithColumn("State").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedCatalogVersion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(1L);

				Create.Index("IX_RmsInvestigationReferrals_Case").OnTable("RmsInvestigationReferrals").OnColumn("DepartmentId").Ascending().OnColumn("RmsInvestigationCaseId").Ascending();
			}

			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM [FeatureFlags] WHERE [FlagKey] = 'Records.Prevention.Occupancy') INSERT INTO [FeatureFlags] ([FlagKey], [Name], [Description], [Category], [IsEnabledGlobally]) VALUES ('Records.Prevention.Occupancy', 'Records Prevention - Occupancies', 'RMS-5 occupancy/property master and the Contacts pre-plan crosswalk. Requires Records.System. Seeded off.', 'Records', 0);");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM [FeatureFlags] WHERE [FlagKey] = 'Records.Prevention.Inspections') INSERT INTO [FeatureFlags] ([FlagKey], [Name], [Description], [Category], [IsEnabledGlobally]) VALUES ('Records.Prevention.Inspections', 'Records Prevention - Inspections', 'RMS-5 inspection programs, code sets, inspections and violations. Requires Records.Prevention.Occupancy. Seeded off.', 'Records', 0);");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM [FeatureFlags] WHERE [FlagKey] = 'Records.Prevention.Hydrants') INSERT INTO [FeatureFlags] ([FlagKey], [Name], [Description], [Category], [IsEnabledGlobally]) VALUES ('Records.Prevention.Hydrants', 'Records Prevention - Hydrants', 'RMS-5 hydrants and water sources with flow tests and the response-map layer. Requires Records.System. Seeded off.', 'Records', 0);");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM [FeatureFlags] WHERE [FlagKey] = 'Records.Prevention.Permits') INSERT INTO [FeatureFlags] ([FlagKey], [Name], [Description], [Category], [IsEnabledGlobally]) VALUES ('Records.Prevention.Permits', 'Records Prevention - Permits', 'RMS-5 permits, plan review and expiration. Requires Records.Prevention.Occupancy. Seeded off.', 'Records', 0);");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM [FeatureFlags] WHERE [FlagKey] = 'Records.Prevention.Crr') INSERT INTO [FeatureFlags] ([FlagKey], [Name], [Description], [Category], [IsEnabledGlobally]) VALUES ('Records.Prevention.Crr', 'Records Prevention - Community Risk Reduction', 'RMS-5 community risk reduction activity tracking. Requires Records.System. Seeded off.', 'Records', 0);");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM [FeatureFlags] WHERE [FlagKey] = 'Records.Investigations') INSERT INTO [FeatureFlags] ([FlagKey], [Name], [Description], [Category], [IsEnabledGlobally]) VALUES ('Records.Investigations', 'Records Investigations', 'RMS-5 investigation cases with case-level authorization, evidence chain of custody and referrals. Requires Records.System. Seeded off.', 'Records', 0);");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM [FeatureFlags] WHERE [FlagKey] = 'Records.QualityReview') INSERT INTO [FeatureFlags] ([FlagKey], [Name], [Description], [Category], [IsEnabledGlobally]) VALUES ('Records.QualityReview', 'Records Quality Review', 'RMS-4 optional post-finalization quality review: rubric-scored sampling of finalized records. Requires Records.System. Seeded off.', 'Records', 0);");
		}

		public override void Down()
		{
			Delete.FromTable("FeatureFlags").Row(new { FlagKey = "Records.Prevention.Occupancy" });
			Delete.FromTable("FeatureFlags").Row(new { FlagKey = "Records.Prevention.Inspections" });
			Delete.FromTable("FeatureFlags").Row(new { FlagKey = "Records.Prevention.Hydrants" });
			Delete.FromTable("FeatureFlags").Row(new { FlagKey = "Records.Prevention.Permits" });
			Delete.FromTable("FeatureFlags").Row(new { FlagKey = "Records.Prevention.Crr" });
			Delete.FromTable("FeatureFlags").Row(new { FlagKey = "Records.Investigations" });
			Delete.FromTable("FeatureFlags").Row(new { FlagKey = "Records.QualityReview" });
			if (Schema.Table("RmsInvestigationReferrals").Exists())
				Delete.Table("RmsInvestigationReferrals");
			if (Schema.Table("RmsInvestigationCustody").Exists())
				Delete.Table("RmsInvestigationCustody");
			if (Schema.Table("RmsInvestigationEvidence").Exists())
				Delete.Table("RmsInvestigationEvidence");
			if (Schema.Table("RmsInvestigationNotes").Exists())
				Delete.Table("RmsInvestigationNotes");
			if (Schema.Table("RmsInvestigationCaseMembers").Exists())
				Delete.Table("RmsInvestigationCaseMembers");
			if (Schema.Table("RmsInvestigationCaseIncidents").Exists())
				Delete.Table("RmsInvestigationCaseIncidents");
			if (Schema.Table("RmsInvestigationCases").Exists())
				Delete.Table("RmsInvestigationCases");
			if (Schema.Table("RmsPreventionSequences").Exists())
				Delete.Table("RmsPreventionSequences");
			if (Schema.Table("RmsPreventionAttachments").Exists())
				Delete.Table("RmsPreventionAttachments");
			if (Schema.Table("RmsCrrActivities").Exists())
				Delete.Table("RmsCrrActivities");
			if (Schema.Table("RmsPlanReviews").Exists())
				Delete.Table("RmsPlanReviews");
			if (Schema.Table("RmsPermits").Exists())
				Delete.Table("RmsPermits");
			if (Schema.Table("RmsPermitTypes").Exists())
				Delete.Table("RmsPermitTypes");
			if (Schema.Table("RmsHydrantMaintenances").Exists())
				Delete.Table("RmsHydrantMaintenances");
			if (Schema.Table("RmsHydrantFlowTests").Exists())
				Delete.Table("RmsHydrantFlowTests");
			if (Schema.Table("RmsHydrants").Exists())
				Delete.Table("RmsHydrants");
			if (Schema.Table("RmsViolations").Exists())
				Delete.Table("RmsViolations");
			if (Schema.Table("RmsInspections").Exists())
				Delete.Table("RmsInspections");
			if (Schema.Table("RmsInspectionPrograms").Exists())
				Delete.Table("RmsInspectionPrograms");
			if (Schema.Table("RmsCodeSections").Exists())
				Delete.Table("RmsCodeSections");
			if (Schema.Table("RmsCodeSets").Exists())
				Delete.Table("RmsCodeSets");
			if (Schema.Table("RmsOccupancyOwnerships").Exists())
				Delete.Table("RmsOccupancyOwnerships");
			if (Schema.Table("RmsOccupancyFieldProvenances").Exists())
				Delete.Table("RmsOccupancyFieldProvenances");
			if (Schema.Table("RmsOccupancyCrosswalks").Exists())
				Delete.Table("RmsOccupancyCrosswalks");
			if (Schema.Table("RmsOccupancyHazards").Exists())
				Delete.Table("RmsOccupancyHazards");
			if (Schema.Table("RmsOccupancyContactLinks").Exists())
				Delete.Table("RmsOccupancyContactLinks");
			if (Schema.Table("RmsOccupancies").Exists())
				Delete.Table("RmsOccupancies");
		}
	}
}
