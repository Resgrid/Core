using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Workforce &amp; Business Operations plan, Phase C (C1, C11): invoice provenance columns (service contract, deployment, time report per line), the Cal OES MARS shadow tables (agency profile, F-5 resource crosswalk, annual rate profiles/lines, administrative-rate inputs, agreement snapshots, work items, expected reimbursement lines — local mirrors of an external system that stores no portal credentials), and the three Phase C feature flags: Operations.Deployments (free), Invoicing.ContractorBilling and CostRecovery.CalOesMars (prerequisite Business.Operations). Registry M0219. Guarded for safe retry.
	/// </summary>
	[Migration(219)]
	public class M0219_ExtendInvoicingAndAddCostRecoveryProfiles : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("Invoices").Column("ServiceContractId").Exists())
				Alter.Table("Invoices").AddColumn("ServiceContractId").AsString(36).Nullable();
			if (!Schema.Table("Invoices").Column("DeploymentId").Exists())
				Alter.Table("Invoices").AddColumn("DeploymentId").AsString(36).Nullable();
			if (!Schema.Table("InvoiceLineItems").Column("DeploymentTimeReportId").Exists())
				Alter.Table("InvoiceLineItems").AddColumn("DeploymentTimeReportId").AsString(36).Nullable();
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Invoices_Deployment' AND object_id = OBJECT_ID('Invoices')) CREATE INDEX [IX_Invoices_Deployment] ON [Invoices] ([DeploymentId]) WHERE [DeploymentId] IS NOT NULL;");

			if (!Schema.Table("CalOesMarsAgencyProfiles").Exists())
			{
				Create.Table("CalOesMarsAgencyProfiles")
					.WithColumn("CalOesMarsAgencyProfileId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("AuthorityProfileCode").AsString(50).NotNullable()
					.WithColumn("MacsDesignator").AsString(20).Nullable()
					.WithColumn("AgencyName").AsString(250).Nullable()
					.WithColumn("AgencyCategory").AsString(50).Nullable()
					.WithColumn("ContactName").AsString(int.MaxValue).Nullable()
					.WithColumn("ContactPhone").AsString(int.MaxValue).Nullable()
					.WithColumn("ContactEmail").AsString(int.MaxValue).Nullable()
					.WithColumn("Address").AsString(int.MaxValue).Nullable()
					.WithColumn("FeinReference").AsString(int.MaxValue).Nullable()
					.WithColumn("UeiReference").AsString(int.MaxValue).Nullable()
					.WithColumn("SamReference").AsString(int.MaxValue).Nullable()
					.WithColumn("FiscalSupplierReference").AsString(int.MaxValue).Nullable()
					.WithColumn("PortalAccountRole").AsString(50).Nullable()
					.WithColumn("PortalAccountReference").AsString(200).Nullable()
					.WithColumn("VerifiedOn").AsDateTime2().Nullable()
					.WithColumn("VerifiedByUserId").AsString(128).Nullable()
					.WithColumn("IsActive").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("SourceArtifact").AsString(int.MaxValue).Nullable()
					.WithColumn("SourceChecksum").AsString(128).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_CalOesMarsAgencyProfiles_Department' AND object_id = OBJECT_ID('CalOesMarsAgencyProfiles')) CREATE UNIQUE INDEX [UX_CalOesMarsAgencyProfiles_Department] ON [CalOesMarsAgencyProfiles] ([DepartmentId]) WHERE [IsDeleted] = 0;");
			}
			if (!Schema.Table("CalOesMarsResourceProfiles").Exists())
			{
				Create.Table("CalOesMarsResourceProfiles")
					.WithColumn("CalOesMarsResourceProfileId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("SubjectType").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("UnitId").AsInt32().Nullable()
					.WithColumn("InventoryAssetId").AsString(36).Nullable()
					.WithColumn("ExternalResourceName").AsString(250).Nullable()
					.WithColumn("MarsResourceId").AsString(100).Nullable()
					.WithColumn("ResourceType").AsString(100).Nullable()
					.WithColumn("ResourceKind").AsString(100).Nullable()
					.WithColumn("CodeScheme").AsString(50).Nullable()
					.WithColumn("UnitDesignator").AsString(100).Nullable()
					.WithColumn("LicensePlate").AsString(int.MaxValue).Nullable()
					.WithColumn("Vin").AsString(int.MaxValue).Nullable()
					.WithColumn("SerialNumber").AsString(int.MaxValue).Nullable()
					.WithColumn("Ownership").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("EffectiveOn").AsDateTime2().Nullable()
					.WithColumn("ExpiresOn").AsDateTime2().Nullable()
					.WithColumn("ObservedExternalStatus").AsString(50).Nullable()
					.WithColumn("ObservedOn").AsDateTime2().Nullable()
					.WithColumn("ReviewState").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("SourceArtifact").AsString(int.MaxValue).Nullable()
					.WithColumn("SourceChecksum").AsString(128).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Create.Index("IX_CalOesMarsResourceProfiles_Department").OnTable("CalOesMarsResourceProfiles").OnColumn("DepartmentId").Ascending().OnColumn("IsDeleted").Ascending();
				Create.Index("IX_CalOesMarsResourceProfiles_Unit").OnTable("CalOesMarsResourceProfiles").OnColumn("UnitId").Ascending();
			}
			if (!Schema.Table("CalOesMarsRateProfiles").Exists())
			{
				Create.Table("CalOesMarsRateProfiles")
					.WithColumn("CalOesMarsRateProfileId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("SubmissionYear").AsInt32().NotNullable()
					.WithColumn("SubmissionType").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("EffectiveOn").AsDateTime2().Nullable()
					.WithColumn("ExpiresOn").AsDateTime2().Nullable()
					.WithColumn("BaseRateAccepted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("AdministrativeRateMethod").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("AdministrativeRateValue").AsDecimal(9,4).Nullable()
					.WithColumn("AuthorityProfileCode").AsString(50).NotNullable()
					.WithColumn("SourceUrl").AsString(int.MaxValue).Nullable()
					.WithColumn("SourceDate").AsDateTime2().Nullable()
					.WithColumn("SignedOn").AsDateTime2().Nullable()
					.WithColumn("SignedByName").AsString(int.MaxValue).Nullable()
					.WithColumn("ObservedExternalStatus").AsString(50).Nullable()
					.WithColumn("ObservedOn").AsDateTime2().Nullable()
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("SourceArtifact").AsString(int.MaxValue).Nullable()
					.WithColumn("SourceChecksum").AsString(128).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Create.Index("IX_CalOesMarsRateProfiles_Department").OnTable("CalOesMarsRateProfiles").OnColumn("DepartmentId").Ascending().OnColumn("SubmissionYear").Ascending().OnColumn("IsDeleted").Ascending();
			}
			if (!Schema.Table("CalOesMarsRateLines").Exists())
			{
				Create.Table("CalOesMarsRateLines")
					.WithColumn("CalOesMarsRateLineId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("CalOesMarsRateProfileId").AsString(36).NotNullable()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("LineKind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ClassificationCode").AsString(100).Nullable()
					.WithColumn("ResourceCode").AsString(100).Nullable()
					.WithColumn("FemaCode").AsString(50).Nullable()
					.WithColumn("Description").AsString(int.MaxValue).Nullable()
					.WithColumn("Basis").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("StraightRate").AsDecimal(18,4).Nullable()
					.WithColumn("OvertimeRate").AsDecimal(18,4).Nullable()
					.WithColumn("IncludesWorkersComp").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("IncludesUnemploymentInsurance").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("PortalToPortalEligible").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("OvertimeEligible").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("Authority").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("SourceInputVersions").AsString(int.MaxValue).Nullable()
					.WithColumn("ObservedExternalStatus").AsString(50).Nullable()
					.WithColumn("SortOrder").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("SourceArtifact").AsString(int.MaxValue).Nullable()
					.WithColumn("SourceChecksum").AsString(128).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Create.Index("IX_CalOesMarsRateLines_Profile").OnTable("CalOesMarsRateLines").OnColumn("CalOesMarsRateProfileId").Ascending().OnColumn("SortOrder").Ascending();
				Create.ForeignKey("FK_CalOesMarsRateLines_Profile").FromTable("CalOesMarsRateLines").ForeignColumn("CalOesMarsRateProfileId").ToTable("CalOesMarsRateProfiles").PrimaryColumn("CalOesMarsRateProfileId");
			}
			if (!Schema.Table("CalOesMarsAdministrativeRateInputs").Exists())
			{
				Create.Table("CalOesMarsAdministrativeRateInputs")
					.WithColumn("CalOesMarsAdministrativeRateInputId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("CalOesMarsRateProfileId").AsString(36).NotNullable()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("FiscalYear").AsInt32().NotNullable()
					.WithColumn("FunctionCode").AsString(50).Nullable()
					.WithColumn("CategoryCode").AsString(50).Nullable()
					.WithColumn("CategoryProfileVersion").AsString(50).Nullable()
					.WithColumn("Classification").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ActualAmount").AsString(int.MaxValue).Nullable()
					.WithColumn("SourceSystem").AsString(100).Nullable()
					.WithColumn("SourceLine").AsString(200).Nullable()
					.WithColumn("InputVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("IncidentDirectExclusion").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("DoubleCountMarker").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ReviewStatus").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ReviewReason").AsString(int.MaxValue).Nullable()
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("SourceArtifact").AsString(int.MaxValue).Nullable()
					.WithColumn("SourceChecksum").AsString(128).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Create.Index("IX_CalOesMarsAdministrativeRateInputs_Profile").OnTable("CalOesMarsAdministrativeRateInputs").OnColumn("CalOesMarsRateProfileId").Ascending();
				Create.ForeignKey("FK_CalOesMarsAdministrativeRateInputs_Profile").FromTable("CalOesMarsAdministrativeRateInputs").ForeignColumn("CalOesMarsRateProfileId").ToTable("CalOesMarsRateProfiles").PrimaryColumn("CalOesMarsRateProfileId");
			}
			if (!Schema.Table("CalOesMarsAgreementSnapshots").Exists())
			{
				Create.Table("CalOesMarsAgreementSnapshots")
					.WithColumn("CalOesMarsAgreementSnapshotId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ClassificationCode").AsString(100).Nullable()
					.WithColumn("ClassificationTitle").AsString(250).Nullable()
					.WithColumn("DocumentKind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("CompensationMethod").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("OvertimeMethod").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("StartOn").AsDateTime2().Nullable()
					.WithColumn("EndOn").AsDateTime2().Nullable()
					.WithColumn("ExternalApprovalStatus").AsString(50).Nullable()
					.WithColumn("ObservedOn").AsDateTime2().Nullable()
					.WithColumn("AttachmentId").AsInt32().Nullable()
					.WithColumn("AttachmentChecksum").AsString(128).Nullable()
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("SourceArtifact").AsString(int.MaxValue).Nullable()
					.WithColumn("SourceChecksum").AsString(128).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Create.Index("IX_CalOesMarsAgreementSnapshots_Department").OnTable("CalOesMarsAgreementSnapshots").OnColumn("DepartmentId").Ascending().OnColumn("IsDeleted").Ascending().OnColumn("StartOn").Ascending();
			}
			if (!Schema.Table("CalOesMarsWorkItems").Exists())
			{
				Create.Table("CalOesMarsWorkItems")
					.WithColumn("CalOesMarsWorkItemId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("DeploymentId").AsString(36).Nullable()
					.WithColumn("RmsExternalOrderId").AsString(36).Nullable()
					.WithColumn("RmsExternalOrderFillId").AsString(36).Nullable()
					.WithColumn("RecordType").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("LocalState").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("MarsRecordId").AsString(100).Nullable()
					.WithColumn("MarsInvoiceId").AsString(100).Nullable()
					.WithColumn("ObservedExternalStatus").AsString(50).Nullable()
					.WithColumn("ObservedOn").AsDateTime2().Nullable()
					.WithColumn("ObservedSource").AsString(50).Nullable()
					.WithColumn("CorrectionComment").AsString(int.MaxValue).Nullable()
					.WithColumn("AuthorityProfileCode").AsString(50).Nullable()
					.WithColumn("RateProfileVersion").AsString(50).Nullable()
					.WithColumn("AgreementSnapshotId").AsString(36).Nullable()
					.WithColumn("SnapshotJson").AsString(int.MaxValue).Nullable()
					.WithColumn("ValidationSummaryJson").AsString(int.MaxValue).Nullable()
					.WithColumn("SubmittedByUserId").AsString(128).Nullable()
					.WithColumn("SubmittedOn").AsDateTime2().Nullable()
					.WithColumn("ApprovedByUserId").AsString(128).Nullable()
					.WithColumn("ApprovedOn").AsDateTime2().Nullable()
					.WithColumn("RejectedByUserId").AsString(128).Nullable()
					.WithColumn("RejectedOn").AsDateTime2().Nullable()
					.WithColumn("PaidOn").AsDateTime2().Nullable()
					.WithColumn("ExpectedTotal").AsDecimal(18,2).Nullable()
					.WithColumn("ApprovedTotal").AsDecimal(18,2).Nullable()
					.WithColumn("PaidTotal").AsDecimal(18,2).Nullable()
					.WithColumn("PaymentReference").AsString(int.MaxValue).Nullable()
					.WithColumn("SupersedesWorkItemId").AsString(36).Nullable()
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("SourceArtifact").AsString(int.MaxValue).Nullable()
					.WithColumn("SourceChecksum").AsString(128).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Create.Index("IX_CalOesMarsWorkItems_Department").OnTable("CalOesMarsWorkItems").OnColumn("DepartmentId").Ascending().OnColumn("IsDeleted").Ascending().OnColumn("LocalState").Ascending();
				Create.Index("IX_CalOesMarsWorkItems_Deployment").OnTable("CalOesMarsWorkItems").OnColumn("DeploymentId").Ascending();
				Create.Index("IX_CalOesMarsWorkItems_External").OnTable("CalOesMarsWorkItems").OnColumn("MarsRecordId").Ascending();
			}
			if (!Schema.Table("CalOesMarsReimbursementLines").Exists())
			{
				Create.Table("CalOesMarsReimbursementLines")
					.WithColumn("CalOesMarsReimbursementLineId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("CalOesMarsWorkItemId").AsString(36).NotNullable()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("DeploymentId").AsString(36).Nullable()
					.WithColumn("LineDate").AsDateTime2().Nullable()
					.WithColumn("LineKind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("SubjectType").AsInt32().Nullable()
					.WithColumn("SubjectId").AsString(128).Nullable()
					.WithColumn("SourceWorkId").AsString(36).Nullable()
					.WithColumn("SourceExpenseId").AsString(36).Nullable()
					.WithColumn("Quantity").AsDecimal(18,4).NotNullable().WithDefaultValue(0)
					.WithColumn("Unit").AsString(20).Nullable()
					.WithColumn("Rate").AsDecimal(18,4).NotNullable().WithDefaultValue(0)
					.WithColumn("RateLineId").AsString(36).Nullable()
					.WithColumn("RateLineVersion").AsInt32().Nullable()
					.WithColumn("ExpectedAmount").AsDecimal(18,2).NotNullable().WithDefaultValue(0)
					.WithColumn("ApprovedAmount").AsDecimal(18,2).Nullable()
					.WithColumn("PaidAmount").AsDecimal(18,2).Nullable()
					.WithColumn("EligibilityState").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("EligibilityReason").AsString(int.MaxValue).Nullable()
					.WithColumn("SourceVersions").AsString(int.MaxValue).Nullable()
					.WithColumn("SortOrder").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable();
				Create.Index("IX_CalOesMarsReimbursementLines_WorkItem").OnTable("CalOesMarsReimbursementLines").OnColumn("CalOesMarsWorkItemId").Ascending().OnColumn("SortOrder").Ascending();
				Create.ForeignKey("FK_CalOesMarsReimbursementLines_WorkItem").FromTable("CalOesMarsReimbursementLines").ForeignColumn("CalOesMarsWorkItemId").ToTable("CalOesMarsWorkItems").PrimaryColumn("CalOesMarsWorkItemId");
			}

			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM [FeatureFlags] WHERE [FlagKey] = 'Operations.Deployments') INSERT INTO [FeatureFlags] ([FlagKey], [Name], [Description], [Category], [IsEnabledGlobally]) VALUES ('Operations.Deployments', 'Deployment finance', 'Free deployment finance wrapper around a Call: roster, daily time reports, expenses, attachments, manifests and the RMS external-order link (Workforce & Business Operations plan, Phase C). Independent of the Business Operations add-on. Seeded off.', 'Business', 0);");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM [FeatureFlags] WHERE [FlagKey] = 'Invoicing.ContractorBilling') INSERT INTO [FeatureFlags] ([FlagKey], [Name], [Description], [Category], [IsEnabledGlobally]) VALUES ('Invoicing.ContractorBilling', 'Contractor billing', 'Rate schedules, service contracts, bids, bid conversion, charge calculation and invoice generation from deployments (Workforce & Business Operations plan, Phase C). Requires Business.Operations. Seeded off.', 'Business', 0);");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM [FeatureFlagPrerequisites] p JOIN [FeatureFlags] f ON f.[FeatureFlagId] = p.[FeatureFlagId] JOIN [FeatureFlags] r ON r.[FeatureFlagId] = p.[RequiredFeatureFlagId] WHERE f.[FlagKey] = 'Invoicing.ContractorBilling' AND r.[FlagKey] = 'Business.Operations') INSERT INTO [FeatureFlagPrerequisites] ([FeatureFlagId], [RequiredFeatureFlagId], [RequiredValue]) SELECT f.[FeatureFlagId], r.[FeatureFlagId], NULL FROM [FeatureFlags] f CROSS JOIN [FeatureFlags] r WHERE f.[FlagKey] = 'Invoicing.ContractorBilling' AND r.[FlagKey] = 'Business.Operations';");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM [FeatureFlags] WHERE [FlagKey] = 'CostRecovery.CalOesMars') INSERT INTO [FeatureFlags] ([FlagKey], [Name], [Description], [Category], [IsEnabledGlobally]) VALUES ('CostRecovery.CalOesMars', 'Cal OES MARS cost recovery', 'California Fire Assistance Agreement cost-recovery preparation and reconciliation for the Cal OES Mutual Aid Reimbursement System (Workforce & Business Operations plan, Phase C11). Manual portal handoff only; requires Business.Operations. Seeded off.', 'Business', 0);");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM [FeatureFlagPrerequisites] p JOIN [FeatureFlags] f ON f.[FeatureFlagId] = p.[FeatureFlagId] JOIN [FeatureFlags] r ON r.[FeatureFlagId] = p.[RequiredFeatureFlagId] WHERE f.[FlagKey] = 'CostRecovery.CalOesMars' AND r.[FlagKey] = 'Business.Operations') INSERT INTO [FeatureFlagPrerequisites] ([FeatureFlagId], [RequiredFeatureFlagId], [RequiredValue]) SELECT f.[FeatureFlagId], r.[FeatureFlagId], NULL FROM [FeatureFlags] f CROSS JOIN [FeatureFlags] r WHERE f.[FlagKey] = 'CostRecovery.CalOesMars' AND r.[FlagKey] = 'Business.Operations';");
		}

		public override void Down()
		{
			if (Schema.Table("CalOesMarsReimbursementLines").Exists()) Delete.Table("CalOesMarsReimbursementLines");
			if (Schema.Table("CalOesMarsWorkItems").Exists()) Delete.Table("CalOesMarsWorkItems");
			if (Schema.Table("CalOesMarsAgreementSnapshots").Exists()) Delete.Table("CalOesMarsAgreementSnapshots");
			if (Schema.Table("CalOesMarsAdministrativeRateInputs").Exists()) Delete.Table("CalOesMarsAdministrativeRateInputs");
			if (Schema.Table("CalOesMarsRateLines").Exists()) Delete.Table("CalOesMarsRateLines");
			if (Schema.Table("CalOesMarsRateProfiles").Exists()) Delete.Table("CalOesMarsRateProfiles");
			if (Schema.Table("CalOesMarsResourceProfiles").Exists()) Delete.Table("CalOesMarsResourceProfiles");
			if (Schema.Table("CalOesMarsAgencyProfiles").Exists()) Delete.Table("CalOesMarsAgencyProfiles");
			if (Schema.Table("InvoiceLineItems").Column("DeploymentTimeReportId").Exists()) Delete.Column("DeploymentTimeReportId").FromTable("InvoiceLineItems");
			// The filtered index depends on the column; SQL Server refuses the column drop while it exists.
			Execute.Sql("IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Invoices_Deployment' AND object_id = OBJECT_ID('Invoices')) DROP INDEX [IX_Invoices_Deployment] ON [Invoices];");
			if (Schema.Table("Invoices").Column("DeploymentId").Exists()) Delete.Column("DeploymentId").FromTable("Invoices");
			if (Schema.Table("Invoices").Column("ServiceContractId").Exists()) Delete.Column("ServiceContractId").FromTable("Invoices");
		}
	}
}
