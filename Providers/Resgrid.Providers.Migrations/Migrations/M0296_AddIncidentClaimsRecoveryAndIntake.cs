using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Incident Back Office LI-C Business, continued (plan §9.14, §9.16, §9.26, §9.27): the claim case ledger, the closeout
	/// gate, disaster declarations, recovery projects and deadlines, and the evidence intake inbox with its files (ADP catalog
	/// 36). No foreign keys (the IC precedent): tenant ownership is enforced in the service layer and the department purge
	/// (IncidentBackOfficeDepartmentCleanup) removes every row. Existence-guarded for safe retry.
	/// </summary>
	[Migration(296)]
	public class M0296_AddIncidentClaimsRecoveryAndIntake : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("IncidentClaims").Exists())
			{
				Create.Table("IncidentClaims")
					.WithColumn("IncidentClaimId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("ClaimNumber").AsString(32).NotNullable()
					.WithColumn("Kind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ClaimantName").AsString(200).NotNullable()
					.WithColumn("IncidentParticipantId").AsString(36).Nullable()
					.WithColumn("ClaimantContactId").AsString(128).Nullable()
					.WithColumn("ResponsibleUnit").AsString(200).Nullable()
					.WithColumn("AssignedUserId").AsString(128).Nullable()
					.WithColumn("DeadlineOn").AsDate().Nullable()
					.WithColumn("RecordId").AsString(36).Nullable()
					.WithColumn("Currency").AsString(3).NotNullable()
					.WithColumn("OpenedOn").AsDateTime2().NotNullable()
					.WithColumn("ClosedOn").AsDateTime2().Nullable()
					.WithColumn("DeadlineNotifiedOn").AsDateTime2().Nullable()
					.WithColumn("FinancialsJson").AsString(int.MaxValue).Nullable()
					.WithColumn("CaseNotes").AsString(int.MaxValue).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedCatalogVersion").AsInt32().Nullable()
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentClaims_Activation' AND object_id = OBJECT_ID('IncidentClaims')) CREATE NONCLUSTERED INDEX [IX_IncidentClaims_Activation] ON [IncidentClaims] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentClaims_Deadline' AND object_id = OBJECT_ID('IncidentClaims')) CREATE NONCLUSTERED INDEX [IX_IncidentClaims_Deadline] ON [IncidentClaims] ([DepartmentId], [IncidentActivationId], [DeadlineOn]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_IncidentClaims_Number' AND object_id = OBJECT_ID('IncidentClaims')) CREATE UNIQUE NONCLUSTERED INDEX [UX_IncidentClaims_Number] ON [IncidentClaims] ([DepartmentId], [ClaimNumber]) WHERE [IsDeleted] = 0;");
			}

			if (!Schema.Table("IncidentCloseoutChecklistItems").Exists())
			{
				Create.Table("IncidentCloseoutChecklistItems")
					.WithColumn("IncidentCloseoutChecklistItemId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
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
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentCloseoutChecklistItems_Activation' AND object_id = OBJECT_ID('IncidentCloseoutChecklistItems')) CREATE NONCLUSTERED INDEX [IX_IncidentCloseoutChecklistItems_Activation] ON [IncidentCloseoutChecklistItems] ([DepartmentId], [IncidentActivationId]);");
			}

			if (!Schema.Table("IncidentDisasterDeclarations").Exists())
			{
				Create.Table("IncidentDisasterDeclarations")
					.WithColumn("IncidentDisasterDeclarationId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("DeclarationNumber").AsString(32).NotNullable()
					.WithColumn("Program").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("State").AsString(8).Nullable()
					.WithColumn("IncidentPeriodStart").AsDate().Nullable()
					.WithColumn("IncidentPeriodEnd").AsDate().Nullable()
					.WithColumn("EligibleCategories").AsString(32).Nullable()
					.WithColumn("ApplicantId").AsString(64).Nullable()
					.WithColumn("FederalSharePercent").AsDecimal(9, 4).Nullable()
					.WithColumn("NonFederalSharePercent").AsDecimal(9, 4).Nullable()
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("EligibilityPolicyVersion").AsString(64).Nullable()
					.WithColumn("RateScheduleYear").AsInt32().Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentDisasterDeclarations_Activation' AND object_id = OBJECT_ID('IncidentDisasterDeclarations')) CREATE NONCLUSTERED INDEX [IX_IncidentDisasterDeclarations_Activation] ON [IncidentDisasterDeclarations] ([DepartmentId], [IncidentActivationId]);");
			}

			if (!Schema.Table("IncidentRecoveryProjects").Exists())
			{
				Create.Table("IncidentRecoveryProjects")
					.WithColumn("IncidentRecoveryProjectId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("IncidentDisasterDeclarationId").AsString(36).NotNullable()
					.WithColumn("Title").AsString(200).NotNullable()
					.WithColumn("Category").AsString(2).NotNullable()
					.WithColumn("IncidentFacilityId").AsString(36).Nullable()
					.WithColumn("ContactId").AsString(128).Nullable()
					.WithColumn("Location").AsString(500).Nullable()
					.WithColumn("ExternalProjectNumber").AsString(64).Nullable()
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentRecoveryProjects_Activation' AND object_id = OBJECT_ID('IncidentRecoveryProjects')) CREATE NONCLUSTERED INDEX [IX_IncidentRecoveryProjects_Activation] ON [IncidentRecoveryProjects] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentRecoveryProjects_Declaration' AND object_id = OBJECT_ID('IncidentRecoveryProjects')) CREATE NONCLUSTERED INDEX [IX_IncidentRecoveryProjects_Declaration] ON [IncidentRecoveryProjects] ([DepartmentId], [IncidentDisasterDeclarationId]);");
			}

			if (!Schema.Table("IncidentRecoveryDeadlines").Exists())
			{
				Create.Table("IncidentRecoveryDeadlines")
					.WithColumn("IncidentRecoveryDeadlineId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("IncidentDisasterDeclarationId").AsString(36).NotNullable()
					.WithColumn("IncidentRecoveryProjectId").AsString(36).Nullable()
					.WithColumn("Kind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("DueOn").AsDate().NotNullable()
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("NotifiedOn").AsDateTime2().Nullable()
					.WithColumn("Notes").AsString(500).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentRecoveryDeadlines_Activation' AND object_id = OBJECT_ID('IncidentRecoveryDeadlines')) CREATE NONCLUSTERED INDEX [IX_IncidentRecoveryDeadlines_Activation] ON [IncidentRecoveryDeadlines] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentRecoveryDeadlines_Due' AND object_id = OBJECT_ID('IncidentRecoveryDeadlines')) CREATE NONCLUSTERED INDEX [IX_IncidentRecoveryDeadlines_Due] ON [IncidentRecoveryDeadlines] ([DepartmentId], [DueOn]);");
			}

			if (!Schema.Table("IncidentIntakeItems").Exists())
			{
				Create.Table("IncidentIntakeItems")
					.WithColumn("IncidentIntakeItemId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("Channel").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("SenderKind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("SenderReferenceId").AsString(128).Nullable()
					.WithColumn("ReceivedOn").AsDateTime2().NotNullable()
					.WithColumn("FileCount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("TargetType").AsString(32).Nullable()
					.WithColumn("TargetId").AsString(36).Nullable()
					.WithColumn("EvidenceArtifactId").AsString(36).Nullable()
					.WithColumn("FiledOn").AsDateTime2().Nullable()
					.WithColumn("FiledByUserId").AsString(128).Nullable()
					.WithColumn("ProviderMessageId").AsString(128).Nullable()
					.WithColumn("RejectReason").AsString(500).Nullable()
					.WithColumn("LooksLikeShiftTicket").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("SenderAddress").AsString(int.MaxValue).Nullable()
					.WithColumn("Subject").AsString(int.MaxValue).Nullable()
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedCatalogVersion").AsInt32().Nullable()
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentIntakeItems_Activation' AND object_id = OBJECT_ID('IncidentIntakeItems')) CREATE NONCLUSTERED INDEX [IX_IncidentIntakeItems_Activation] ON [IncidentIntakeItems] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentIntakeItems_Status' AND object_id = OBJECT_ID('IncidentIntakeItems')) CREATE NONCLUSTERED INDEX [IX_IncidentIntakeItems_Status] ON [IncidentIntakeItems] ([DepartmentId], [IncidentActivationId], [Status]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_IncidentIntakeItems_Message' AND object_id = OBJECT_ID('IncidentIntakeItems')) CREATE UNIQUE NONCLUSTERED INDEX [UX_IncidentIntakeItems_Message] ON [IncidentIntakeItems] ([DepartmentId], [ProviderMessageId]) WHERE [ProviderMessageId] IS NOT NULL;");
			}

			if (!Schema.Table("IncidentIntakeFiles").Exists())
			{
				Create.Table("IncidentIntakeFiles")
					.WithColumn("IncidentIntakeFileId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("IncidentIntakeItemId").AsString(36).NotNullable()
					.WithColumn("FileName").AsString(260).NotNullable()
					.WithColumn("ContentType").AsString(100).NotNullable()
					.WithColumn("Size").AsInt64().NotNullable().WithDefaultValue(0)
					.WithColumn("Data").AsBinary(int.MaxValue).NotNullable()
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentIntakeFiles_Activation' AND object_id = OBJECT_ID('IncidentIntakeFiles')) CREATE NONCLUSTERED INDEX [IX_IncidentIntakeFiles_Activation] ON [IncidentIntakeFiles] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentIntakeFiles_Item' AND object_id = OBJECT_ID('IncidentIntakeFiles')) CREATE NONCLUSTERED INDEX [IX_IncidentIntakeFiles_Item] ON [IncidentIntakeFiles] ([DepartmentId], [IncidentIntakeItemId]);");
			}
		}

		public override void Down()
		{
			if (Schema.Table("IncidentIntakeFiles").Exists())
				Delete.Table("IncidentIntakeFiles");
			if (Schema.Table("IncidentIntakeItems").Exists())
				Delete.Table("IncidentIntakeItems");
			if (Schema.Table("IncidentRecoveryDeadlines").Exists())
				Delete.Table("IncidentRecoveryDeadlines");
			if (Schema.Table("IncidentRecoveryProjects").Exists())
				Delete.Table("IncidentRecoveryProjects");
			if (Schema.Table("IncidentDisasterDeclarations").Exists())
				Delete.Table("IncidentDisasterDeclarations");
			if (Schema.Table("IncidentCloseoutChecklistItems").Exists())
				Delete.Table("IncidentCloseoutChecklistItems");
			if (Schema.Table("IncidentClaims").Exists())
				Delete.Table("IncidentClaims");
		}
	}
}
