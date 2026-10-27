using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Incident Back Office LI-B vendors (plan §9.12, §9.28): the department support plan, areas and vendor preferences,
	/// children of the shipped InventoryVendors row (locations, capabilities, availability windows, compliance documents,
	/// protected tax / remittance detail), incident vendor engagements, capacity holds and commitments, quotes, and participant
	/// / vendor self-service links (ADP catalog 35). No foreign keys (the IC precedent): tenant ownership is enforced in the
	/// service layer and the department purge (IncidentBackOfficeDepartmentCleanup) removes every row. Existence-guarded for
	/// safe retry.
	/// </summary>
	[Migration(294)]
	public class M0294_AddIncidentVendorsAndSelfService : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("DepartmentEmergencySupportPlans").Exists())
			{
				Create.Table("DepartmentEmergencySupportPlans")
					.WithColumn("DepartmentEmergencySupportPlanId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("Name").AsString(200).NotNullable()
					.WithColumn("Description").AsString(int.MaxValue).Nullable()
					.WithColumn("ReviewedOn").AsDateTime2().Nullable()
					.WithColumn("NextReviewOn").AsDate().Nullable()
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DepartmentEmergencySupportPlans_Department' AND object_id = OBJECT_ID('DepartmentEmergencySupportPlans')) CREATE NONCLUSTERED INDEX [IX_DepartmentEmergencySupportPlans_Department] ON [DepartmentEmergencySupportPlans] ([DepartmentId]);");
			}

			if (!Schema.Table("SupportPlanAreas").Exists())
			{
				Create.Table("SupportPlanAreas")
					.WithColumn("SupportPlanAreaId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("DepartmentEmergencySupportPlanId").AsString(36).NotNullable()
					.WithColumn("Name").AsString(200).NotNullable()
					.WithColumn("Geography").AsString(1000).Nullable()
					.WithColumn("IncidentTypes").AsString(500).Nullable()
					.WithColumn("ActivationThreshold").AsString(500).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SupportPlanAreas_Department' AND object_id = OBJECT_ID('SupportPlanAreas')) CREATE NONCLUSTERED INDEX [IX_SupportPlanAreas_Department] ON [SupportPlanAreas] ([DepartmentId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SupportPlanAreas_Plan' AND object_id = OBJECT_ID('SupportPlanAreas')) CREATE NONCLUSTERED INDEX [IX_SupportPlanAreas_Plan] ON [SupportPlanAreas] ([DepartmentId], [DepartmentEmergencySupportPlanId]);");
			}

			if (!Schema.Table("SupportPlanVendorPreferences").Exists())
			{
				Create.Table("SupportPlanVendorPreferences")
					.WithColumn("SupportPlanVendorPreferenceId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("DepartmentEmergencySupportPlanId").AsString(36).NotNullable()
					.WithColumn("SupportPlanAreaId").AsString(36).Nullable()
					.WithColumn("VendorId").AsString(36).NotNullable()
					.WithColumn("Category").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Rank").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Notes").AsString(500).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SupportPlanVendorPreferences_Department' AND object_id = OBJECT_ID('SupportPlanVendorPreferences')) CREATE NONCLUSTERED INDEX [IX_SupportPlanVendorPreferences_Department] ON [SupportPlanVendorPreferences] ([DepartmentId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SupportPlanVendorPreferences_Plan' AND object_id = OBJECT_ID('SupportPlanVendorPreferences')) CREATE NONCLUSTERED INDEX [IX_SupportPlanVendorPreferences_Plan] ON [SupportPlanVendorPreferences] ([DepartmentId], [DepartmentEmergencySupportPlanId], [Category]);");
			}

			if (!Schema.Table("IncidentSupportPlanSnapshots").Exists())
			{
				Create.Table("IncidentSupportPlanSnapshots")
					.WithColumn("IncidentSupportPlanSnapshotId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("DepartmentEmergencySupportPlanId").AsString(36).NotNullable()
					.WithColumn("ContentJson").AsString(int.MaxValue).NotNullable()
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentSupportPlanSnapshots_Activation' AND object_id = OBJECT_ID('IncidentSupportPlanSnapshots')) CREATE NONCLUSTERED INDEX [IX_IncidentSupportPlanSnapshots_Activation] ON [IncidentSupportPlanSnapshots] ([DepartmentId], [IncidentActivationId]);");
			}

			if (!Schema.Table("VendorLocations").Exists())
			{
				Create.Table("VendorLocations")
					.WithColumn("VendorLocationId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("VendorId").AsString(36).NotNullable()
					.WithColumn("Name").AsString(200).NotNullable()
					.WithColumn("Address").AsString(500).Nullable()
					.WithColumn("ServiceRadiusKm").AsDecimal(18, 4).Nullable()
					.WithColumn("DispatchPhone").AsString(64).Nullable()
					.WithColumn("AfterHoursPhone").AsString(64).Nullable()
					.WithColumn("OrderingChannel").AsString(200).Nullable()
					.WithColumn("Hours").AsString(200).Nullable()
					.WithColumn("Is24x7").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("Languages").AsString(200).Nullable()
					.WithColumn("Notes").AsString(1000).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_VendorLocations_Department' AND object_id = OBJECT_ID('VendorLocations')) CREATE NONCLUSTERED INDEX [IX_VendorLocations_Department] ON [VendorLocations] ([DepartmentId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_VendorLocations_Vendor' AND object_id = OBJECT_ID('VendorLocations')) CREATE NONCLUSTERED INDEX [IX_VendorLocations_Vendor] ON [VendorLocations] ([DepartmentId], [VendorId]);");
			}

			if (!Schema.Table("VendorCapabilities").Exists())
			{
				Create.Table("VendorCapabilities")
					.WithColumn("VendorCapabilityId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("VendorId").AsString(36).NotNullable()
					.WithColumn("VendorLocationId").AsString(36).Nullable()
					.WithColumn("Category").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Name").AsString(200).NotNullable()
					.WithColumn("Unit").AsString(32).NotNullable()
					.WithColumn("NormalCapacity").AsDecimal(18, 4).NotNullable().WithDefaultValue(0)
					.WithColumn("MaxCapacity").AsDecimal(18, 4).NotNullable().WithDefaultValue(0)
					.WithColumn("LeadTimeHours").AsInt32().Nullable()
					.WithColumn("MinimumOrder").AsString(200).Nullable()
					.WithColumn("CutoffTime").AsString(64).Nullable()
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("LastVerifiedOn").AsDateTime2().Nullable()
					.WithColumn("VerifiedByUserId").AsString(128).Nullable()
					.WithColumn("Notes").AsString(1000).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_VendorCapabilities_Department' AND object_id = OBJECT_ID('VendorCapabilities')) CREATE NONCLUSTERED INDEX [IX_VendorCapabilities_Department] ON [VendorCapabilities] ([DepartmentId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_VendorCapabilities_Vendor' AND object_id = OBJECT_ID('VendorCapabilities')) CREATE NONCLUSTERED INDEX [IX_VendorCapabilities_Vendor] ON [VendorCapabilities] ([DepartmentId], [VendorId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_VendorCapabilities_Category' AND object_id = OBJECT_ID('VendorCapabilities')) CREATE NONCLUSTERED INDEX [IX_VendorCapabilities_Category] ON [VendorCapabilities] ([DepartmentId], [Category]);");
			}

			if (!Schema.Table("VendorAvailabilityWindows").Exists())
			{
				Create.Table("VendorAvailabilityWindows")
					.WithColumn("VendorAvailabilityWindowId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("VendorCapabilityId").AsString(36).NotNullable()
					.WithColumn("StartsOn").AsDateTime2().NotNullable()
					.WithColumn("EndsOn").AsDateTime2().NotNullable()
					.WithColumn("Kind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Capacity").AsDecimal(18, 4).Nullable()
					.WithColumn("Notes").AsString(500).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_VendorAvailabilityWindows_Department' AND object_id = OBJECT_ID('VendorAvailabilityWindows')) CREATE NONCLUSTERED INDEX [IX_VendorAvailabilityWindows_Department] ON [VendorAvailabilityWindows] ([DepartmentId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_VendorAvailabilityWindows_Capability' AND object_id = OBJECT_ID('VendorAvailabilityWindows')) CREATE NONCLUSTERED INDEX [IX_VendorAvailabilityWindows_Capability] ON [VendorAvailabilityWindows] ([DepartmentId], [VendorCapabilityId], [StartsOn]);");
			}

			if (!Schema.Table("VendorComplianceDocuments").Exists())
			{
				Create.Table("VendorComplianceDocuments")
					.WithColumn("VendorComplianceDocumentId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("VendorId").AsString(36).NotNullable()
					.WithColumn("Kind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("DocumentNumber").AsString(100).Nullable()
					.WithColumn("IssuedOn").AsDate().Nullable()
					.WithColumn("ExpiresOn").AsDate().Nullable()
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Notes").AsString(1000).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_VendorComplianceDocuments_Department' AND object_id = OBJECT_ID('VendorComplianceDocuments')) CREATE NONCLUSTERED INDEX [IX_VendorComplianceDocuments_Department] ON [VendorComplianceDocuments] ([DepartmentId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_VendorComplianceDocuments_Vendor' AND object_id = OBJECT_ID('VendorComplianceDocuments')) CREATE NONCLUSTERED INDEX [IX_VendorComplianceDocuments_Vendor] ON [VendorComplianceDocuments] ([DepartmentId], [VendorId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_VendorComplianceDocuments_Expiry' AND object_id = OBJECT_ID('VendorComplianceDocuments')) CREATE NONCLUSTERED INDEX [IX_VendorComplianceDocuments_Expiry] ON [VendorComplianceDocuments] ([DepartmentId], [ExpiresOn]);");
			}

			if (!Schema.Table("VendorProtectedDetails").Exists())
			{
				Create.Table("VendorProtectedDetails")
					.WithColumn("VendorProtectedDetailId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("VendorId").AsString(36).NotNullable()
					.WithColumn("TaxIdentifier").AsString(int.MaxValue).Nullable()
					.WithColumn("RemittanceDetails").AsString(int.MaxValue).Nullable()
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedCatalogVersion").AsInt32().Nullable()
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_VendorProtectedDetails_Department' AND object_id = OBJECT_ID('VendorProtectedDetails')) CREATE NONCLUSTERED INDEX [IX_VendorProtectedDetails_Department] ON [VendorProtectedDetails] ([DepartmentId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_VendorProtectedDetails_Vendor' AND object_id = OBJECT_ID('VendorProtectedDetails')) CREATE UNIQUE NONCLUSTERED INDEX [UX_VendorProtectedDetails_Vendor] ON [VendorProtectedDetails] ([DepartmentId], [VendorId]);");
			}

			if (!Schema.Table("IncidentVendorEngagements").Exists())
			{
				Create.Table("IncidentVendorEngagements")
					.WithColumn("IncidentVendorEngagementId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("VendorId").AsString(36).NotNullable()
					.WithColumn("VendorCapabilityId").AsString(36).Nullable()
					.WithColumn("VendorAgreementId").AsString(36).Nullable()
					.WithColumn("ServiceDescription").AsString(500).NotNullable()
					.WithColumn("IncidentFacilityId").AsString(36).Nullable()
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("PurchaserUserId").AsString(128).Nullable()
					.WithColumn("OwnerUserId").AsString(128).Nullable()
					.WithColumn("WindowStart").AsDateTime2().Nullable()
					.WithColumn("WindowEnd").AsDateTime2().Nullable()
					.WithColumn("Notes").AsString(int.MaxValue).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentVendorEngagements_Activation' AND object_id = OBJECT_ID('IncidentVendorEngagements')) CREATE NONCLUSTERED INDEX [IX_IncidentVendorEngagements_Activation] ON [IncidentVendorEngagements] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentVendorEngagements_Status' AND object_id = OBJECT_ID('IncidentVendorEngagements')) CREATE NONCLUSTERED INDEX [IX_IncidentVendorEngagements_Status] ON [IncidentVendorEngagements] ([DepartmentId], [IncidentActivationId], [Status]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentVendorEngagements_Vendor' AND object_id = OBJECT_ID('IncidentVendorEngagements')) CREATE NONCLUSTERED INDEX [IX_IncidentVendorEngagements_Vendor] ON [IncidentVendorEngagements] ([DepartmentId], [VendorId]);");
			}

			if (!Schema.Table("IncidentVendorCapacityCommitments").Exists())
			{
				Create.Table("IncidentVendorCapacityCommitments")
					.WithColumn("IncidentVendorCapacityCommitmentId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("IncidentVendorEngagementId").AsString(36).NotNullable()
					.WithColumn("VendorCapabilityId").AsString(36).NotNullable()
					.WithColumn("BucketStart").AsDateTime2().NotNullable()
					.WithColumn("BucketEnd").AsDateTime2().NotNullable()
					.WithColumn("Kind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Quantity").AsDecimal(18, 4).NotNullable().WithDefaultValue(0)
					.WithColumn("Unit").AsString(32).NotNullable()
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ExpiresOn").AsDateTime2().Nullable()
					.WithColumn("IsOverride").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("OverrideReason").AsString(500).Nullable()
					.WithColumn("ApprovedByUserId").AsString(128).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentVendorCapacityCommitments_Activation' AND object_id = OBJECT_ID('IncidentVendorCapacityCommitments')) CREATE NONCLUSTERED INDEX [IX_IncidentVendorCapacityCommitments_Activation] ON [IncidentVendorCapacityCommitments] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentVendorCapacityCommitments_Capability' AND object_id = OBJECT_ID('IncidentVendorCapacityCommitments')) CREATE NONCLUSTERED INDEX [IX_IncidentVendorCapacityCommitments_Capability] ON [IncidentVendorCapacityCommitments] ([VendorCapabilityId], [BucketStart], [BucketEnd]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentVendorCapacityCommitments_Engagement' AND object_id = OBJECT_ID('IncidentVendorCapacityCommitments')) CREATE NONCLUSTERED INDEX [IX_IncidentVendorCapacityCommitments_Engagement] ON [IncidentVendorCapacityCommitments] ([DepartmentId], [IncidentVendorEngagementId]);");
			}

			if (!Schema.Table("IncidentVendorQuotes").Exists())
			{
				Create.Table("IncidentVendorQuotes")
					.WithColumn("IncidentVendorQuoteId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("IncidentVendorEngagementId").AsString(36).NotNullable()
					.WithColumn("RequestedOn").AsDateTime2().NotNullable()
					.WithColumn("ReceivedOn").AsDateTime2().Nullable()
					.WithColumn("Amount").AsDecimal(18, 2).Nullable()
					.WithColumn("Currency").AsString(3).NotNullable()
					.WithColumn("Terms").AsString(2000).Nullable()
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentVendorQuotes_Activation' AND object_id = OBJECT_ID('IncidentVendorQuotes')) CREATE NONCLUSTERED INDEX [IX_IncidentVendorQuotes_Activation] ON [IncidentVendorQuotes] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentVendorQuotes_Engagement' AND object_id = OBJECT_ID('IncidentVendorQuotes')) CREATE NONCLUSTERED INDEX [IX_IncidentVendorQuotes_Engagement] ON [IncidentVendorQuotes] ([DepartmentId], [IncidentVendorEngagementId]);");
			}

			if (!Schema.Table("IncidentSelfServiceLinks").Exists())
			{
				Create.Table("IncidentSelfServiceLinks")
					.WithColumn("IncidentSelfServiceLinkId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("SubjectType").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("SubjectId").AsString(36).NotNullable()
					.WithColumn("TokenHash").AsString(128).NotNullable()
					.WithColumn("Purposes").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ExpiresOn").AsDateTime2().NotNullable()
					.WithColumn("RevokedOn").AsDateTime2().Nullable()
					.WithColumn("RevokedByUserId").AsString(128).Nullable()
					.WithColumn("LastUsedOn").AsDateTime2().Nullable()
					.WithColumn("UseCount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("OneTimeCodeHash").AsString(128).Nullable()
					.WithColumn("OneTimeCodeExpiresOn").AsDateTime2().Nullable()
					.WithColumn("VerifiedOn").AsDateTime2().Nullable()
					.WithColumn("DeliveryTarget").AsString(int.MaxValue).Nullable()
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedCatalogVersion").AsInt32().Nullable()
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentSelfServiceLinks_Activation' AND object_id = OBJECT_ID('IncidentSelfServiceLinks')) CREATE NONCLUSTERED INDEX [IX_IncidentSelfServiceLinks_Activation] ON [IncidentSelfServiceLinks] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentSelfServiceLinks_Subject' AND object_id = OBJECT_ID('IncidentSelfServiceLinks')) CREATE NONCLUSTERED INDEX [IX_IncidentSelfServiceLinks_Subject] ON [IncidentSelfServiceLinks] ([DepartmentId], [SubjectId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_IncidentSelfServiceLinks_Token' AND object_id = OBJECT_ID('IncidentSelfServiceLinks')) CREATE UNIQUE NONCLUSTERED INDEX [UX_IncidentSelfServiceLinks_Token] ON [IncidentSelfServiceLinks] ([TokenHash]);");
			}
		}

		public override void Down()
		{
			if (Schema.Table("IncidentSelfServiceLinks").Exists())
				Delete.Table("IncidentSelfServiceLinks");
			if (Schema.Table("IncidentVendorQuotes").Exists())
				Delete.Table("IncidentVendorQuotes");
			if (Schema.Table("IncidentVendorCapacityCommitments").Exists())
				Delete.Table("IncidentVendorCapacityCommitments");
			if (Schema.Table("IncidentVendorEngagements").Exists())
				Delete.Table("IncidentVendorEngagements");
			if (Schema.Table("VendorProtectedDetails").Exists())
				Delete.Table("VendorProtectedDetails");
			if (Schema.Table("VendorComplianceDocuments").Exists())
				Delete.Table("VendorComplianceDocuments");
			if (Schema.Table("VendorAvailabilityWindows").Exists())
				Delete.Table("VendorAvailabilityWindows");
			if (Schema.Table("VendorCapabilities").Exists())
				Delete.Table("VendorCapabilities");
			if (Schema.Table("VendorLocations").Exists())
				Delete.Table("VendorLocations");
			if (Schema.Table("IncidentSupportPlanSnapshots").Exists())
				Delete.Table("IncidentSupportPlanSnapshots");
			if (Schema.Table("SupportPlanVendorPreferences").Exists())
				Delete.Table("SupportPlanVendorPreferences");
			if (Schema.Table("SupportPlanAreas").Exists())
				Delete.Table("SupportPlanAreas");
			if (Schema.Table("DepartmentEmergencySupportPlans").Exists())
				Delete.Table("DepartmentEmergencySupportPlans");
		}
	}
}
