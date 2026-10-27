using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Incident Back Office LI-B Logistics, resources (plan §9.3–§9.5, §12.2): resources, components, status history,
	/// movements, timeout extensions, mission packages, requests with approvals, fills and tracked ETA updates, receipts,
	/// check-ins and demob gates; plus the three additive ResourceOrders columns (IncidentActivationId,
	/// IncidentResourceRequestId, OriginSystem). No foreign keys (the IC precedent): tenant ownership is enforced in the
	/// service layer and the department purge (IncidentBackOfficeDepartmentCleanup) removes every row. Existence-guarded for
	/// safe retry.
	/// </summary>
	[Migration(292)]
	public class M0292_AddIncidentLogisticsResources : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("IncidentResources").Exists())
			{
				Create.Table("IncidentResources")
					.WithColumn("IncidentResourceId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("Kind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Name").AsString(200).NotNullable()
					.WithColumn("ResourceType").AsString(100).Nullable()
					.WithColumn("Capability").AsString(200).Nullable()
					.WithColumn("TypingCode").AsString(64).Nullable()
					.WithColumn("ProviderAgency").AsString(200).Nullable()
					.WithColumn("PayingAgency").AsString(200).Nullable()
					.WithColumn("OrderingAuthority").AsString(200).Nullable()
					.WithColumn("OrderNumber").AsString(100).Nullable()
					.WithColumn("RequestNumber").AsString(100).Nullable()
					.WithColumn("HomeLocation").AsString(200).Nullable()
					.WithColumn("LeaderName").AsString(200).Nullable()
					.WithColumn("LeaderParticipantId").AsString(36).Nullable()
					.WithColumn("PersonnelCount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ApparatusCount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("UnitId").AsInt32().Nullable()
					.WithColumn("IncidentAdHocUnitId").AsString(128).Nullable()
					.WithColumn("IncidentRequestFillId").AsString(36).Nullable()
					.WithColumn("IncidentMissionPackageId").AsString(36).Nullable()
					.WithColumn("IncidentVendorEngagementId").AsString(36).Nullable()
					.WithColumn("SupportStatus").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("CurrentResourceAssignmentId").AsString(128).Nullable()
					.WithColumn("PaymentClass").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ArrivedOn").AsDateTime2().Nullable()
					.WithColumn("CheckedInOn").AsDateTime2().Nullable()
					.WithColumn("AssignmentStartOn").AsDate().Nullable()
					.WithColumn("MaxAssignmentDays").AsInt32().Nullable()
					.WithColumn("TimeoutOn").AsDate().Nullable()
					.WithColumn("TimeoutBasis").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("TentativeReleaseOn").AsDateTime2().Nullable()
					.WithColumn("ReleasedOn").AsDateTime2().Nullable()
					.WithColumn("DepartedOn").AsDateTime2().Nullable()
					.WithColumn("Destination").AsString(200).Nullable()
					.WithColumn("Notes").AsString(int.MaxValue).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentResources_Activation' AND object_id = OBJECT_ID('IncidentResources')) CREATE NONCLUSTERED INDEX [IX_IncidentResources_Activation] ON [IncidentResources] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentResources_Status' AND object_id = OBJECT_ID('IncidentResources')) CREATE NONCLUSTERED INDEX [IX_IncidentResources_Status] ON [IncidentResources] ([DepartmentId], [IncidentActivationId], [SupportStatus]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentResources_Timeout' AND object_id = OBJECT_ID('IncidentResources')) CREATE NONCLUSTERED INDEX [IX_IncidentResources_Timeout] ON [IncidentResources] ([DepartmentId], [IncidentActivationId], [TimeoutOn]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_IncidentResources_AdHoc' AND object_id = OBJECT_ID('IncidentResources')) CREATE UNIQUE NONCLUSTERED INDEX [UX_IncidentResources_AdHoc] ON [IncidentResources] ([DepartmentId], [IncidentActivationId], [IncidentAdHocUnitId]) WHERE [IsDeleted] = 0 AND [IncidentAdHocUnitId] IS NOT NULL;");
			}

			if (!Schema.Table("IncidentResourceComponents").Exists())
			{
				Create.Table("IncidentResourceComponents")
					.WithColumn("IncidentResourceComponentId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("IncidentResourceId").AsString(36).NotNullable()
					.WithColumn("ComponentResourceId").AsString(36).Nullable()
					.WithColumn("IncidentParticipantId").AsString(36).Nullable()
					.WithColumn("Kind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Name").AsString(200).NotNullable()
					.WithColumn("Quantity").AsDecimal(18, 4).NotNullable().WithDefaultValue(0)
					.WithColumn("Notes").AsString(int.MaxValue).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentResourceComponents_Activation' AND object_id = OBJECT_ID('IncidentResourceComponents')) CREATE NONCLUSTERED INDEX [IX_IncidentResourceComponents_Activation] ON [IncidentResourceComponents] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentResourceComponents_Resource' AND object_id = OBJECT_ID('IncidentResourceComponents')) CREATE NONCLUSTERED INDEX [IX_IncidentResourceComponents_Resource] ON [IncidentResourceComponents] ([DepartmentId], [IncidentResourceId]);");
			}

			if (!Schema.Table("IncidentResourceStatusHistory").Exists())
			{
				Create.Table("IncidentResourceStatusHistory")
					.WithColumn("IncidentResourceStatusHistoryId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("SubjectType").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("SubjectId").AsString(36).NotNullable()
					.WithColumn("OldStatus").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("NewStatus").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Reason").AsString(500).Nullable()
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentResourceStatusHistory_Activation' AND object_id = OBJECT_ID('IncidentResourceStatusHistory')) CREATE NONCLUSTERED INDEX [IX_IncidentResourceStatusHistory_Activation] ON [IncidentResourceStatusHistory] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentResourceStatusHistory_Subject' AND object_id = OBJECT_ID('IncidentResourceStatusHistory')) CREATE NONCLUSTERED INDEX [IX_IncidentResourceStatusHistory_Subject] ON [IncidentResourceStatusHistory] ([DepartmentId], [SubjectId], [AddedOn]);");
			}

			if (!Schema.Table("IncidentResourceMovements").Exists())
			{
				Create.Table("IncidentResourceMovements")
					.WithColumn("IncidentResourceMovementId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("IncidentResourceId").AsString(36).NotNullable()
					.WithColumn("Kind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("FromLocation").AsString(200).Nullable()
					.WithColumn("ToLocation").AsString(200).Nullable()
					.WithColumn("DepartedOn").AsDateTime2().Nullable()
					.WithColumn("ArrivedOn").AsDateTime2().Nullable()
					.WithColumn("EtaOn").AsDateTime2().Nullable()
					.WithColumn("Notes").AsString(int.MaxValue).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentResourceMovements_Activation' AND object_id = OBJECT_ID('IncidentResourceMovements')) CREATE NONCLUSTERED INDEX [IX_IncidentResourceMovements_Activation] ON [IncidentResourceMovements] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentResourceMovements_Resource' AND object_id = OBJECT_ID('IncidentResourceMovements')) CREATE NONCLUSTERED INDEX [IX_IncidentResourceMovements_Resource] ON [IncidentResourceMovements] ([DepartmentId], [IncidentResourceId]);");
			}

			if (!Schema.Table("IncidentAssignmentExtensions").Exists())
			{
				Create.Table("IncidentAssignmentExtensions")
					.WithColumn("IncidentAssignmentExtensionId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("SubjectType").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("SubjectId").AsString(36).NotNullable()
					.WithColumn("PriorTimeoutOn").AsDate().Nullable()
					.WithColumn("NewTimeoutOn").AsDate().NotNullable()
					.WithColumn("ApprovedByUserId").AsString(128).Nullable()
					.WithColumn("Reason").AsString(500).NotNullable()
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentAssignmentExtensions_Activation' AND object_id = OBJECT_ID('IncidentAssignmentExtensions')) CREATE NONCLUSTERED INDEX [IX_IncidentAssignmentExtensions_Activation] ON [IncidentAssignmentExtensions] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentAssignmentExtensions_Subject' AND object_id = OBJECT_ID('IncidentAssignmentExtensions')) CREATE NONCLUSTERED INDEX [IX_IncidentAssignmentExtensions_Subject] ON [IncidentAssignmentExtensions] ([DepartmentId], [SubjectId]);");
			}

			if (!Schema.Table("IncidentMissionPackages").Exists())
			{
				Create.Table("IncidentMissionPackages")
					.WithColumn("IncidentMissionPackageId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).Nullable()
					.WithColumn("Name").AsString(200).NotNullable()
					.WithColumn("Code").AsString(64).Nullable()
					.WithColumn("Kind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Description").AsString(int.MaxValue).Nullable()
					.WithColumn("ComponentsJson").AsString(int.MaxValue).Nullable()
					.WithColumn("IsTemplate").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("SourceTemplateId").AsString(36).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentMissionPackages_Department' AND object_id = OBJECT_ID('IncidentMissionPackages')) CREATE NONCLUSTERED INDEX [IX_IncidentMissionPackages_Department] ON [IncidentMissionPackages] ([DepartmentId]);");
			}

			if (!Schema.Table("IncidentMissionPackageEstimates").Exists())
			{
				Create.Table("IncidentMissionPackageEstimates")
					.WithColumn("IncidentMissionPackageEstimateId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentMissionPackageId").AsString(36).NotNullable()
					.WithColumn("Category").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Quantity").AsDecimal(18, 4).NotNullable().WithDefaultValue(0)
					.WithColumn("Unit").AsString(16).Nullable()
					.WithColumn("Amount").AsDecimal(18, 2).Nullable()
					.WithColumn("Currency").AsString(3).Nullable()
					.WithColumn("Notes").AsString(500).Nullable()
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentMissionPackageEstimates_Department' AND object_id = OBJECT_ID('IncidentMissionPackageEstimates')) CREATE NONCLUSTERED INDEX [IX_IncidentMissionPackageEstimates_Department] ON [IncidentMissionPackageEstimates] ([DepartmentId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentMissionPackageEstimates_Package' AND object_id = OBJECT_ID('IncidentMissionPackageEstimates')) CREATE NONCLUSTERED INDEX [IX_IncidentMissionPackageEstimates_Package] ON [IncidentMissionPackageEstimates] ([DepartmentId], [IncidentMissionPackageId]);");
			}

			if (!Schema.Table("IncidentResourceRequests").Exists())
			{
				Create.Table("IncidentResourceRequests")
					.WithColumn("IncidentResourceRequestId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("IncidentOperationalPeriodId").AsString(36).Nullable()
					.WithColumn("RequestNumber").AsString(32).NotNullable()
					.WithColumn("IsLocallyIssued").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("IncidentNeedId").AsString(128).Nullable()
					.WithColumn("IncidentObjectiveId").AsString(36).Nullable()
					.WithColumn("Priority").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("SubStateDetailId").AsInt32().Nullable()
					.WithColumn("Kind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ResourceType").AsString(100).Nullable()
					.WithColumn("Capability").AsString(200).Nullable()
					.WithColumn("Quantity").AsDecimal(18, 4).NotNullable().WithDefaultValue(0)
					.WithColumn("UnitOfMeasure").AsString(16).Nullable()
					.WithColumn("Substitutions").AsString(500).Nullable()
					.WithColumn("Justification").AsString(int.MaxValue).Nullable()
					.WithColumn("NeedBy").AsDateTime2().Nullable()
					.WithColumn("DeliveryLocation").AsString(200).Nullable()
					.WithColumn("ReportingLocation").AsString(200).Nullable()
					.WithColumn("ReceivingContact").AsString(200).Nullable()
					.WithColumn("FundingCode").AsString(100).Nullable()
					.WithColumn("IncidentBudgetLineId").AsString(36).Nullable()
					.WithColumn("OwningSection").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("SourceChannel").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("RequestedByUserId").AsString(128).Nullable()
					.WithColumn("SubmittedOn").AsDateTime2().Nullable()
					.WithColumn("ApprovedOn").AsDateTime2().Nullable()
					.WithColumn("ApprovedByUserId").AsString(128).Nullable()
					.WithColumn("OrderedOn").AsDateTime2().Nullable()
					.WithColumn("OrderedByUserId").AsString(128).Nullable()
					.WithColumn("RejectedReason").AsString(500).Nullable()
					.WithColumn("CancelledReason").AsString(500).Nullable()
					.WithColumn("ClosedOn").AsDateTime2().Nullable()
					.WithColumn("ResourceOrderId").AsInt32().Nullable()
					.WithColumn("IncidentVendorEngagementId").AsString(36).Nullable()
					.WithColumn("Notes").AsString(int.MaxValue).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentResourceRequests_Activation' AND object_id = OBJECT_ID('IncidentResourceRequests')) CREATE NONCLUSTERED INDEX [IX_IncidentResourceRequests_Activation] ON [IncidentResourceRequests] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentResourceRequests_Status' AND object_id = OBJECT_ID('IncidentResourceRequests')) CREATE NONCLUSTERED INDEX [IX_IncidentResourceRequests_Status] ON [IncidentResourceRequests] ([DepartmentId], [IncidentActivationId], [Status]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentResourceRequests_Need' AND object_id = OBJECT_ID('IncidentResourceRequests')) CREATE NONCLUSTERED INDEX [IX_IncidentResourceRequests_Need] ON [IncidentResourceRequests] ([DepartmentId], [IncidentNeedId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_IncidentResourceRequests_Number' AND object_id = OBJECT_ID('IncidentResourceRequests')) CREATE UNIQUE NONCLUSTERED INDEX [UX_IncidentResourceRequests_Number] ON [IncidentResourceRequests] ([DepartmentId], [RequestNumber]) WHERE [IsDeleted] = 0;");
			}

			if (!Schema.Table("IncidentRequestApprovals").Exists())
			{
				Create.Table("IncidentRequestApprovals")
					.WithColumn("IncidentRequestApprovalId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("IncidentResourceRequestId").AsString(36).NotNullable()
					.WithColumn("Step").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Decision").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Comment").AsString(1000).Nullable()
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentRequestApprovals_Activation' AND object_id = OBJECT_ID('IncidentRequestApprovals')) CREATE NONCLUSTERED INDEX [IX_IncidentRequestApprovals_Activation] ON [IncidentRequestApprovals] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentRequestApprovals_Request' AND object_id = OBJECT_ID('IncidentRequestApprovals')) CREATE NONCLUSTERED INDEX [IX_IncidentRequestApprovals_Request] ON [IncidentRequestApprovals] ([DepartmentId], [IncidentResourceRequestId]);");
			}

			if (!Schema.Table("IncidentRequestFills").Exists())
			{
				Create.Table("IncidentRequestFills")
					.WithColumn("IncidentRequestFillId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("IncidentResourceRequestId").AsString(36).NotNullable()
					.WithColumn("Source").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("SourceReference").AsString(100).Nullable()
					.WithColumn("Provider").AsString(200).Nullable()
					.WithColumn("ResourceKind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ResourceType").AsString(100).Nullable()
					.WithColumn("ApparatusCount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("PersonnelCount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Quantity").AsDecimal(18, 4).NotNullable().WithDefaultValue(0)
					.WithColumn("EtaOn").AsDateTime2().Nullable()
					.WithColumn("EtaConfidence").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("AcceptedOn").AsDateTime2().Nullable()
					.WithColumn("ArrivedOn").AsDateTime2().Nullable()
					.WithColumn("ResourceOrderFillId").AsInt32().Nullable()
					.WithColumn("Notes").AsString(int.MaxValue).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentRequestFills_Activation' AND object_id = OBJECT_ID('IncidentRequestFills')) CREATE NONCLUSTERED INDEX [IX_IncidentRequestFills_Activation] ON [IncidentRequestFills] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentRequestFills_Request' AND object_id = OBJECT_ID('IncidentRequestFills')) CREATE NONCLUSTERED INDEX [IX_IncidentRequestFills_Request] ON [IncidentRequestFills] ([DepartmentId], [IncidentResourceRequestId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentRequestFills_Eta' AND object_id = OBJECT_ID('IncidentRequestFills')) CREATE NONCLUSTERED INDEX [IX_IncidentRequestFills_Eta] ON [IncidentRequestFills] ([DepartmentId], [IncidentActivationId], [EtaOn]);");
			}

			if (!Schema.Table("IncidentFillEtaUpdates").Exists())
			{
				Create.Table("IncidentFillEtaUpdates")
					.WithColumn("IncidentFillEtaUpdateId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("IncidentRequestFillId").AsString(36).NotNullable()
					.WithColumn("PriorEtaOn").AsDateTime2().Nullable()
					.WithColumn("NewEtaOn").AsDateTime2().Nullable()
					.WithColumn("Confidence").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Reason").AsString(500).Nullable()
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentFillEtaUpdates_Activation' AND object_id = OBJECT_ID('IncidentFillEtaUpdates')) CREATE NONCLUSTERED INDEX [IX_IncidentFillEtaUpdates_Activation] ON [IncidentFillEtaUpdates] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentFillEtaUpdates_Fill' AND object_id = OBJECT_ID('IncidentFillEtaUpdates')) CREATE NONCLUSTERED INDEX [IX_IncidentFillEtaUpdates_Fill] ON [IncidentFillEtaUpdates] ([DepartmentId], [IncidentRequestFillId]);");
			}

			if (!Schema.Table("IncidentReceipts").Exists())
			{
				Create.Table("IncidentReceipts")
					.WithColumn("IncidentReceiptId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("IncidentResourceRequestId").AsString(36).Nullable()
					.WithColumn("IncidentRequestFillId").AsString(36).Nullable()
					.WithColumn("ReceivingLocation").AsString(200).Nullable()
					.WithColumn("ShipmentReference").AsString(100).Nullable()
					.WithColumn("Carrier").AsString(200).Nullable()
					.WithColumn("ReceivedOn").AsDateTime2().NotNullable()
					.WithColumn("ReceivedByUserId").AsString(128).Nullable()
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("DiscrepancyKind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Notes").AsString(int.MaxValue).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentReceipts_Activation' AND object_id = OBJECT_ID('IncidentReceipts')) CREATE NONCLUSTERED INDEX [IX_IncidentReceipts_Activation] ON [IncidentReceipts] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentReceipts_Request' AND object_id = OBJECT_ID('IncidentReceipts')) CREATE NONCLUSTERED INDEX [IX_IncidentReceipts_Request] ON [IncidentReceipts] ([DepartmentId], [IncidentResourceRequestId]);");
			}

			if (!Schema.Table("IncidentReceiptLines").Exists())
			{
				Create.Table("IncidentReceiptLines")
					.WithColumn("IncidentReceiptLineId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("IncidentReceiptId").AsString(36).NotNullable()
					.WithColumn("ItemDescription").AsString(500).NotNullable()
					.WithColumn("InventoryItemId").AsString(36).Nullable()
					.WithColumn("InventoryTransactionId").AsString(36).Nullable()
					.WithColumn("QuantityExpected").AsDecimal(18, 4).NotNullable().WithDefaultValue(0)
					.WithColumn("QuantityAccepted").AsDecimal(18, 4).NotNullable().WithDefaultValue(0)
					.WithColumn("QuantityRejected").AsDecimal(18, 4).NotNullable().WithDefaultValue(0)
					.WithColumn("QuantityBackordered").AsDecimal(18, 4).NotNullable().WithDefaultValue(0)
					.WithColumn("UnitOfMeasure").AsString(16).Nullable()
					.WithColumn("Damage").AsString(1000).Nullable()
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentReceiptLines_Activation' AND object_id = OBJECT_ID('IncidentReceiptLines')) CREATE NONCLUSTERED INDEX [IX_IncidentReceiptLines_Activation] ON [IncidentReceiptLines] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentReceiptLines_Receipt' AND object_id = OBJECT_ID('IncidentReceiptLines')) CREATE NONCLUSTERED INDEX [IX_IncidentReceiptLines_Receipt] ON [IncidentReceiptLines] ([DepartmentId], [IncidentReceiptId]);");
			}

			if (!Schema.Table("IncidentCheckIns").Exists())
			{
				Create.Table("IncidentCheckIns")
					.WithColumn("IncidentCheckInId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("SubjectType").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("SubjectId").AsString(36).NotNullable()
					.WithColumn("Source").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("CheckedInOn").AsDateTime2().NotNullable()
					.WithColumn("MobilizationOrigin").AsString(200).Nullable()
					.WithColumn("ActualPersonnelCount").AsInt32().Nullable()
					.WithColumn("VehicleDescription").AsString(200).Nullable()
					.WithColumn("ReportingPoint").AsString(200).Nullable()
					.WithColumn("FirstWorkDay").AsDate().Nullable()
					.WithColumn("AssignmentLengthDays").AsInt32().Nullable()
					.WithColumn("SpecialRequirements").AsString(1000).Nullable()
					.WithColumn("IsDuplicateCandidate").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("Notes").AsString(int.MaxValue).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentCheckIns_Activation' AND object_id = OBJECT_ID('IncidentCheckIns')) CREATE NONCLUSTERED INDEX [IX_IncidentCheckIns_Activation] ON [IncidentCheckIns] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentCheckIns_Subject' AND object_id = OBJECT_ID('IncidentCheckIns')) CREATE NONCLUSTERED INDEX [IX_IncidentCheckIns_Subject] ON [IncidentCheckIns] ([DepartmentId], [SubjectId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentCheckIns_Day' AND object_id = OBJECT_ID('IncidentCheckIns')) CREATE NONCLUSTERED INDEX [IX_IncidentCheckIns_Day] ON [IncidentCheckIns] ([DepartmentId], [IncidentActivationId], [CheckedInOn]);");
			}

			if (!Schema.Table("IncidentDemobPlans").Exists())
			{
				Create.Table("IncidentDemobPlans")
					.WithColumn("IncidentDemobPlanId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("SubjectType").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("SubjectId").AsString(36).NotNullable()
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("TentativeReleaseOn").AsDateTime2().Nullable()
					.WithColumn("ApprovedReleaseOn").AsDateTime2().Nullable()
					.WithColumn("ApprovedByUserId").AsString(128).Nullable()
					.WithColumn("DepartureWindowStart").AsDateTime2().Nullable()
					.WithColumn("DepartureWindowEnd").AsDateTime2().Nullable()
					.WithColumn("Destination").AsString(200).Nullable()
					.WithColumn("TravelMode").AsString(64).Nullable()
					.WithColumn("ReassignmentEligible").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("DepartedOn").AsDateTime2().Nullable()
					.WithColumn("Notes").AsString(int.MaxValue).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentDemobPlans_Activation' AND object_id = OBJECT_ID('IncidentDemobPlans')) CREATE NONCLUSTERED INDEX [IX_IncidentDemobPlans_Activation] ON [IncidentDemobPlans] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentDemobPlans_Status' AND object_id = OBJECT_ID('IncidentDemobPlans')) CREATE NONCLUSTERED INDEX [IX_IncidentDemobPlans_Status] ON [IncidentDemobPlans] ([DepartmentId], [IncidentActivationId], [Status]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentDemobPlans_Subject' AND object_id = OBJECT_ID('IncidentDemobPlans')) CREATE NONCLUSTERED INDEX [IX_IncidentDemobPlans_Subject] ON [IncidentDemobPlans] ([DepartmentId], [SubjectId]);");
			}

			if (!Schema.Table("IncidentDemobChecklistItems").Exists())
			{
				Create.Table("IncidentDemobChecklistItems")
					.WithColumn("IncidentDemobChecklistItemId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("IncidentDemobPlanId").AsString(36).NotNullable()
					.WithColumn("Category").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Name").AsString(200).NotNullable()
					.WithColumn("OwnerPosition").AsString(32).Nullable()
					.WithColumn("IsBlocking").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("CompletedOn").AsDateTime2().Nullable()
					.WithColumn("CompletedByUserId").AsString(128).Nullable()
					.WithColumn("WaivedReason").AsString(500).Nullable()
					.WithColumn("WaivedByUserId").AsString(128).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentDemobChecklistItems_Activation' AND object_id = OBJECT_ID('IncidentDemobChecklistItems')) CREATE NONCLUSTERED INDEX [IX_IncidentDemobChecklistItems_Activation] ON [IncidentDemobChecklistItems] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentDemobChecklistItems_Plan' AND object_id = OBJECT_ID('IncidentDemobChecklistItems')) CREATE NONCLUSTERED INDEX [IX_IncidentDemobChecklistItems_Plan] ON [IncidentDemobChecklistItems] ([DepartmentId], [IncidentDemobPlanId]);");
			}

			// Plan §12.2: the only change to shipped structure. Nullable / defaulted, so no existing row changes and the Orders UI ignores them.
			if (!Schema.Table("ResourceOrders").Column("IncidentActivationId").Exists())
				Alter.Table("ResourceOrders").AddColumn("IncidentActivationId").AsString(36).Nullable();
			if (!Schema.Table("ResourceOrders").Column("IncidentResourceRequestId").Exists())
				Alter.Table("ResourceOrders").AddColumn("IncidentResourceRequestId").AsString(36).Nullable();
			if (!Schema.Table("ResourceOrders").Column("OriginSystem").Exists())
				Alter.Table("ResourceOrders").AddColumn("OriginSystem").AsInt32().NotNullable().WithDefaultValue(0);
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ResourceOrders_IncidentActivation' AND object_id = OBJECT_ID('ResourceOrders')) CREATE NONCLUSTERED INDEX [IX_ResourceOrders_IncidentActivation] ON [ResourceOrders] ([DepartmentId], [IncidentActivationId]) WHERE [IncidentActivationId] IS NOT NULL;");
		}

		public override void Down()
		{
			Execute.Sql("IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ResourceOrders_IncidentActivation' AND object_id = OBJECT_ID('ResourceOrders')) DROP INDEX [IX_ResourceOrders_IncidentActivation] ON [ResourceOrders];");
			if (Schema.Table("ResourceOrders").Column("OriginSystem").Exists())
				Execute.Sql("DECLARE @df sysname = (SELECT d.name FROM sys.default_constraints d JOIN sys.columns c ON c.default_object_id = d.object_id WHERE d.parent_object_id = OBJECT_ID('ResourceOrders') AND c.name = 'OriginSystem'); IF @df IS NOT NULL EXEC('ALTER TABLE [ResourceOrders] DROP CONSTRAINT [' + @df + ']'); ALTER TABLE [ResourceOrders] DROP COLUMN [OriginSystem];");
			if (Schema.Table("ResourceOrders").Column("IncidentResourceRequestId").Exists())
				Delete.Column("IncidentResourceRequestId").FromTable("ResourceOrders");
			if (Schema.Table("ResourceOrders").Column("IncidentActivationId").Exists())
				Delete.Column("IncidentActivationId").FromTable("ResourceOrders");
			if (Schema.Table("IncidentDemobChecklistItems").Exists())
				Delete.Table("IncidentDemobChecklistItems");
			if (Schema.Table("IncidentDemobPlans").Exists())
				Delete.Table("IncidentDemobPlans");
			if (Schema.Table("IncidentCheckIns").Exists())
				Delete.Table("IncidentCheckIns");
			if (Schema.Table("IncidentReceiptLines").Exists())
				Delete.Table("IncidentReceiptLines");
			if (Schema.Table("IncidentReceipts").Exists())
				Delete.Table("IncidentReceipts");
			if (Schema.Table("IncidentFillEtaUpdates").Exists())
				Delete.Table("IncidentFillEtaUpdates");
			if (Schema.Table("IncidentRequestFills").Exists())
				Delete.Table("IncidentRequestFills");
			if (Schema.Table("IncidentRequestApprovals").Exists())
				Delete.Table("IncidentRequestApprovals");
			if (Schema.Table("IncidentResourceRequests").Exists())
				Delete.Table("IncidentResourceRequests");
			if (Schema.Table("IncidentMissionPackageEstimates").Exists())
				Delete.Table("IncidentMissionPackageEstimates");
			if (Schema.Table("IncidentMissionPackages").Exists())
				Delete.Table("IncidentMissionPackages");
			if (Schema.Table("IncidentAssignmentExtensions").Exists())
				Delete.Table("IncidentAssignmentExtensions");
			if (Schema.Table("IncidentResourceMovements").Exists())
				Delete.Table("IncidentResourceMovements");
			if (Schema.Table("IncidentResourceStatusHistory").Exists())
				Delete.Table("IncidentResourceStatusHistory");
			if (Schema.Table("IncidentResourceComponents").Exists())
				Delete.Table("IncidentResourceComponents");
			if (Schema.Table("IncidentResources").Exists())
				Delete.Table("IncidentResources");
		}
	}
}
