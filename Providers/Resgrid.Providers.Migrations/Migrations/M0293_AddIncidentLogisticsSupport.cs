using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Incident Back Office LI-B Logistics, support (plan §9.6–§9.11, §9.15, §11.5): facilities with capacity, agreements and
	/// services; lodging properties, blocks, units, beds, the per-night inventory that enforces one occupant per bed-night,
	/// assignments and rooming lists; meal and potable-water planning; support-service templates, tickets and assets;
	/// transport, travel and fuel; the communications assignment ledger (ADP catalog 35). No foreign keys (the IC precedent):
	/// tenant ownership is enforced in the service layer and the department purge (IncidentBackOfficeDepartmentCleanup) removes
	/// every row. Existence-guarded for safe retry.
	/// </summary>
	[Migration(293)]
	public class M0293_AddIncidentLogisticsSupport : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("IncidentFacilities").Exists())
			{
				Create.Table("IncidentFacilities")
					.WithColumn("IncidentFacilityId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("Name").AsString(200).NotNullable()
					.WithColumn("FacilityType").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("PoiId").AsInt32().Nullable()
					.WithColumn("MapLayerId").AsString(64).Nullable()
					.WithColumn("IndoorMapId").AsString(64).Nullable()
					.WithColumn("Address").AsString(500).Nullable()
					.WithColumn("OperatingHours").AsString(200).Nullable()
					.WithColumn("OwnerOperator").AsString(200).Nullable()
					.WithColumn("ContactId").AsString(128).Nullable()
					.WithColumn("ContactPhone").AsString(64).Nullable()
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("SafetyNotes").AsString(int.MaxValue).Nullable()
					.WithColumn("AccessNotes").AsString(int.MaxValue).Nullable()
					.WithColumn("OpenedOn").AsDateTime2().Nullable()
					.WithColumn("ClosedOn").AsDateTime2().Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentFacilities_Activation' AND object_id = OBJECT_ID('IncidentFacilities')) CREATE NONCLUSTERED INDEX [IX_IncidentFacilities_Activation] ON [IncidentFacilities] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentFacilities_Type' AND object_id = OBJECT_ID('IncidentFacilities')) CREATE NONCLUSTERED INDEX [IX_IncidentFacilities_Type] ON [IncidentFacilities] ([DepartmentId], [IncidentActivationId], [FacilityType]);");
			}

			if (!Schema.Table("IncidentFacilityCapacities").Exists())
			{
				Create.Table("IncidentFacilityCapacities")
					.WithColumn("IncidentFacilityCapacityId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("IncidentFacilityId").AsString(36).NotNullable()
					.WithColumn("ServiceKind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("MaxCapacity").AsDecimal(18, 4).NotNullable().WithDefaultValue(0)
					.WithColumn("UsableCapacity").AsDecimal(18, 4).NotNullable().WithDefaultValue(0)
					.WithColumn("CurrentUse").AsDecimal(18, 4).NotNullable().WithDefaultValue(0)
					.WithColumn("Reserved").AsDecimal(18, 4).NotNullable().WithDefaultValue(0)
					.WithColumn("OutOfService").AsDecimal(18, 4).NotNullable().WithDefaultValue(0)
					.WithColumn("Unit").AsString(16).Nullable()
					.WithColumn("ThresholdPercent").AsDecimal(9, 4).Nullable()
					.WithColumn("Notes").AsString(500).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentFacilityCapacities_Activation' AND object_id = OBJECT_ID('IncidentFacilityCapacities')) CREATE NONCLUSTERED INDEX [IX_IncidentFacilityCapacities_Activation] ON [IncidentFacilityCapacities] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentFacilityCapacities_Facility' AND object_id = OBJECT_ID('IncidentFacilityCapacities')) CREATE NONCLUSTERED INDEX [IX_IncidentFacilityCapacities_Facility] ON [IncidentFacilityCapacities] ([DepartmentId], [IncidentFacilityId]);");
			}

			if (!Schema.Table("IncidentFacilityAgreements").Exists())
			{
				Create.Table("IncidentFacilityAgreements")
					.WithColumn("IncidentFacilityAgreementId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("IncidentFacilityId").AsString(36).NotNullable()
					.WithColumn("VendorAgreementId").AsString(36).Nullable()
					.WithColumn("AgreementKind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("RecordId").AsString(36).Nullable()
					.WithColumn("StartsOn").AsDate().Nullable()
					.WithColumn("EndsOn").AsDate().Nullable()
					.WithColumn("Terms").AsString(int.MaxValue).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentFacilityAgreements_Activation' AND object_id = OBJECT_ID('IncidentFacilityAgreements')) CREATE NONCLUSTERED INDEX [IX_IncidentFacilityAgreements_Activation] ON [IncidentFacilityAgreements] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentFacilityAgreements_Facility' AND object_id = OBJECT_ID('IncidentFacilityAgreements')) CREATE NONCLUSTERED INDEX [IX_IncidentFacilityAgreements_Facility] ON [IncidentFacilityAgreements] ([DepartmentId], [IncidentFacilityId]);");
			}

			if (!Schema.Table("IncidentFacilityServices").Exists())
			{
				Create.Table("IncidentFacilityServices")
					.WithColumn("IncidentFacilityServiceId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("IncidentFacilityId").AsString(36).NotNullable()
					.WithColumn("ServiceKind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Schedule").AsString(200).Nullable()
					.WithColumn("IncidentVendorEngagementId").AsString(36).Nullable()
					.WithColumn("Notes").AsString(500).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentFacilityServices_Activation' AND object_id = OBJECT_ID('IncidentFacilityServices')) CREATE NONCLUSTERED INDEX [IX_IncidentFacilityServices_Activation] ON [IncidentFacilityServices] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentFacilityServices_Facility' AND object_id = OBJECT_ID('IncidentFacilityServices')) CREATE NONCLUSTERED INDEX [IX_IncidentFacilityServices_Facility] ON [IncidentFacilityServices] ([DepartmentId], [IncidentFacilityId]);");
			}

			if (!Schema.Table("IncidentLodgingProperties").Exists())
			{
				Create.Table("IncidentLodgingProperties")
					.WithColumn("IncidentLodgingPropertyId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("IncidentFacilityId").AsString(36).Nullable()
					.WithColumn("VendorId").AsString(36).Nullable()
					.WithColumn("ContactId").AsString(128).Nullable()
					.WithColumn("Name").AsString(200).NotNullable()
					.WithColumn("Address").AsString(500).Nullable()
					.WithColumn("DistanceMinutes").AsInt32().Nullable()
					.WithColumn("CheckInHours").AsString(100).Nullable()
					.WithColumn("Amenities").AsString(1000).Nullable()
					.WithColumn("CancellationTerms").AsString(1000).Nullable()
					.WithColumn("DirectBillMethod").AsString(200).Nullable()
					.WithColumn("BillingContact").AsString(200).Nullable()
					.WithColumn("CutoffOn").AsDateTime2().Nullable()
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("IsCamp").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("Notes").AsString(int.MaxValue).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentLodgingProperties_Activation' AND object_id = OBJECT_ID('IncidentLodgingProperties')) CREATE NONCLUSTERED INDEX [IX_IncidentLodgingProperties_Activation] ON [IncidentLodgingProperties] ([DepartmentId], [IncidentActivationId]);");
			}

			if (!Schema.Table("IncidentLodgingBlocks").Exists())
			{
				Create.Table("IncidentLodgingBlocks")
					.WithColumn("IncidentLodgingBlockId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("IncidentLodgingPropertyId").AsString(36).NotNullable()
					.WithColumn("RoomType").AsString(100).Nullable()
					.WithColumn("FirstNight").AsDate().NotNullable()
					.WithColumn("LastNight").AsDate().NotNullable()
					.WithColumn("RoomsBlocked").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("RoomsConfirmed").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("NightlyRate").AsDecimal(18, 2).Nullable()
					.WithColumn("TaxPercent").AsDecimal(9, 4).Nullable()
					.WithColumn("Currency").AsString(3).NotNullable()
					.WithColumn("CutoffOn").AsDateTime2().Nullable()
					.WithColumn("AttritionTerms").AsString(1000).Nullable()
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("IncidentVendorEngagementId").AsString(36).Nullable()
					.WithColumn("VendorAgreementId").AsString(36).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentLodgingBlocks_Activation' AND object_id = OBJECT_ID('IncidentLodgingBlocks')) CREATE NONCLUSTERED INDEX [IX_IncidentLodgingBlocks_Activation] ON [IncidentLodgingBlocks] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentLodgingBlocks_Property' AND object_id = OBJECT_ID('IncidentLodgingBlocks')) CREATE NONCLUSTERED INDEX [IX_IncidentLodgingBlocks_Property] ON [IncidentLodgingBlocks] ([DepartmentId], [IncidentLodgingPropertyId]);");
			}

			if (!Schema.Table("IncidentLodgingUnits").Exists())
			{
				Create.Table("IncidentLodgingUnits")
					.WithColumn("IncidentLodgingUnitId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("IncidentLodgingPropertyId").AsString(36).NotNullable()
					.WithColumn("IncidentLodgingBlockId").AsString(36).Nullable()
					.WithColumn("UnitType").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("BedCount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("IsAccessible").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("IsOutOfService").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("UnitLabel").AsString(int.MaxValue).Nullable()
					.WithColumn("Notes").AsString(500).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedCatalogVersion").AsInt32().Nullable()
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentLodgingUnits_Activation' AND object_id = OBJECT_ID('IncidentLodgingUnits')) CREATE NONCLUSTERED INDEX [IX_IncidentLodgingUnits_Activation] ON [IncidentLodgingUnits] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentLodgingUnits_Property' AND object_id = OBJECT_ID('IncidentLodgingUnits')) CREATE NONCLUSTERED INDEX [IX_IncidentLodgingUnits_Property] ON [IncidentLodgingUnits] ([DepartmentId], [IncidentLodgingPropertyId]);");
			}

			if (!Schema.Table("IncidentLodgingBeds").Exists())
			{
				Create.Table("IncidentLodgingBeds")
					.WithColumn("IncidentLodgingBedId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("IncidentLodgingUnitId").AsString(36).NotNullable()
					.WithColumn("IncidentLodgingPropertyId").AsString(36).NotNullable()
					.WithColumn("BedLabel").AsString(32).Nullable()
					.WithColumn("IsAccessible").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("IsOutOfService").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentLodgingBeds_Activation' AND object_id = OBJECT_ID('IncidentLodgingBeds')) CREATE NONCLUSTERED INDEX [IX_IncidentLodgingBeds_Activation] ON [IncidentLodgingBeds] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentLodgingBeds_Unit' AND object_id = OBJECT_ID('IncidentLodgingBeds')) CREATE NONCLUSTERED INDEX [IX_IncidentLodgingBeds_Unit] ON [IncidentLodgingBeds] ([DepartmentId], [IncidentLodgingUnitId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentLodgingBeds_Property' AND object_id = OBJECT_ID('IncidentLodgingBeds')) CREATE NONCLUSTERED INDEX [IX_IncidentLodgingBeds_Property] ON [IncidentLodgingBeds] ([DepartmentId], [IncidentLodgingPropertyId]);");
			}

			if (!Schema.Table("IncidentLodgingNightInventory").Exists())
			{
				Create.Table("IncidentLodgingNightInventory")
					.WithColumn("IncidentLodgingNightInventoryId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("IncidentLodgingBedId").AsString(36).NotNullable()
					.WithColumn("Night").AsDate().NotNullable()
					.WithColumn("IncidentLodgingAssignmentId").AsString(36).NotNullable()
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentLodgingNightInventory_Activation' AND object_id = OBJECT_ID('IncidentLodgingNightInventory')) CREATE NONCLUSTERED INDEX [IX_IncidentLodgingNightInventory_Activation] ON [IncidentLodgingNightInventory] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentLodgingNightInventory_Assignment' AND object_id = OBJECT_ID('IncidentLodgingNightInventory')) CREATE NONCLUSTERED INDEX [IX_IncidentLodgingNightInventory_Assignment] ON [IncidentLodgingNightInventory] ([DepartmentId], [IncidentLodgingAssignmentId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentLodgingNightInventory_Night' AND object_id = OBJECT_ID('IncidentLodgingNightInventory')) CREATE NONCLUSTERED INDEX [IX_IncidentLodgingNightInventory_Night] ON [IncidentLodgingNightInventory] ([DepartmentId], [IncidentActivationId], [Night]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_IncidentLodgingNightInventory_BedNight' AND object_id = OBJECT_ID('IncidentLodgingNightInventory')) CREATE UNIQUE NONCLUSTERED INDEX [UX_IncidentLodgingNightInventory_BedNight] ON [IncidentLodgingNightInventory] ([IncidentLodgingBedId], [Night]);");
			}

			if (!Schema.Table("IncidentLodgingAssignments").Exists())
			{
				Create.Table("IncidentLodgingAssignments")
					.WithColumn("IncidentLodgingAssignmentId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("IncidentLodgingBedId").AsString(36).NotNullable()
					.WithColumn("IncidentLodgingPropertyId").AsString(36).NotNullable()
					.WithColumn("SubjectType").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("IncidentParticipantId").AsString(36).Nullable()
					.WithColumn("IncidentResourceId").AsString(36).Nullable()
					.WithColumn("ReservationLabel").AsString(128).Nullable()
					.WithColumn("FirstNight").AsDate().NotNullable()
					.WithColumn("LastNight").AsDate().NotNullable()
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("IsAccessible").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RequiresSingleOccupancy").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("DayNightPattern").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("IsRestPeriod").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("IncidentVendorEngagementId").AsString(36).Nullable()
					.WithColumn("IncidentBudgetLineId").AsString(36).Nullable()
					.WithColumn("RateSnapshotId").AsString(36).Nullable()
					.WithColumn("Currency").AsString(3).NotNullable()
					.WithColumn("ConfirmationNumber").AsString(int.MaxValue).Nullable()
					.WithColumn("Notes").AsString(int.MaxValue).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedCatalogVersion").AsInt32().Nullable()
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentLodgingAssignments_Activation' AND object_id = OBJECT_ID('IncidentLodgingAssignments')) CREATE NONCLUSTERED INDEX [IX_IncidentLodgingAssignments_Activation] ON [IncidentLodgingAssignments] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentLodgingAssignments_Nights' AND object_id = OBJECT_ID('IncidentLodgingAssignments')) CREATE NONCLUSTERED INDEX [IX_IncidentLodgingAssignments_Nights] ON [IncidentLodgingAssignments] ([DepartmentId], [IncidentActivationId], [FirstNight]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentLodgingAssignments_Bed' AND object_id = OBJECT_ID('IncidentLodgingAssignments')) CREATE NONCLUSTERED INDEX [IX_IncidentLodgingAssignments_Bed] ON [IncidentLodgingAssignments] ([IncidentLodgingBedId], [FirstNight], [LastNight]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentLodgingAssignments_Participant' AND object_id = OBJECT_ID('IncidentLodgingAssignments')) CREATE NONCLUSTERED INDEX [IX_IncidentLodgingAssignments_Participant] ON [IncidentLodgingAssignments] ([IncidentParticipantId], [FirstNight]);");
			}

			if (!Schema.Table("IncidentRoomingListVersions").Exists())
			{
				Create.Table("IncidentRoomingListVersions")
					.WithColumn("IncidentRoomingListVersionId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("IncidentLodgingPropertyId").AsString(36).NotNullable()
					.WithColumn("VersionNumber").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("SentOn").AsDateTime2().Nullable()
					.WithColumn("Checksum").AsString(64).Nullable()
					.WithColumn("LineCount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ContentJson").AsString(int.MaxValue).Nullable()
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedCatalogVersion").AsInt32().Nullable()
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentRoomingListVersions_Activation' AND object_id = OBJECT_ID('IncidentRoomingListVersions')) CREATE NONCLUSTERED INDEX [IX_IncidentRoomingListVersions_Activation] ON [IncidentRoomingListVersions] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentRoomingListVersions_Property' AND object_id = OBJECT_ID('IncidentRoomingListVersions')) CREATE NONCLUSTERED INDEX [IX_IncidentRoomingListVersions_Property] ON [IncidentRoomingListVersions] ([DepartmentId], [IncidentLodgingPropertyId], [VersionNumber]);");
			}

			if (!Schema.Table("IncidentMealPlans").Exists())
			{
				Create.Table("IncidentMealPlans")
					.WithColumn("IncidentMealPlanId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("IncidentOperationalPeriodId").AsString(36).Nullable()
					.WithColumn("IncidentFacilityId").AsString(36).Nullable()
					.WithColumn("SiteName").AsString(200).NotNullable()
					.WithColumn("MealType").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ServiceDate").AsDate().NotNullable()
					.WithColumn("WindowStart").AsDateTime2().NotNullable()
					.WithColumn("WindowEnd").AsDateTime2().NotNullable()
					.WithColumn("CutoffOn").AsDateTime2().Nullable()
					.WithColumn("DeliveryMode").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("IncidentVendorEngagementId").AsString(36).Nullable()
					.WithColumn("IsOffsite").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("VoucherTerms").AsString(500).Nullable()
					.WithColumn("AuthorizedHeadcount").AsInt32().Nullable()
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Notes").AsString(int.MaxValue).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentMealPlans_Activation' AND object_id = OBJECT_ID('IncidentMealPlans')) CREATE NONCLUSTERED INDEX [IX_IncidentMealPlans_Activation] ON [IncidentMealPlans] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentMealPlans_Window' AND object_id = OBJECT_ID('IncidentMealPlans')) CREATE NONCLUSTERED INDEX [IX_IncidentMealPlans_Window] ON [IncidentMealPlans] ([DepartmentId], [IncidentActivationId], [WindowStart]);");
			}

			if (!Schema.Table("IncidentMealDemands").Exists())
			{
				Create.Table("IncidentMealDemands")
					.WithColumn("IncidentMealDemandId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("IncidentMealPlanId").AsString(36).NotNullable()
					.WithColumn("Version").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("MetricVersion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Population").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Inbound").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Departures").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Remote").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Guests").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Exclusions").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ContingencyPercent").AsDecimal(9, 4).NotNullable().WithDefaultValue(0)
					.WithColumn("UnregisteredBand").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("PredictedTimeoutBand").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ForecastCount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ManualOverride").AsInt32().Nullable()
					.WithColumn("OverrideReason").AsString(500).Nullable()
					.WithColumn("DietaryJson").AsString(int.MaxValue).Nullable()
					.WithColumn("InputsJson").AsString(int.MaxValue).Nullable()
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentMealDemands_Activation' AND object_id = OBJECT_ID('IncidentMealDemands')) CREATE NONCLUSTERED INDEX [IX_IncidentMealDemands_Activation] ON [IncidentMealDemands] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentMealDemands_Plan' AND object_id = OBJECT_ID('IncidentMealDemands')) CREATE NONCLUSTERED INDEX [IX_IncidentMealDemands_Plan] ON [IncidentMealDemands] ([DepartmentId], [IncidentMealPlanId], [Version]);");
			}

			if (!Schema.Table("IncidentMealOrders").Exists())
			{
				Create.Table("IncidentMealOrders")
					.WithColumn("IncidentMealOrderId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("IncidentMealPlanId").AsString(36).NotNullable()
					.WithColumn("IncidentVendorEngagementId").AsString(36).Nullable()
					.WithColumn("OrderedCount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("FrozenForecastVersion").AsInt32().Nullable()
					.WithColumn("UnitPrice").AsDecimal(18, 4).Nullable()
					.WithColumn("Currency").AsString(3).NotNullable()
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Notes").AsString(1000).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentMealOrders_Activation' AND object_id = OBJECT_ID('IncidentMealOrders')) CREATE NONCLUSTERED INDEX [IX_IncidentMealOrders_Activation] ON [IncidentMealOrders] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentMealOrders_Plan' AND object_id = OBJECT_ID('IncidentMealOrders')) CREATE NONCLUSTERED INDEX [IX_IncidentMealOrders_Plan] ON [IncidentMealOrders] ([DepartmentId], [IncidentMealPlanId]);");
			}

			if (!Schema.Table("IncidentMealActuals").Exists())
			{
				Create.Table("IncidentMealActuals")
					.WithColumn("IncidentMealActualId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("IncidentMealPlanId").AsString(36).NotNullable()
					.WithColumn("Delivered").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Served").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Returned").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Waste").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Shortage").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ReceivingTemperature").AsString(32).Nullable()
					.WithColumn("InspectionNotes").AsString(1000).Nullable()
					.WithColumn("Complaint").AsString(1000).Nullable()
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentMealActuals_Activation' AND object_id = OBJECT_ID('IncidentMealActuals')) CREATE NONCLUSTERED INDEX [IX_IncidentMealActuals_Activation] ON [IncidentMealActuals] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentMealActuals_Plan' AND object_id = OBJECT_ID('IncidentMealActuals')) CREATE NONCLUSTERED INDEX [IX_IncidentMealActuals_Plan] ON [IncidentMealActuals] ([DepartmentId], [IncidentMealPlanId]);");
			}

			if (!Schema.Table("IncidentWaterPlans").Exists())
			{
				Create.Table("IncidentWaterPlans")
					.WithColumn("IncidentWaterPlanId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("IncidentFacilityId").AsString(36).Nullable()
					.WithColumn("SiteName").AsString(200).NotNullable()
					.WithColumn("StorageCapacity").AsDecimal(18, 4).NotNullable().WithDefaultValue(0)
					.WithColumn("Unit").AsString(16).NotNullable()
					.WithColumn("ReorderThreshold").AsDecimal(18, 4).Nullable()
					.WithColumn("DaysOfSupplyThreshold").AsDecimal(18, 4).Nullable()
					.WithColumn("Source").AsString(200).Nullable()
					.WithColumn("CoefficientProfile").AsString(64).Nullable()
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentWaterPlans_Activation' AND object_id = OBJECT_ID('IncidentWaterPlans')) CREATE NONCLUSTERED INDEX [IX_IncidentWaterPlans_Activation] ON [IncidentWaterPlans] ([DepartmentId], [IncidentActivationId]);");
			}

			if (!Schema.Table("IncidentWaterStock").Exists())
			{
				Create.Table("IncidentWaterStock")
					.WithColumn("IncidentWaterStockId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("IncidentWaterPlanId").AsString(36).NotNullable()
					.WithColumn("Kind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Quantity").AsDecimal(18, 4).NotNullable().WithDefaultValue(0)
					.WithColumn("UnitOfMeasure").AsString(16).NotNullable()
					.WithColumn("CanonicalQuantity").AsDecimal(18, 4).Nullable()
					.WithColumn("CanonicalUnit").AsString(16).Nullable()
					.WithColumn("MeasuredOn").AsDateTime2().NotNullable()
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentWaterStock_Activation' AND object_id = OBJECT_ID('IncidentWaterStock')) CREATE NONCLUSTERED INDEX [IX_IncidentWaterStock_Activation] ON [IncidentWaterStock] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentWaterStock_Plan' AND object_id = OBJECT_ID('IncidentWaterStock')) CREATE NONCLUSTERED INDEX [IX_IncidentWaterStock_Plan] ON [IncidentWaterStock] ([DepartmentId], [IncidentWaterPlanId], [MeasuredOn]);");
			}

			if (!Schema.Table("IncidentWaterDeliveries").Exists())
			{
				Create.Table("IncidentWaterDeliveries")
					.WithColumn("IncidentWaterDeliveryId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("IncidentWaterPlanId").AsString(36).NotNullable()
					.WithColumn("IncidentVendorEngagementId").AsString(36).Nullable()
					.WithColumn("Quantity").AsDecimal(18, 4).NotNullable().WithDefaultValue(0)
					.WithColumn("UnitOfMeasure").AsString(16).NotNullable()
					.WithColumn("CanonicalQuantity").AsDecimal(18, 4).Nullable()
					.WithColumn("CanonicalUnit").AsString(16).Nullable()
					.WithColumn("ScheduledOn").AsDateTime2().Nullable()
					.WithColumn("DeliveredOn").AsDateTime2().Nullable()
					.WithColumn("TestResult").AsString(200).Nullable()
					.WithColumn("Notes").AsString(1000).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentWaterDeliveries_Activation' AND object_id = OBJECT_ID('IncidentWaterDeliveries')) CREATE NONCLUSTERED INDEX [IX_IncidentWaterDeliveries_Activation] ON [IncidentWaterDeliveries] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentWaterDeliveries_Plan' AND object_id = OBJECT_ID('IncidentWaterDeliveries')) CREATE NONCLUSTERED INDEX [IX_IncidentWaterDeliveries_Plan] ON [IncidentWaterDeliveries] ([DepartmentId], [IncidentWaterPlanId]);");
			}

			if (!Schema.Table("IncidentDemandCoefficients").Exists())
			{
				Create.Table("IncidentDemandCoefficients")
					.WithColumn("IncidentDemandCoefficientId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).Nullable()
					.WithColumn("Kind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("PopulationClass").AsString(32).Nullable()
					.WithColumn("Value").AsDecimal(18, 4).NotNullable().WithDefaultValue(0)
					.WithColumn("Unit").AsString(16).Nullable()
					.WithColumn("Climate").AsString(32).Nullable()
					.WithColumn("Notes").AsString(500).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentDemandCoefficients_Department' AND object_id = OBJECT_ID('IncidentDemandCoefficients')) CREATE NONCLUSTERED INDEX [IX_IncidentDemandCoefficients_Department] ON [IncidentDemandCoefficients] ([DepartmentId]);");
			}

			if (!Schema.Table("IncidentServiceTemplates").Exists())
			{
				Create.Table("IncidentServiceTemplates")
					.WithColumn("IncidentServiceTemplateId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("Code").AsString(32).NotNullable()
					.WithColumn("Name").AsString(200).NotNullable()
					.WithColumn("Category").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("DefaultSlaMinutes").AsInt32().Nullable()
					.WithColumn("IsSelfService").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("IsActive").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("Description").AsString(1000).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentServiceTemplates_Department' AND object_id = OBJECT_ID('IncidentServiceTemplates')) CREATE NONCLUSTERED INDEX [IX_IncidentServiceTemplates_Department] ON [IncidentServiceTemplates] ([DepartmentId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_IncidentServiceTemplates_Code' AND object_id = OBJECT_ID('IncidentServiceTemplates')) CREATE UNIQUE NONCLUSTERED INDEX [UX_IncidentServiceTemplates_Code] ON [IncidentServiceTemplates] ([DepartmentId], [Code]) WHERE [IsDeleted] = 0;");
			}

			if (!Schema.Table("IncidentServiceRequests").Exists())
			{
				Create.Table("IncidentServiceRequests")
					.WithColumn("IncidentServiceRequestId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("IncidentOperationalPeriodId").AsString(36).Nullable()
					.WithColumn("IncidentServiceTemplateId").AsString(36).NotNullable()
					.WithColumn("TicketNumber").AsString(32).NotNullable()
					.WithColumn("RequesterUserId").AsString(128).Nullable()
					.WithColumn("RequesterParticipantId").AsString(36).Nullable()
					.WithColumn("IncidentFacilityId").AsString(36).Nullable()
					.WithColumn("Location").AsString(200).Nullable()
					.WithColumn("Priority").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("NeededBy").AsDateTime2().Nullable()
					.WithColumn("Title").AsString(200).NotNullable()
					.WithColumn("Quantity").AsDecimal(18, 4).Nullable()
					.WithColumn("Unit").AsString(16).Nullable()
					.WithColumn("AssetReference").AsString(200).Nullable()
					.WithColumn("AssignedPosition").AsString(32).Nullable()
					.WithColumn("AssignedUserId").AsString(128).Nullable()
					.WithColumn("IncidentVendorEngagementId").AsString(36).Nullable()
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("SlaDueOn").AsDateTime2().Nullable()
					.WithColumn("SlaBreached").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("CompletedOn").AsDateTime2().Nullable()
					.WithColumn("CustomerConfirmedOn").AsDateTime2().Nullable()
					.WithColumn("CostAmount").AsDecimal(18, 2).Nullable()
					.WithColumn("Currency").AsString(3).Nullable()
					.WithColumn("WorkOrderId").AsString(36).Nullable()
					.WithColumn("Description").AsString(int.MaxValue).Nullable()
					.WithColumn("CompletionNotes").AsString(int.MaxValue).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedCatalogVersion").AsInt32().Nullable()
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentServiceRequests_Activation' AND object_id = OBJECT_ID('IncidentServiceRequests')) CREATE NONCLUSTERED INDEX [IX_IncidentServiceRequests_Activation] ON [IncidentServiceRequests] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentServiceRequests_Status' AND object_id = OBJECT_ID('IncidentServiceRequests')) CREATE NONCLUSTERED INDEX [IX_IncidentServiceRequests_Status] ON [IncidentServiceRequests] ([DepartmentId], [IncidentActivationId], [Status]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentServiceRequests_Template' AND object_id = OBJECT_ID('IncidentServiceRequests')) CREATE NONCLUSTERED INDEX [IX_IncidentServiceRequests_Template] ON [IncidentServiceRequests] ([DepartmentId], [IncidentServiceTemplateId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_IncidentServiceRequests_Ticket' AND object_id = OBJECT_ID('IncidentServiceRequests')) CREATE UNIQUE NONCLUSTERED INDEX [UX_IncidentServiceRequests_Ticket] ON [IncidentServiceRequests] ([DepartmentId], [IncidentActivationId], [TicketNumber]) WHERE [IsDeleted] = 0;");
			}

			if (!Schema.Table("IncidentServiceActivities").Exists())
			{
				Create.Table("IncidentServiceActivities")
					.WithColumn("IncidentServiceActivityId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("IncidentServiceRequestId").AsString(36).NotNullable()
					.WithColumn("Kind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("LaborMinutes").AsInt32().Nullable()
					.WithColumn("Amount").AsDecimal(18, 2).Nullable()
					.WithColumn("Currency").AsString(3).Nullable()
					.WithColumn("InventoryTransactionId").AsString(36).Nullable()
					.WithColumn("Description").AsString(int.MaxValue).Nullable()
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedCatalogVersion").AsInt32().Nullable()
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentServiceActivities_Activation' AND object_id = OBJECT_ID('IncidentServiceActivities')) CREATE NONCLUSTERED INDEX [IX_IncidentServiceActivities_Activation] ON [IncidentServiceActivities] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentServiceActivities_Ticket' AND object_id = OBJECT_ID('IncidentServiceActivities')) CREATE NONCLUSTERED INDEX [IX_IncidentServiceActivities_Ticket] ON [IncidentServiceActivities] ([DepartmentId], [IncidentServiceRequestId]);");
			}

			if (!Schema.Table("IncidentServiceAssets").Exists())
			{
				Create.Table("IncidentServiceAssets")
					.WithColumn("IncidentServiceAssetId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("IncidentServiceTemplateId").AsString(36).Nullable()
					.WithColumn("Name").AsString(200).NotNullable()
					.WithColumn("Kind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("SerialNumber").AsString(100).Nullable()
					.WithColumn("InventoryAssetId").AsString(36).Nullable()
					.WithColumn("IncidentFacilityId").AsString(36).Nullable()
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentServiceAssets_Activation' AND object_id = OBJECT_ID('IncidentServiceAssets')) CREATE NONCLUSTERED INDEX [IX_IncidentServiceAssets_Activation] ON [IncidentServiceAssets] ([DepartmentId], [IncidentActivationId]);");
			}

			if (!Schema.Table("IncidentTransportRequests").Exists())
			{
				Create.Table("IncidentTransportRequests")
					.WithColumn("IncidentTransportRequestId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("Kind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("RequestedByUserId").AsString(128).Nullable()
					.WithColumn("PickupLocation").AsString(200).Nullable()
					.WithColumn("DropoffLocation").AsString(200).Nullable()
					.WithColumn("NeededOn").AsDateTime2().Nullable()
					.WithColumn("Passengers").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Cargo").AsString(500).Nullable()
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("AssignedVehicle").AsString(200).Nullable()
					.WithColumn("AssignedDriver").AsString(200).Nullable()
					.WithColumn("Notes").AsString(1000).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentTransportRequests_Activation' AND object_id = OBJECT_ID('IncidentTransportRequests')) CREATE NONCLUSTERED INDEX [IX_IncidentTransportRequests_Activation] ON [IncidentTransportRequests] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentTransportRequests_Status' AND object_id = OBJECT_ID('IncidentTransportRequests')) CREATE NONCLUSTERED INDEX [IX_IncidentTransportRequests_Status] ON [IncidentTransportRequests] ([DepartmentId], [IncidentActivationId], [Status]);");
			}

			if (!Schema.Table("IncidentTransportMovements").Exists())
			{
				Create.Table("IncidentTransportMovements")
					.WithColumn("IncidentTransportMovementId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("IncidentTransportRequestId").AsString(36).Nullable()
					.WithColumn("Kind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Route").AsString(500).Nullable()
					.WithColumn("VehicleLabel").AsString(200).Nullable()
					.WithColumn("DriverName").AsString(200).Nullable()
					.WithColumn("DepartedOn").AsDateTime2().Nullable()
					.WithColumn("ArrivedOn").AsDateTime2().Nullable()
					.WithColumn("EtaOn").AsDateTime2().Nullable()
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("PassengerCount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("PassengerNames").AsString(int.MaxValue).Nullable()
					.WithColumn("Mileage").AsDecimal(18, 4).Nullable()
					.WithColumn("ExceptionKind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Notes").AsString(1000).Nullable()
					.WithColumn("PickupAddresses").AsString(int.MaxValue).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedCatalogVersion").AsInt32().Nullable()
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentTransportMovements_Activation' AND object_id = OBJECT_ID('IncidentTransportMovements')) CREATE NONCLUSTERED INDEX [IX_IncidentTransportMovements_Activation] ON [IncidentTransportMovements] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentTransportMovements_Day' AND object_id = OBJECT_ID('IncidentTransportMovements')) CREATE NONCLUSTERED INDEX [IX_IncidentTransportMovements_Day] ON [IncidentTransportMovements] ([DepartmentId], [IncidentActivationId], [DepartedOn]);");
			}

			if (!Schema.Table("IncidentTravelItineraries").Exists())
			{
				Create.Table("IncidentTravelItineraries")
					.WithColumn("IncidentTravelItineraryId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("SubjectType").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("SubjectId").AsString(36).NotNullable()
					.WithColumn("Direction").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("TravelMode").AsString(64).Nullable()
					.WithColumn("DepartureOn").AsDate().Nullable()
					.WithColumn("Destination").AsString(200).Nullable()
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("BookingLocator").AsString(int.MaxValue).Nullable()
					.WithColumn("CarrierDetail").AsString(int.MaxValue).Nullable()
					.WithColumn("PickupAddress").AsString(int.MaxValue).Nullable()
					.WithColumn("Notes").AsString(int.MaxValue).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedCatalogVersion").AsInt32().Nullable()
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentTravelItineraries_Activation' AND object_id = OBJECT_ID('IncidentTravelItineraries')) CREATE NONCLUSTERED INDEX [IX_IncidentTravelItineraries_Activation] ON [IncidentTravelItineraries] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentTravelItineraries_Subject' AND object_id = OBJECT_ID('IncidentTravelItineraries')) CREATE NONCLUSTERED INDEX [IX_IncidentTravelItineraries_Subject] ON [IncidentTravelItineraries] ([DepartmentId], [SubjectId]);");
			}

			if (!Schema.Table("IncidentFuelTransactions").Exists())
			{
				Create.Table("IncidentFuelTransactions")
					.WithColumn("IncidentFuelTransactionId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("FuelType").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Kind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("SiteName").AsString(200).Nullable()
					.WithColumn("IncidentFacilityId").AsString(36).Nullable()
					.WithColumn("IncidentResourceId").AsString(36).Nullable()
					.WithColumn("Quantity").AsDecimal(18, 4).NotNullable().WithDefaultValue(0)
					.WithColumn("UnitOfMeasure").AsString(16).NotNullable()
					.WithColumn("CanonicalQuantity").AsDecimal(18, 4).Nullable()
					.WithColumn("CanonicalUnit").AsString(16).Nullable()
					.WithColumn("CardLastFour").AsString(4).Nullable()
					.WithColumn("IncidentVendorEngagementId").AsString(36).Nullable()
					.WithColumn("OccurredOn").AsDateTime2().NotNullable()
					.WithColumn("ExceptionKind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Notes").AsString(1000).Nullable()
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentFuelTransactions_Activation' AND object_id = OBJECT_ID('IncidentFuelTransactions')) CREATE NONCLUSTERED INDEX [IX_IncidentFuelTransactions_Activation] ON [IncidentFuelTransactions] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentFuelTransactions_Day' AND object_id = OBJECT_ID('IncidentFuelTransactions')) CREATE NONCLUSTERED INDEX [IX_IncidentFuelTransactions_Day] ON [IncidentFuelTransactions] ([DepartmentId], [IncidentActivationId], [OccurredOn]);");
			}

			if (!Schema.Table("IncidentCommunicationsPlans").Exists())
			{
				Create.Table("IncidentCommunicationsPlans")
					.WithColumn("IncidentCommunicationsPlanId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("IncidentOperationalPeriodId").AsString(36).Nullable()
					.WithColumn("RecordId").AsString(36).Nullable()
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Notes").AsString(1000).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentCommunicationsPlans_Activation' AND object_id = OBJECT_ID('IncidentCommunicationsPlans')) CREATE NONCLUSTERED INDEX [IX_IncidentCommunicationsPlans_Activation] ON [IncidentCommunicationsPlans] ([DepartmentId], [IncidentActivationId]);");
			}

			if (!Schema.Table("IncidentCommunicationsResourceAssignments").Exists())
			{
				Create.Table("IncidentCommunicationsResourceAssignments")
					.WithColumn("IncidentCommunicationsResourceAssignmentId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("IncidentCommunicationsPlanId").AsString(36).Nullable()
					.WithColumn("ItemKind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ItemLabel").AsString(200).NotNullable()
					.WithColumn("SerialNumber").AsString(100).Nullable()
					.WithColumn("InventoryAssetId").AsString(36).Nullable()
					.WithColumn("HolderType").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("HolderId").AsString(36).Nullable()
					.WithColumn("HolderName").AsString(200).Nullable()
					.WithColumn("IssuedOn").AsDateTime2().Nullable()
					.WithColumn("ExpectedReturnOn").AsDateTime2().Nullable()
					.WithColumn("ReturnedOn").AsDateTime2().Nullable()
					.WithColumn("ProgrammingProfile").AsString(200).Nullable()
					.WithColumn("Condition").AsString(200).Nullable()
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Notes").AsString(1000).Nullable()
					.WithColumn("RestrictedChannelIds").AsString(int.MaxValue).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedCatalogVersion").AsInt32().Nullable()
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentCommunicationsResourceAssignments_Activation' AND object_id = OBJECT_ID('IncidentCommunicationsResourceAssignments')) CREATE NONCLUSTERED INDEX [IX_IncidentCommunicationsResourceAssignments_Activation] ON [IncidentCommunicationsResourceAssignments] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentCommunicationsResourceAssignments_Status' AND object_id = OBJECT_ID('IncidentCommunicationsResourceAssignments')) CREATE NONCLUSTERED INDEX [IX_IncidentCommunicationsResourceAssignments_Status] ON [IncidentCommunicationsResourceAssignments] ([DepartmentId], [IncidentActivationId], [Status]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentCommunicationsResourceAssignments_Holder' AND object_id = OBJECT_ID('IncidentCommunicationsResourceAssignments')) CREATE NONCLUSTERED INDEX [IX_IncidentCommunicationsResourceAssignments_Holder] ON [IncidentCommunicationsResourceAssignments] ([DepartmentId], [HolderId]);");
			}
		}

		public override void Down()
		{
			if (Schema.Table("IncidentCommunicationsResourceAssignments").Exists())
				Delete.Table("IncidentCommunicationsResourceAssignments");
			if (Schema.Table("IncidentCommunicationsPlans").Exists())
				Delete.Table("IncidentCommunicationsPlans");
			if (Schema.Table("IncidentFuelTransactions").Exists())
				Delete.Table("IncidentFuelTransactions");
			if (Schema.Table("IncidentTravelItineraries").Exists())
				Delete.Table("IncidentTravelItineraries");
			if (Schema.Table("IncidentTransportMovements").Exists())
				Delete.Table("IncidentTransportMovements");
			if (Schema.Table("IncidentTransportRequests").Exists())
				Delete.Table("IncidentTransportRequests");
			if (Schema.Table("IncidentServiceAssets").Exists())
				Delete.Table("IncidentServiceAssets");
			if (Schema.Table("IncidentServiceActivities").Exists())
				Delete.Table("IncidentServiceActivities");
			if (Schema.Table("IncidentServiceRequests").Exists())
				Delete.Table("IncidentServiceRequests");
			if (Schema.Table("IncidentServiceTemplates").Exists())
				Delete.Table("IncidentServiceTemplates");
			if (Schema.Table("IncidentDemandCoefficients").Exists())
				Delete.Table("IncidentDemandCoefficients");
			if (Schema.Table("IncidentWaterDeliveries").Exists())
				Delete.Table("IncidentWaterDeliveries");
			if (Schema.Table("IncidentWaterStock").Exists())
				Delete.Table("IncidentWaterStock");
			if (Schema.Table("IncidentWaterPlans").Exists())
				Delete.Table("IncidentWaterPlans");
			if (Schema.Table("IncidentMealActuals").Exists())
				Delete.Table("IncidentMealActuals");
			if (Schema.Table("IncidentMealOrders").Exists())
				Delete.Table("IncidentMealOrders");
			if (Schema.Table("IncidentMealDemands").Exists())
				Delete.Table("IncidentMealDemands");
			if (Schema.Table("IncidentMealPlans").Exists())
				Delete.Table("IncidentMealPlans");
			if (Schema.Table("IncidentRoomingListVersions").Exists())
				Delete.Table("IncidentRoomingListVersions");
			if (Schema.Table("IncidentLodgingAssignments").Exists())
				Delete.Table("IncidentLodgingAssignments");
			if (Schema.Table("IncidentLodgingNightInventory").Exists())
				Delete.Table("IncidentLodgingNightInventory");
			if (Schema.Table("IncidentLodgingBeds").Exists())
				Delete.Table("IncidentLodgingBeds");
			if (Schema.Table("IncidentLodgingUnits").Exists())
				Delete.Table("IncidentLodgingUnits");
			if (Schema.Table("IncidentLodgingBlocks").Exists())
				Delete.Table("IncidentLodgingBlocks");
			if (Schema.Table("IncidentLodgingProperties").Exists())
				Delete.Table("IncidentLodgingProperties");
			if (Schema.Table("IncidentFacilityServices").Exists())
				Delete.Table("IncidentFacilityServices");
			if (Schema.Table("IncidentFacilityAgreements").Exists())
				Delete.Table("IncidentFacilityAgreements");
			if (Schema.Table("IncidentFacilityCapacities").Exists())
				Delete.Table("IncidentFacilityCapacities");
			if (Schema.Table("IncidentFacilities").Exists())
				Delete.Table("IncidentFacilities");
		}
	}
}
