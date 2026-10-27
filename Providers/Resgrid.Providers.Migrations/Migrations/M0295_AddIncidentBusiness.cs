using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Incident Back Office LI-C Business (plan §9.12.2–§9.13, §9.23, §11.4): vendor agreements and payment plans, requisitions
	/// and approvals, commitments, purchase receipts, invoice references and payment packages; budgets, lines, funding
	/// authorizations, spending delegations, funding codes and cost groups; time records with the DTR-import unique constraint,
	/// adjustments, equipment use and expenses; rate snapshots, the cost ledger, accrual snapshots, projections, cost-share
	/// agreements, rules, apportionment runs and allocations (ADP catalog 36). No foreign keys (the IC precedent): tenant
	/// ownership is enforced in the service layer and the department purge (IncidentBackOfficeDepartmentCleanup) removes every
	/// row. Existence-guarded for safe retry.
	/// </summary>
	[Migration(295)]
	public class M0295_AddIncidentBusiness : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("VendorAgreements").Exists())
			{
				Create.Table("VendorAgreements")
					.WithColumn("VendorAgreementId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("VendorId").AsString(36).NotNullable()
					.WithColumn("AgreementType").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("AgreementNumber").AsString(100).Nullable()
					.WithColumn("RecordId").AsString(36).Nullable()
					.WithColumn("EffectiveOn").AsDate().Nullable()
					.WithColumn("ExpiresOn").AsDate().Nullable()
					.WithColumn("CeilingAmount").AsDecimal(18, 2).Nullable()
					.WithColumn("Currency").AsString(3).NotNullable()
					.WithColumn("RateScheduleId").AsString(36).Nullable()
					.WithColumn("RatesJson").AsString(int.MaxValue).Nullable()
					.WithColumn("TaxesAndFees").AsString(1000).Nullable()
					.WithColumn("Minimums").AsString(1000).Nullable()
					.WithColumn("CancellationTerms").AsString(1000).Nullable()
					.WithColumn("OrderingInstructions").AsString(int.MaxValue).Nullable()
					.WithColumn("InspectionRules").AsString(1000).Nullable()
					.WithColumn("PaymentTerms").AsString(1000).Nullable()
					.WithColumn("InvoiceRoute").AsString(500).Nullable()
					.WithColumn("RequiredDocuments").AsString(1000).Nullable()
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_VendorAgreements_Department' AND object_id = OBJECT_ID('VendorAgreements')) CREATE NONCLUSTERED INDEX [IX_VendorAgreements_Department] ON [VendorAgreements] ([DepartmentId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_VendorAgreements_Vendor' AND object_id = OBJECT_ID('VendorAgreements')) CREATE NONCLUSTERED INDEX [IX_VendorAgreements_Vendor] ON [VendorAgreements] ([DepartmentId], [VendorId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_VendorAgreements_Expiry' AND object_id = OBJECT_ID('VendorAgreements')) CREATE NONCLUSTERED INDEX [IX_VendorAgreements_Expiry] ON [VendorAgreements] ([DepartmentId], [ExpiresOn]);");
			}

			if (!Schema.Table("VendorPaymentPlans").Exists())
			{
				Create.Table("VendorPaymentPlans")
					.WithColumn("VendorPaymentPlanId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("VendorAgreementId").AsString(36).Nullable()
					.WithColumn("VendorId").AsString(36).NotNullable()
					.WithColumn("PayingAgency").AsString(200).Nullable()
					.WithColumn("FundingSource").AsString(200).Nullable()
					.WithColumn("Method").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Terms").AsString(1000).Nullable()
					.WithColumn("PaymentOffice").AsString(200).Nullable()
					.WithColumn("RemittanceReference").AsString(int.MaxValue).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedCatalogVersion").AsInt32().Nullable()
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_VendorPaymentPlans_Department' AND object_id = OBJECT_ID('VendorPaymentPlans')) CREATE NONCLUSTERED INDEX [IX_VendorPaymentPlans_Department] ON [VendorPaymentPlans] ([DepartmentId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_VendorPaymentPlans_Vendor' AND object_id = OBJECT_ID('VendorPaymentPlans')) CREATE NONCLUSTERED INDEX [IX_VendorPaymentPlans_Vendor] ON [VendorPaymentPlans] ([DepartmentId], [VendorId]);");
			}

			if (!Schema.Table("PurchaseRequisitions").Exists())
			{
				Create.Table("PurchaseRequisitions")
					.WithColumn("PurchaseRequisitionId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("RequisitionNumber").AsString(32).NotNullable()
					.WithColumn("IncidentResourceRequestId").AsString(36).Nullable()
					.WithColumn("VendorId").AsString(36).Nullable()
					.WithColumn("IncidentVendorEngagementId").AsString(36).Nullable()
					.WithColumn("IncidentBudgetLineId").AsString(36).Nullable()
					.WithColumn("Method").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Description").AsString(1000).NotNullable()
					.WithColumn("EstimatedAmount").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
					.WithColumn("Currency").AsString(3).NotNullable()
					.WithColumn("Justification").AsString(int.MaxValue).Nullable()
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("RequestedByUserId").AsString(128).Nullable()
					.WithColumn("SubmittedOn").AsDateTime2().Nullable()
					.WithColumn("ApprovedOn").AsDateTime2().Nullable()
					.WithColumn("ApprovedByUserId").AsString(128).Nullable()
					.WithColumn("ConflictOfInterestAttested").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("IsSplitPurchaseWarning").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("OverrideReason").AsString(500).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PurchaseRequisitions_Activation' AND object_id = OBJECT_ID('PurchaseRequisitions')) CREATE NONCLUSTERED INDEX [IX_PurchaseRequisitions_Activation] ON [PurchaseRequisitions] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PurchaseRequisitions_Status' AND object_id = OBJECT_ID('PurchaseRequisitions')) CREATE NONCLUSTERED INDEX [IX_PurchaseRequisitions_Status] ON [PurchaseRequisitions] ([DepartmentId], [IncidentActivationId], [Status]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_PurchaseRequisitions_Number' AND object_id = OBJECT_ID('PurchaseRequisitions')) CREATE UNIQUE NONCLUSTERED INDEX [UX_PurchaseRequisitions_Number] ON [PurchaseRequisitions] ([DepartmentId], [RequisitionNumber]) WHERE [IsDeleted] = 0;");
			}

			if (!Schema.Table("PurchaseApprovals").Exists())
			{
				Create.Table("PurchaseApprovals")
					.WithColumn("PurchaseApprovalId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("PurchaseRequisitionId").AsString(36).NotNullable()
					.WithColumn("Step").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Decision").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ThresholdAmount").AsDecimal(18, 2).Nullable()
					.WithColumn("Comment").AsString(1000).Nullable()
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PurchaseApprovals_Activation' AND object_id = OBJECT_ID('PurchaseApprovals')) CREATE NONCLUSTERED INDEX [IX_PurchaseApprovals_Activation] ON [PurchaseApprovals] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PurchaseApprovals_Requisition' AND object_id = OBJECT_ID('PurchaseApprovals')) CREATE NONCLUSTERED INDEX [IX_PurchaseApprovals_Requisition] ON [PurchaseApprovals] ([DepartmentId], [PurchaseRequisitionId]);");
			}

			if (!Schema.Table("IncidentCommitments").Exists())
			{
				Create.Table("IncidentCommitments")
					.WithColumn("IncidentCommitmentId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("CommitmentNumber").AsString(32).NotNullable()
					.WithColumn("PurchaseRequisitionId").AsString(36).Nullable()
					.WithColumn("VendorId").AsString(36).Nullable()
					.WithColumn("IncidentVendorEngagementId").AsString(36).Nullable()
					.WithColumn("VendorAgreementId").AsString(36).Nullable()
					.WithColumn("IncidentBudgetLineId").AsString(36).Nullable()
					.WithColumn("IncidentFundingCodeId").AsString(36).Nullable()
					.WithColumn("Method").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Amount").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
					.WithColumn("Currency").AsString(3).NotNullable()
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("CommittedOn").AsDateTime2().NotNullable()
					.WithColumn("CommittedByUserId").AsString(128).Nullable()
					.WithColumn("InventoryPurchaseOrderId").AsString(36).Nullable()
					.WithColumn("Description").AsString(1000).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentCommitments_Activation' AND object_id = OBJECT_ID('IncidentCommitments')) CREATE NONCLUSTERED INDEX [IX_IncidentCommitments_Activation] ON [IncidentCommitments] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentCommitments_Status' AND object_id = OBJECT_ID('IncidentCommitments')) CREATE NONCLUSTERED INDEX [IX_IncidentCommitments_Status] ON [IncidentCommitments] ([DepartmentId], [IncidentActivationId], [Status]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_IncidentCommitments_Number' AND object_id = OBJECT_ID('IncidentCommitments')) CREATE UNIQUE NONCLUSTERED INDEX [UX_IncidentCommitments_Number] ON [IncidentCommitments] ([DepartmentId], [CommitmentNumber]) WHERE [IsDeleted] = 0;");
			}

			if (!Schema.Table("PurchaseReceipts").Exists())
			{
				Create.Table("PurchaseReceipts")
					.WithColumn("PurchaseReceiptId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("IncidentCommitmentId").AsString(36).NotNullable()
					.WithColumn("IncidentReceiptId").AsString(36).Nullable()
					.WithColumn("AcceptedOn").AsDateTime2().NotNullable()
					.WithColumn("AcceptedByUserId").AsString(128).Nullable()
					.WithColumn("Quantity").AsDecimal(18, 4).Nullable()
					.WithColumn("Amount").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
					.WithColumn("Currency").AsString(3).NotNullable()
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Notes").AsString(1000).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PurchaseReceipts_Activation' AND object_id = OBJECT_ID('PurchaseReceipts')) CREATE NONCLUSTERED INDEX [IX_PurchaseReceipts_Activation] ON [PurchaseReceipts] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PurchaseReceipts_Commitment' AND object_id = OBJECT_ID('PurchaseReceipts')) CREATE NONCLUSTERED INDEX [IX_PurchaseReceipts_Commitment] ON [PurchaseReceipts] ([DepartmentId], [IncidentCommitmentId]);");
			}

			if (!Schema.Table("VendorInvoiceReferences").Exists())
			{
				Create.Table("VendorInvoiceReferences")
					.WithColumn("VendorInvoiceReferenceId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("VendorId").AsString(36).NotNullable()
					.WithColumn("IncidentCommitmentId").AsString(36).Nullable()
					.WithColumn("InvoiceNumber").AsString(100).NotNullable()
					.WithColumn("InvoiceDate").AsDate().Nullable()
					.WithColumn("Amount").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
					.WithColumn("Currency").AsString(3).NotNullable()
					.WithColumn("ReceivedOn").AsDateTime2().NotNullable()
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("MatchVariance").AsDecimal(18, 2).Nullable()
					.WithColumn("ExceptionKind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("AcceptedByUserId").AsString(128).Nullable()
					.WithColumn("AcceptedOn").AsDateTime2().Nullable()
					.WithColumn("EvidenceArtifactId").AsString(36).Nullable()
					.WithColumn("Notes").AsString(1000).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_VendorInvoiceReferences_Activation' AND object_id = OBJECT_ID('VendorInvoiceReferences')) CREATE NONCLUSTERED INDEX [IX_VendorInvoiceReferences_Activation] ON [VendorInvoiceReferences] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_VendorInvoiceReferences_Vendor' AND object_id = OBJECT_ID('VendorInvoiceReferences')) CREATE NONCLUSTERED INDEX [IX_VendorInvoiceReferences_Vendor] ON [VendorInvoiceReferences] ([DepartmentId], [VendorId], [InvoiceNumber]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_VendorInvoiceReferences_Status' AND object_id = OBJECT_ID('VendorInvoiceReferences')) CREATE NONCLUSTERED INDEX [IX_VendorInvoiceReferences_Status] ON [VendorInvoiceReferences] ([DepartmentId], [IncidentActivationId], [Status]);");
			}

			if (!Schema.Table("PaymentPackages").Exists())
			{
				Create.Table("PaymentPackages")
					.WithColumn("PaymentPackageId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("PackageNumber").AsString(32).NotNullable()
					.WithColumn("VendorId").AsString(36).Nullable()
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("TotalAmount").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
					.WithColumn("Currency").AsString(3).NotNullable()
					.WithColumn("LineCount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("SubmittedOn").AsDateTime2().Nullable()
					.WithColumn("ExternalReference").AsString(200).Nullable()
					.WithColumn("PaidReference").AsString(200).Nullable()
					.WithColumn("PaidOn").AsDateTime2().Nullable()
					.WithColumn("EvidenceArtifactId").AsString(36).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PaymentPackages_Activation' AND object_id = OBJECT_ID('PaymentPackages')) CREATE NONCLUSTERED INDEX [IX_PaymentPackages_Activation] ON [PaymentPackages] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_PaymentPackages_Number' AND object_id = OBJECT_ID('PaymentPackages')) CREATE UNIQUE NONCLUSTERED INDEX [UX_PaymentPackages_Number] ON [PaymentPackages] ([DepartmentId], [PackageNumber]) WHERE [IsDeleted] = 0;");
			}

			if (!Schema.Table("PaymentPackageLines").Exists())
			{
				Create.Table("PaymentPackageLines")
					.WithColumn("PaymentPackageLineId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("PaymentPackageId").AsString(36).NotNullable()
					.WithColumn("VendorInvoiceReferenceId").AsString(36).NotNullable()
					.WithColumn("Amount").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
					.WithColumn("Currency").AsString(3).NotNullable()
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PaymentPackageLines_Activation' AND object_id = OBJECT_ID('PaymentPackageLines')) CREATE NONCLUSTERED INDEX [IX_PaymentPackageLines_Activation] ON [PaymentPackageLines] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PaymentPackageLines_Package' AND object_id = OBJECT_ID('PaymentPackageLines')) CREATE NONCLUSTERED INDEX [IX_PaymentPackageLines_Package] ON [PaymentPackageLines] ([DepartmentId], [PaymentPackageId]);");
			}

			if (!Schema.Table("IncidentBudgets").Exists())
			{
				Create.Table("IncidentBudgets")
					.WithColumn("IncidentBudgetId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("Name").AsString(200).NotNullable()
					.WithColumn("OriginalAmount").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
					.WithColumn("RevisedAmount").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
					.WithColumn("Currency").AsString(3).NotNullable()
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("RecordId").AsString(36).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentBudgets_Activation' AND object_id = OBJECT_ID('IncidentBudgets')) CREATE NONCLUSTERED INDEX [IX_IncidentBudgets_Activation] ON [IncidentBudgets] ([DepartmentId], [IncidentActivationId]);");
			}

			if (!Schema.Table("IncidentBudgetLines").Exists())
			{
				Create.Table("IncidentBudgetLines")
					.WithColumn("IncidentBudgetLineId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("IncidentBudgetId").AsString(36).NotNullable()
					.WithColumn("Name").AsString(200).NotNullable()
					.WithColumn("Kind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ScopeKey").AsString(64).Nullable()
					.WithColumn("IncidentFundingCodeId").AsString(36).Nullable()
					.WithColumn("OriginalAmount").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
					.WithColumn("RevisedAmount").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
					.WithColumn("Currency").AsString(3).NotNullable()
					.WithColumn("ThresholdPercent").AsDecimal(9, 4).Nullable()
					.WithColumn("FloorAmount").AsDecimal(18, 2).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentBudgetLines_Activation' AND object_id = OBJECT_ID('IncidentBudgetLines')) CREATE NONCLUSTERED INDEX [IX_IncidentBudgetLines_Activation] ON [IncidentBudgetLines] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentBudgetLines_Budget' AND object_id = OBJECT_ID('IncidentBudgetLines')) CREATE NONCLUSTERED INDEX [IX_IncidentBudgetLines_Budget] ON [IncidentBudgetLines] ([DepartmentId], [IncidentBudgetId]);");
			}

			if (!Schema.Table("IncidentFundingAuthorizations").Exists())
			{
				Create.Table("IncidentFundingAuthorizations")
					.WithColumn("IncidentFundingAuthorizationId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("IncidentBudgetLineId").AsString(36).Nullable()
					.WithColumn("FromBudgetLineId").AsString(36).Nullable()
					.WithColumn("Kind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Amount").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
					.WithColumn("Currency").AsString(3).NotNullable()
					.WithColumn("AuthorizedOn").AsDateTime2().NotNullable()
					.WithColumn("RecordId").AsString(36).Nullable()
					.WithColumn("Reason").AsString(1000).Nullable()
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentFundingAuthorizations_Activation' AND object_id = OBJECT_ID('IncidentFundingAuthorizations')) CREATE NONCLUSTERED INDEX [IX_IncidentFundingAuthorizations_Activation] ON [IncidentFundingAuthorizations] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentFundingAuthorizations_Line' AND object_id = OBJECT_ID('IncidentFundingAuthorizations')) CREATE NONCLUSTERED INDEX [IX_IncidentFundingAuthorizations_Line] ON [IncidentFundingAuthorizations] ([DepartmentId], [IncidentBudgetLineId]);");
			}

			if (!Schema.Table("IncidentSpendingDelegations").Exists())
			{
				Create.Table("IncidentSpendingDelegations")
					.WithColumn("IncidentSpendingDelegationId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("PositionCode").AsString(32).Nullable()
					.WithColumn("UserId").AsString(128).Nullable()
					.WithColumn("Action").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ThresholdAmount").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
					.WithColumn("Currency").AsString(3).NotNullable()
					.WithColumn("IncidentFundingCodeId").AsString(36).Nullable()
					.WithColumn("EffectiveOn").AsDateTime2().NotNullable()
					.WithColumn("ExpiresOn").AsDateTime2().Nullable()
					.WithColumn("RecordId").AsString(36).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentSpendingDelegations_Activation' AND object_id = OBJECT_ID('IncidentSpendingDelegations')) CREATE NONCLUSTERED INDEX [IX_IncidentSpendingDelegations_Activation] ON [IncidentSpendingDelegations] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentSpendingDelegations_User' AND object_id = OBJECT_ID('IncidentSpendingDelegations')) CREATE NONCLUSTERED INDEX [IX_IncidentSpendingDelegations_User] ON [IncidentSpendingDelegations] ([DepartmentId], [IncidentActivationId], [UserId]);");
			}

			if (!Schema.Table("IncidentFundingCodes").Exists())
			{
				Create.Table("IncidentFundingCodes")
					.WithColumn("IncidentFundingCodeId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("Code").AsString(100).NotNullable()
					.WithColumn("Kind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Description").AsString(500).Nullable()
					.WithColumn("PayingAgency").AsString(200).Nullable()
					.WithColumn("EffectiveOn").AsDate().Nullable()
					.WithColumn("ExpiresOn").AsDate().Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentFundingCodes_Activation' AND object_id = OBJECT_ID('IncidentFundingCodes')) CREATE NONCLUSTERED INDEX [IX_IncidentFundingCodes_Activation] ON [IncidentFundingCodes] ([DepartmentId], [IncidentActivationId]);");
			}

			if (!Schema.Table("IncidentCostGroups").Exists())
			{
				Create.Table("IncidentCostGroups")
					.WithColumn("IncidentCostGroupId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("Name").AsString(200).NotNullable()
					.WithColumn("Code").AsString(64).Nullable()
					.WithColumn("Description").AsString(500).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentCostGroups_Activation' AND object_id = OBJECT_ID('IncidentCostGroups')) CREATE NONCLUSTERED INDEX [IX_IncidentCostGroups_Activation] ON [IncidentCostGroups] ([DepartmentId], [IncidentActivationId]);");
			}

			if (!Schema.Table("IncidentTimeRecords").Exists())
			{
				Create.Table("IncidentTimeRecords")
					.WithColumn("IncidentTimeRecordId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("IncidentOperationalPeriodId").AsString(36).Nullable()
					.WithColumn("SubjectType").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("IncidentParticipantId").AsString(36).Nullable()
					.WithColumn("IncidentResourceId").AsString(36).Nullable()
					.WithColumn("IncidentPositionAssignmentId").AsString(36).Nullable()
					.WithColumn("IncidentRecoveryProjectId").AsString(36).Nullable()
					.WithColumn("MemberActivationId").AsString(36).Nullable()
					.WithColumn("EntryType").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("StartOn").AsDateTime2().NotNullable()
					.WithColumn("EndOn").AsDateTime2().NotNullable()
					.WithColumn("PaidBreakMinutes").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("UnpaidBreakMinutes").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("PaymentClass").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("RateSnapshotId").AsString(36).Nullable()
					.WithColumn("Currency").AsString(3).NotNullable()
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("IsSystemGenerated").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("CertifiedByUserId").AsString(128).Nullable()
					.WithColumn("CertifiedOn").AsDateTime2().Nullable()
					.WithColumn("ApprovedByUserId").AsString(128).Nullable()
					.WithColumn("ApprovedOn").AsDateTime2().Nullable()
					.WithColumn("AdjustsTimeRecordId").AsString(36).Nullable()
					.WithColumn("SourceType").AsString(32).Nullable()
					.WithColumn("SourceId").AsString(36).Nullable()
					.WithColumn("Remarks").AsString(int.MaxValue).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedCatalogVersion").AsInt32().Nullable()
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentTimeRecords_Activation' AND object_id = OBJECT_ID('IncidentTimeRecords')) CREATE NONCLUSTERED INDEX [IX_IncidentTimeRecords_Activation] ON [IncidentTimeRecords] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentTimeRecords_Period' AND object_id = OBJECT_ID('IncidentTimeRecords')) CREATE NONCLUSTERED INDEX [IX_IncidentTimeRecords_Period] ON [IncidentTimeRecords] ([DepartmentId], [IncidentActivationId], [IncidentOperationalPeriodId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentTimeRecords_Participant' AND object_id = OBJECT_ID('IncidentTimeRecords')) CREATE NONCLUSTERED INDEX [IX_IncidentTimeRecords_Participant] ON [IncidentTimeRecords] ([DepartmentId], [IncidentParticipantId], [StartOn]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentTimeRecords_Resource' AND object_id = OBJECT_ID('IncidentTimeRecords')) CREATE NONCLUSTERED INDEX [IX_IncidentTimeRecords_Resource] ON [IncidentTimeRecords] ([DepartmentId], [IncidentResourceId], [StartOn]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_IncidentTimeRecords_Source' AND object_id = OBJECT_ID('IncidentTimeRecords')) CREATE UNIQUE NONCLUSTERED INDEX [UX_IncidentTimeRecords_Source] ON [IncidentTimeRecords] ([DepartmentId], [SourceType], [SourceId]) WHERE [IsDeleted] = 0 AND [SourceType] IS NOT NULL AND [SourceId] IS NOT NULL;");
			}

			if (!Schema.Table("IncidentTimeAdjustments").Exists())
			{
				Create.Table("IncidentTimeAdjustments")
					.WithColumn("IncidentTimeAdjustmentId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("IncidentTimeRecordId").AsString(36).NotNullable()
					.WithColumn("AdjustingTimeRecordId").AsString(36).NotNullable()
					.WithColumn("Reason").AsString(1000).NotNullable()
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentTimeAdjustments_Activation' AND object_id = OBJECT_ID('IncidentTimeAdjustments')) CREATE NONCLUSTERED INDEX [IX_IncidentTimeAdjustments_Activation] ON [IncidentTimeAdjustments] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentTimeAdjustments_Record' AND object_id = OBJECT_ID('IncidentTimeAdjustments')) CREATE NONCLUSTERED INDEX [IX_IncidentTimeAdjustments_Record] ON [IncidentTimeAdjustments] ([DepartmentId], [IncidentTimeRecordId]);");
			}

			if (!Schema.Table("IncidentEquipmentUseRecords").Exists())
			{
				Create.Table("IncidentEquipmentUseRecords")
					.WithColumn("IncidentEquipmentUseRecordId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("IncidentOperationalPeriodId").AsString(36).Nullable()
					.WithColumn("IncidentResourceId").AsString(36).NotNullable()
					.WithColumn("IncidentVendorEngagementId").AsString(36).Nullable()
					.WithColumn("VendorAgreementId").AsString(36).Nullable()
					.WithColumn("ShiftDate").AsDate().NotNullable()
					.WithColumn("StartOn").AsDateTime2().Nullable()
					.WithColumn("EndOn").AsDateTime2().Nullable()
					.WithColumn("Hours").AsDecimal(18, 4).Nullable()
					.WithColumn("Mileage").AsDecimal(18, 4).Nullable()
					.WithColumn("UseUnit").AsString(16).Nullable()
					.WithColumn("Quantity").AsDecimal(18, 4).Nullable()
					.WithColumn("WorkPerformed").AsString(2000).Nullable()
					.WithColumn("FuelProvided").AsString(500).Nullable()
					.WithColumn("OperatorName").AsString(200).Nullable()
					.WithColumn("ShiftTicketRecordId").AsString(36).Nullable()
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("RateSnapshotId").AsString(36).Nullable()
					.WithColumn("Currency").AsString(3).NotNullable()
					.WithColumn("IncidentRecoveryProjectId").AsString(36).Nullable()
					.WithColumn("SourceType").AsString(32).Nullable()
					.WithColumn("SourceId").AsString(36).Nullable()
					.WithColumn("CertifiedByUserId").AsString(128).Nullable()
					.WithColumn("CertifiedOn").AsDateTime2().Nullable()
					.WithColumn("ApprovedByUserId").AsString(128).Nullable()
					.WithColumn("ApprovedOn").AsDateTime2().Nullable()
					.WithColumn("AdjustsRecordId").AsString(36).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentEquipmentUseRecords_Activation' AND object_id = OBJECT_ID('IncidentEquipmentUseRecords')) CREATE NONCLUSTERED INDEX [IX_IncidentEquipmentUseRecords_Activation] ON [IncidentEquipmentUseRecords] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentEquipmentUseRecords_Resource' AND object_id = OBJECT_ID('IncidentEquipmentUseRecords')) CREATE NONCLUSTERED INDEX [IX_IncidentEquipmentUseRecords_Resource] ON [IncidentEquipmentUseRecords] ([DepartmentId], [IncidentResourceId], [ShiftDate]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_IncidentEquipmentUseRecords_Source' AND object_id = OBJECT_ID('IncidentEquipmentUseRecords')) CREATE UNIQUE NONCLUSTERED INDEX [UX_IncidentEquipmentUseRecords_Source] ON [IncidentEquipmentUseRecords] ([DepartmentId], [SourceType], [SourceId]) WHERE [IsDeleted] = 0 AND [SourceType] IS NOT NULL AND [SourceId] IS NOT NULL;");
			}

			if (!Schema.Table("IncidentExpenses").Exists())
			{
				Create.Table("IncidentExpenses")
					.WithColumn("IncidentExpenseId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("IncidentOperationalPeriodId").AsString(36).Nullable()
					.WithColumn("SubjectType").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("SubjectId").AsString(36).Nullable()
					.WithColumn("Category").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("IncurredOn").AsDate().NotNullable()
					.WithColumn("Amount").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
					.WithColumn("Currency").AsString(3).NotNullable()
					.WithColumn("IsReimbursable").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("IsBillable").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("VendorId").AsString(36).Nullable()
					.WithColumn("EvidenceArtifactId").AsString(36).Nullable()
					.WithColumn("IncidentRecoveryProjectId").AsString(36).Nullable()
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ApprovedByUserId").AsString(128).Nullable()
					.WithColumn("ApprovedOn").AsDateTime2().Nullable()
					.WithColumn("Notes").AsString(int.MaxValue).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedCatalogVersion").AsInt32().Nullable()
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentExpenses_Activation' AND object_id = OBJECT_ID('IncidentExpenses')) CREATE NONCLUSTERED INDEX [IX_IncidentExpenses_Activation] ON [IncidentExpenses] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentExpenses_Day' AND object_id = OBJECT_ID('IncidentExpenses')) CREATE NONCLUSTERED INDEX [IX_IncidentExpenses_Day] ON [IncidentExpenses] ([DepartmentId], [IncidentActivationId], [IncurredOn]);");
			}

			if (!Schema.Table("IncidentRateSnapshots").Exists())
			{
				Create.Table("IncidentRateSnapshots")
					.WithColumn("IncidentRateSnapshotId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("SubjectKind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("SubjectId").AsString(36).Nullable()
					.WithColumn("RateScheduleId").AsString(36).Nullable()
					.WithColumn("RateScheduleEntryId").AsString(36).Nullable()
					.WithColumn("VendorAgreementId").AsString(36).Nullable()
					.WithColumn("Description").AsString(500).Nullable()
					.WithColumn("Amount").AsDecimal(18, 4).Nullable()
					.WithColumn("Unit").AsString(16).NotNullable()
					.WithColumn("Currency").AsString(3).NotNullable()
					.WithColumn("EffectiveOn").AsDate().Nullable()
					.WithColumn("RulesJson").AsString(int.MaxValue).Nullable()
					.WithColumn("PersonalAmount").AsString(int.MaxValue).Nullable()
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedCatalogVersion").AsInt32().Nullable()
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentRateSnapshots_Activation' AND object_id = OBJECT_ID('IncidentRateSnapshots')) CREATE NONCLUSTERED INDEX [IX_IncidentRateSnapshots_Activation] ON [IncidentRateSnapshots] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentRateSnapshots_Subject' AND object_id = OBJECT_ID('IncidentRateSnapshots')) CREATE NONCLUSTERED INDEX [IX_IncidentRateSnapshots_Subject] ON [IncidentRateSnapshots] ([DepartmentId], [SubjectId]);");
			}

			if (!Schema.Table("IncidentCostTransactions").Exists())
			{
				Create.Table("IncidentCostTransactions")
					.WithColumn("IncidentCostTransactionId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("IncidentOperationalPeriodId").AsString(36).Nullable()
					.WithColumn("Measure").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Category").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("SourceType").AsString(32).Nullable()
					.WithColumn("SourceId").AsString(36).Nullable()
					.WithColumn("IncidentResourceId").AsString(36).Nullable()
					.WithColumn("IncidentParticipantId").AsString(36).Nullable()
					.WithColumn("VendorId").AsString(36).Nullable()
					.WithColumn("IncidentBudgetLineId").AsString(36).Nullable()
					.WithColumn("IncidentFundingCodeId").AsString(36).Nullable()
					.WithColumn("IncidentCostGroupId").AsString(36).Nullable()
					.WithColumn("Section").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("IncidentFacilityId").AsString(36).Nullable()
					.WithColumn("Agency").AsString(200).Nullable()
					.WithColumn("IncidentRecoveryProjectId").AsString(36).Nullable()
					.WithColumn("MemberActivationId").AsString(36).Nullable()
					.WithColumn("CostDate").AsDate().NotNullable()
					.WithColumn("Quantity").AsDecimal(18, 4).Nullable()
					.WithColumn("Unit").AsString(16).Nullable()
					.WithColumn("Amount").AsDecimal(18, 2).Nullable()
					.WithColumn("Currency").AsString(3).NotNullable()
					.WithColumn("IsSystemGenerated").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("AdjustsTransactionId").AsString(36).Nullable()
					.WithColumn("RunId").AsString(36).Nullable()
					.WithColumn("ProtectedAmount").AsString(int.MaxValue).Nullable()
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedCatalogVersion").AsInt32().Nullable()
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentCostTransactions_Activation' AND object_id = OBJECT_ID('IncidentCostTransactions')) CREATE NONCLUSTERED INDEX [IX_IncidentCostTransactions_Activation] ON [IncidentCostTransactions] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentCostTransactions_Day' AND object_id = OBJECT_ID('IncidentCostTransactions')) CREATE NONCLUSTERED INDEX [IX_IncidentCostTransactions_Day] ON [IncidentCostTransactions] ([DepartmentId], [IncidentActivationId], [CostDate], [Measure]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentCostTransactions_Source' AND object_id = OBJECT_ID('IncidentCostTransactions')) CREATE NONCLUSTERED INDEX [IX_IncidentCostTransactions_Source] ON [IncidentCostTransactions] ([DepartmentId], [SourceType], [SourceId]);");
			}

			if (!Schema.Table("IncidentAccrualSnapshots").Exists())
			{
				Create.Table("IncidentAccrualSnapshots")
					.WithColumn("IncidentAccrualSnapshotId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("AsOfDate").AsDate().NotNullable()
					.WithColumn("Measure").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Category").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Amount").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
					.WithColumn("Currency").AsString(3).NotNullable()
					.WithColumn("SubjectCount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("RunId").AsString(36).Nullable()
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentAccrualSnapshots_Activation' AND object_id = OBJECT_ID('IncidentAccrualSnapshots')) CREATE NONCLUSTERED INDEX [IX_IncidentAccrualSnapshots_Activation] ON [IncidentAccrualSnapshots] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentAccrualSnapshots_Day' AND object_id = OBJECT_ID('IncidentAccrualSnapshots')) CREATE NONCLUSTERED INDEX [IX_IncidentAccrualSnapshots_Day] ON [IncidentAccrualSnapshots] ([DepartmentId], [IncidentActivationId], [AsOfDate]);");
			}

			if (!Schema.Table("IncidentCostProjections").Exists())
			{
				Create.Table("IncidentCostProjections")
					.WithColumn("IncidentCostProjectionId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("ScenarioName").AsString(200).NotNullable()
					.WithColumn("ProjectedEndOn").AsDate().Nullable()
					.WithColumn("DailyBurn").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
					.WithColumn("CumulativeToDate").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
					.WithColumn("ForecastAtCompletion").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
					.WithColumn("Currency").AsString(3).NotNullable()
					.WithColumn("Assumptions").AsString(2000).Nullable()
					.WithColumn("RunId").AsString(36).Nullable()
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentCostProjections_Activation' AND object_id = OBJECT_ID('IncidentCostProjections')) CREATE NONCLUSTERED INDEX [IX_IncidentCostProjections_Activation] ON [IncidentCostProjections] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentCostProjections_Recent' AND object_id = OBJECT_ID('IncidentCostProjections')) CREATE NONCLUSTERED INDEX [IX_IncidentCostProjections_Recent] ON [IncidentCostProjections] ([DepartmentId], [IncidentActivationId], [AddedOn]);");
			}

			if (!Schema.Table("IncidentCostShareAgreements").Exists())
			{
				Create.Table("IncidentCostShareAgreements")
					.WithColumn("IncidentCostShareAgreementId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("Name").AsString(200).NotNullable()
					.WithColumn("Version").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Basis").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("EffectiveFrom").AsDate().Nullable()
					.WithColumn("EffectiveTo").AsDate().Nullable()
					.WithColumn("RecordId").AsString(36).Nullable()
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("PartiesJson").AsString(int.MaxValue).Nullable()
					.WithColumn("SupersedesAgreementId").AsString(36).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentCostShareAgreements_Activation' AND object_id = OBJECT_ID('IncidentCostShareAgreements')) CREATE NONCLUSTERED INDEX [IX_IncidentCostShareAgreements_Activation] ON [IncidentCostShareAgreements] ([DepartmentId], [IncidentActivationId]);");
			}

			if (!Schema.Table("IncidentCostShareRules").Exists())
			{
				Create.Table("IncidentCostShareRules")
					.WithColumn("IncidentCostShareRuleId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("IncidentCostShareAgreementId").AsString(36).NotNullable()
					.WithColumn("RuleOrder").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ScopeKind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ScopeValue").AsString(100).Nullable()
					.WithColumn("Method").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ParametersJson").AsString(int.MaxValue).Nullable()
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentCostShareRules_Activation' AND object_id = OBJECT_ID('IncidentCostShareRules')) CREATE NONCLUSTERED INDEX [IX_IncidentCostShareRules_Activation] ON [IncidentCostShareRules] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentCostShareRules_Agreement' AND object_id = OBJECT_ID('IncidentCostShareRules')) CREATE NONCLUSTERED INDEX [IX_IncidentCostShareRules_Agreement] ON [IncidentCostShareRules] ([DepartmentId], [IncidentCostShareAgreementId], [RuleOrder]);");
			}

			if (!Schema.Table("IncidentApportionmentRuns").Exists())
			{
				Create.Table("IncidentApportionmentRuns")
					.WithColumn("IncidentApportionmentRunId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("IncidentCostShareAgreementId").AsString(36).NotNullable()
					.WithColumn("AgreementVersion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("RunKind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ThroughDate").AsDate().NotNullable()
					.WithColumn("InputChecksum").AsString(64).NotNullable()
					.WithColumn("OutputChecksum").AsString(64).NotNullable()
					.WithColumn("TotalApportioned").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
					.WithColumn("Currency").AsString(3).NotNullable()
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentApportionmentRuns_Activation' AND object_id = OBJECT_ID('IncidentApportionmentRuns')) CREATE NONCLUSTERED INDEX [IX_IncidentApportionmentRuns_Activation] ON [IncidentApportionmentRuns] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentApportionmentRuns_Agreement' AND object_id = OBJECT_ID('IncidentApportionmentRuns')) CREATE NONCLUSTERED INDEX [IX_IncidentApportionmentRuns_Agreement] ON [IncidentApportionmentRuns] ([DepartmentId], [IncidentCostShareAgreementId]);");
			}

			if (!Schema.Table("IncidentCostAllocations").Exists())
			{
				Create.Table("IncidentCostAllocations")
					.WithColumn("IncidentCostAllocationId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("IncidentApportionmentRunId").AsString(36).NotNullable()
					.WithColumn("IncidentCostShareRuleId").AsString(36).Nullable()
					.WithColumn("PartyKey").AsString(128).NotNullable()
					.WithColumn("PartyName").AsString(200).NotNullable()
					.WithColumn("Category").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Amount").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
					.WithColumn("Currency").AsString(3).NotNullable()
					.WithColumn("Method").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("AdjustsAllocationId").AsString(36).Nullable()
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentCostAllocations_Activation' AND object_id = OBJECT_ID('IncidentCostAllocations')) CREATE NONCLUSTERED INDEX [IX_IncidentCostAllocations_Activation] ON [IncidentCostAllocations] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentCostAllocations_Run' AND object_id = OBJECT_ID('IncidentCostAllocations')) CREATE NONCLUSTERED INDEX [IX_IncidentCostAllocations_Run] ON [IncidentCostAllocations] ([DepartmentId], [IncidentApportionmentRunId]);");
			}
		}

		public override void Down()
		{
			if (Schema.Table("IncidentCostAllocations").Exists())
				Delete.Table("IncidentCostAllocations");
			if (Schema.Table("IncidentApportionmentRuns").Exists())
				Delete.Table("IncidentApportionmentRuns");
			if (Schema.Table("IncidentCostShareRules").Exists())
				Delete.Table("IncidentCostShareRules");
			if (Schema.Table("IncidentCostShareAgreements").Exists())
				Delete.Table("IncidentCostShareAgreements");
			if (Schema.Table("IncidentCostProjections").Exists())
				Delete.Table("IncidentCostProjections");
			if (Schema.Table("IncidentAccrualSnapshots").Exists())
				Delete.Table("IncidentAccrualSnapshots");
			if (Schema.Table("IncidentCostTransactions").Exists())
				Delete.Table("IncidentCostTransactions");
			if (Schema.Table("IncidentRateSnapshots").Exists())
				Delete.Table("IncidentRateSnapshots");
			if (Schema.Table("IncidentExpenses").Exists())
				Delete.Table("IncidentExpenses");
			if (Schema.Table("IncidentEquipmentUseRecords").Exists())
				Delete.Table("IncidentEquipmentUseRecords");
			if (Schema.Table("IncidentTimeAdjustments").Exists())
				Delete.Table("IncidentTimeAdjustments");
			if (Schema.Table("IncidentTimeRecords").Exists())
				Delete.Table("IncidentTimeRecords");
			if (Schema.Table("IncidentCostGroups").Exists())
				Delete.Table("IncidentCostGroups");
			if (Schema.Table("IncidentFundingCodes").Exists())
				Delete.Table("IncidentFundingCodes");
			if (Schema.Table("IncidentSpendingDelegations").Exists())
				Delete.Table("IncidentSpendingDelegations");
			if (Schema.Table("IncidentFundingAuthorizations").Exists())
				Delete.Table("IncidentFundingAuthorizations");
			if (Schema.Table("IncidentBudgetLines").Exists())
				Delete.Table("IncidentBudgetLines");
			if (Schema.Table("IncidentBudgets").Exists())
				Delete.Table("IncidentBudgets");
			if (Schema.Table("PaymentPackageLines").Exists())
				Delete.Table("PaymentPackageLines");
			if (Schema.Table("PaymentPackages").Exists())
				Delete.Table("PaymentPackages");
			if (Schema.Table("VendorInvoiceReferences").Exists())
				Delete.Table("VendorInvoiceReferences");
			if (Schema.Table("PurchaseReceipts").Exists())
				Delete.Table("PurchaseReceipts");
			if (Schema.Table("IncidentCommitments").Exists())
				Delete.Table("IncidentCommitments");
			if (Schema.Table("PurchaseApprovals").Exists())
				Delete.Table("PurchaseApprovals");
			if (Schema.Table("PurchaseRequisitions").Exists())
				Delete.Table("PurchaseRequisitions");
			if (Schema.Table("VendorPaymentPlans").Exists())
				Delete.Table("VendorPaymentPlans");
			if (Schema.Table("VendorAgreements").Exists())
				Delete.Table("VendorAgreements");
		}
	}
}
