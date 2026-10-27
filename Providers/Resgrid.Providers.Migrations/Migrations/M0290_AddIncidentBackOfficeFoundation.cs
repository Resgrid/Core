using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Incident Back Office LI-A Foundation (large-incident-logistics-finance-plan.md rev 4.1, §9.0–§9.2, §9.5 enrollment,
	/// §9.17 intelligence, §11; registry §4C): activations with complexes and exercise mode, authority profiles and snapshots,
	/// standing team rosters, operational periods, plan assembly manifests and weather blocks, objectives, the position
	/// catalog, organization nodes and position assignments, participants with protected detail (ADP catalog 34) and
	/// qualification snapshots, the snapshot / metric / freshness / projection-cursor read models and the position-routing log.
	/// No column on any IC table. No foreign keys (the IC precedent): tenant ownership is enforced in the service layer and the
	/// department purge (IncidentBackOfficeDepartmentCleanup) removes every row. Existence-guarded for safe retry.
	/// </summary>
	[Migration(290)]
	public class M0290_AddIncidentBackOfficeFoundation : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("IncidentActivations").Exists())
			{
				Create.Table("IncidentActivations")
					.WithColumn("IncidentActivationId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("Kind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ParentActivationId").AsString(36).Nullable()
					.WithColumn("CallId").AsInt32().Nullable()
					.WithColumn("IncidentCommandId").AsString(128).Nullable()
					.WithColumn("Name").AsString(256).NotNullable()
					.WithColumn("ShortCode").AsString(32).NotNullable()
					.WithColumn("ComplexityType").AsInt32().Nullable()
					.WithColumn("TimeZoneId").AsString(100).NotNullable()
					.WithColumn("Currency").AsString(3).NotNullable()
					.WithColumn("MeasurementSystem").AsString(10).NotNullable()
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("IsExercise").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("AuthorityProfileSnapshotId").AsString(36).Nullable()
					.WithColumn("Description").AsString(int.MaxValue).Nullable()
					.WithColumn("OpenedOn").AsDateTime2().Nullable()
					.WithColumn("OpenedByUserId").AsString(128).Nullable()
					.WithColumn("ClosedOn").AsDateTime2().Nullable()
					.WithColumn("ClosedByUserId").AsString(128).Nullable()
					.WithColumn("SealedOn").AsDateTime2().Nullable()
					.WithColumn("SealedByUserId").AsString(128).Nullable()
					.WithColumn("ReopenedOn").AsDateTime2().Nullable()
					.WithColumn("ReopenedByUserId").AsString(128).Nullable()
					.WithColumn("ReopenReason").AsString(int.MaxValue).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentActivations_Department' AND object_id = OBJECT_ID('IncidentActivations')) CREATE NONCLUSTERED INDEX [IX_IncidentActivations_Department] ON [IncidentActivations] ([DepartmentId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentActivations_Status' AND object_id = OBJECT_ID('IncidentActivations')) CREATE NONCLUSTERED INDEX [IX_IncidentActivations_Status] ON [IncidentActivations] ([DepartmentId], [Status]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentActivations_Call' AND object_id = OBJECT_ID('IncidentActivations')) CREATE NONCLUSTERED INDEX [IX_IncidentActivations_Call] ON [IncidentActivations] ([DepartmentId], [CallId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentActivations_Parent' AND object_id = OBJECT_ID('IncidentActivations')) CREATE NONCLUSTERED INDEX [IX_IncidentActivations_Parent] ON [IncidentActivations] ([DepartmentId], [ParentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_IncidentActivations_ShortCode' AND object_id = OBJECT_ID('IncidentActivations')) CREATE UNIQUE NONCLUSTERED INDEX [UX_IncidentActivations_ShortCode] ON [IncidentActivations] ([DepartmentId], [ShortCode]) WHERE [IsDeleted] = 0;");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_IncidentActivations_Board' AND object_id = OBJECT_ID('IncidentActivations')) CREATE UNIQUE NONCLUSTERED INDEX [UX_IncidentActivations_Board] ON [IncidentActivations] ([DepartmentId], [IncidentCommandId]) WHERE [IsDeleted] = 0 AND [IncidentCommandId] IS NOT NULL;");
			}

			if (!Schema.Table("DepartmentIncidentAuthorityProfiles").Exists())
			{
				Create.Table("DepartmentIncidentAuthorityProfiles")
					.WithColumn("DepartmentIncidentAuthorityProfileId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("Name").AsString(200).NotNullable()
					.WithColumn("IsDefault").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("Description").AsString(int.MaxValue).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DepartmentIncidentAuthorityProfiles_Department' AND object_id = OBJECT_ID('DepartmentIncidentAuthorityProfiles')) CREATE NONCLUSTERED INDEX [IX_DepartmentIncidentAuthorityProfiles_Department] ON [DepartmentIncidentAuthorityProfiles] ([DepartmentId]);");
			}

			if (!Schema.Table("IncidentDomainAuthorities").Exists())
			{
				Create.Table("IncidentDomainAuthorities")
					.WithColumn("IncidentDomainAuthorityId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("OwnerKind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("OwnerId").AsString(36).NotNullable()
					.WithColumn("Domain").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Mode").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("SourceSystem").AsString(100).Nullable()
					.WithColumn("ConnectorState").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Precedence").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Notes").AsString(int.MaxValue).Nullable()
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentDomainAuthorities_Department' AND object_id = OBJECT_ID('IncidentDomainAuthorities')) CREATE NONCLUSTERED INDEX [IX_IncidentDomainAuthorities_Department] ON [IncidentDomainAuthorities] ([DepartmentId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentDomainAuthorities_Owner' AND object_id = OBJECT_ID('IncidentDomainAuthorities')) CREATE NONCLUSTERED INDEX [IX_IncidentDomainAuthorities_Owner] ON [IncidentDomainAuthorities] ([DepartmentId], [OwnerKind], [OwnerId]);");
			}

			if (!Schema.Table("IncidentAuthorityProfileSnapshots").Exists())
			{
				Create.Table("IncidentAuthorityProfileSnapshots")
					.WithColumn("IncidentAuthorityProfileSnapshotId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("SourceProfileId").AsString(36).Nullable()
					.WithColumn("Name").AsString(200).NotNullable()
					.WithColumn("TakenOn").AsDateTime2().NotNullable()
					.WithColumn("TakenByUserId").AsString(128).Nullable()
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentAuthorityProfileSnapshots_Activation' AND object_id = OBJECT_ID('IncidentAuthorityProfileSnapshots')) CREATE NONCLUSTERED INDEX [IX_IncidentAuthorityProfileSnapshots_Activation] ON [IncidentAuthorityProfileSnapshots] ([DepartmentId], [IncidentActivationId]);");
			}

			if (!Schema.Table("IncidentReconciliationItems").Exists())
			{
				Create.Table("IncidentReconciliationItems")
					.WithColumn("IncidentReconciliationItemId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("Domain").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("SubjectType").AsString(32).NotNullable()
					.WithColumn("SubjectId").AsString(36).Nullable()
					.WithColumn("SourceSystem").AsString(100).Nullable()
					.WithColumn("ExternalIdentifier").AsString(200).Nullable()
					.WithColumn("Description").AsString(int.MaxValue).Nullable()
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("OwnerUserId").AsString(128).Nullable()
					.WithColumn("ResolvedOn").AsDateTime2().Nullable()
					.WithColumn("ResolvedByUserId").AsString(128).Nullable()
					.WithColumn("Resolution").AsString(int.MaxValue).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentReconciliationItems_Activation' AND object_id = OBJECT_ID('IncidentReconciliationItems')) CREATE NONCLUSTERED INDEX [IX_IncidentReconciliationItems_Activation] ON [IncidentReconciliationItems] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentReconciliationItems_Status' AND object_id = OBJECT_ID('IncidentReconciliationItems')) CREATE NONCLUSTERED INDEX [IX_IncidentReconciliationItems_Status] ON [IncidentReconciliationItems] ([DepartmentId], [IncidentActivationId], [Status]);");
			}

			if (!Schema.Table("DepartmentIncidentTeams").Exists())
			{
				Create.Table("DepartmentIncidentTeams")
					.WithColumn("DepartmentIncidentTeamId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("Name").AsString(200).NotNullable()
					.WithColumn("TeamType").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("IsActive").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("OwningGroupId").AsInt32().Nullable()
					.WithColumn("Description").AsString(int.MaxValue).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DepartmentIncidentTeams_Department' AND object_id = OBJECT_ID('DepartmentIncidentTeams')) CREATE NONCLUSTERED INDEX [IX_DepartmentIncidentTeams_Department] ON [DepartmentIncidentTeams] ([DepartmentId]);");
			}

			if (!Schema.Table("DepartmentIncidentTeamRotations").Exists())
			{
				Create.Table("DepartmentIncidentTeamRotations")
					.WithColumn("DepartmentIncidentTeamRotationId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("DepartmentIncidentTeamId").AsString(36).NotNullable()
					.WithColumn("WindowStart").AsDateTime2().NotNullable()
					.WithColumn("WindowEnd").AsDateTime2().NotNullable()
					.WithColumn("Notes").AsString(int.MaxValue).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DepartmentIncidentTeamRotations_Department' AND object_id = OBJECT_ID('DepartmentIncidentTeamRotations')) CREATE NONCLUSTERED INDEX [IX_DepartmentIncidentTeamRotations_Department] ON [DepartmentIncidentTeamRotations] ([DepartmentId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DepartmentIncidentTeamRotations_Team' AND object_id = OBJECT_ID('DepartmentIncidentTeamRotations')) CREATE NONCLUSTERED INDEX [IX_DepartmentIncidentTeamRotations_Team] ON [DepartmentIncidentTeamRotations] ([DepartmentId], [DepartmentIncidentTeamId], [WindowStart]);");
			}

			if (!Schema.Table("DepartmentIncidentTeamSlots").Exists())
			{
				Create.Table("DepartmentIncidentTeamSlots")
					.WithColumn("DepartmentIncidentTeamSlotId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("DepartmentIncidentTeamId").AsString(36).NotNullable()
					.WithColumn("IncidentPositionDefinitionId").AsString(36).NotNullable()
					.WithColumn("SlotOrder").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Label").AsString(200).Nullable()
					.WithColumn("Notes").AsString(int.MaxValue).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DepartmentIncidentTeamSlots_Department' AND object_id = OBJECT_ID('DepartmentIncidentTeamSlots')) CREATE NONCLUSTERED INDEX [IX_DepartmentIncidentTeamSlots_Department] ON [DepartmentIncidentTeamSlots] ([DepartmentId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DepartmentIncidentTeamSlots_Team' AND object_id = OBJECT_ID('DepartmentIncidentTeamSlots')) CREATE NONCLUSTERED INDEX [IX_DepartmentIncidentTeamSlots_Team] ON [DepartmentIncidentTeamSlots] ([DepartmentId], [DepartmentIncidentTeamId], [SlotOrder]);");
			}

			if (!Schema.Table("DepartmentIncidentTeamCandidates").Exists())
			{
				Create.Table("DepartmentIncidentTeamCandidates")
					.WithColumn("DepartmentIncidentTeamCandidateId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("DepartmentIncidentTeamId").AsString(36).NotNullable()
					.WithColumn("DepartmentIncidentTeamSlotId").AsString(36).NotNullable()
					.WithColumn("CandidateKind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Priority").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("UserId").AsString(128).Nullable()
					.WithColumn("ContactId").AsString(128).Nullable()
					.WithColumn("Notes").AsString(int.MaxValue).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DepartmentIncidentTeamCandidates_Department' AND object_id = OBJECT_ID('DepartmentIncidentTeamCandidates')) CREATE NONCLUSTERED INDEX [IX_DepartmentIncidentTeamCandidates_Department] ON [DepartmentIncidentTeamCandidates] ([DepartmentId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DepartmentIncidentTeamCandidates_Slot' AND object_id = OBJECT_ID('DepartmentIncidentTeamCandidates')) CREATE NONCLUSTERED INDEX [IX_DepartmentIncidentTeamCandidates_Slot] ON [DepartmentIncidentTeamCandidates] ([DepartmentId], [DepartmentIncidentTeamSlotId], [Priority]);");
			}

			if (!Schema.Table("IncidentOperationalPeriods").Exists())
			{
				Create.Table("IncidentOperationalPeriods")
					.WithColumn("IncidentOperationalPeriodId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("Number").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("StartOn").AsDateTime2().NotNullable()
					.WithColumn("EndOn").AsDateTime2().NotNullable()
					.WithColumn("TimeZoneId").AsString(100).NotNullable()
					.WithColumn("BriefingOn").AsDateTime2().Nullable()
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("PlannerUserId").AsString(128).Nullable()
					.WithColumn("IsFrozen").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("FrozenOn").AsDateTime2().Nullable()
					.WithColumn("FrozenByUserId").AsString(128).Nullable()
					.WithColumn("ApprovedOn").AsDateTime2().Nullable()
					.WithColumn("ApprovedByUserId").AsString(128).Nullable()
					.WithColumn("CopiedFromPeriodId").AsString(36).Nullable()
					.WithColumn("Notes").AsString(int.MaxValue).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentOperationalPeriods_Activation' AND object_id = OBJECT_ID('IncidentOperationalPeriods')) CREATE NONCLUSTERED INDEX [IX_IncidentOperationalPeriods_Activation] ON [IncidentOperationalPeriods] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentOperationalPeriods_Window' AND object_id = OBJECT_ID('IncidentOperationalPeriods')) CREATE NONCLUSTERED INDEX [IX_IncidentOperationalPeriods_Window] ON [IncidentOperationalPeriods] ([DepartmentId], [IncidentActivationId], [StartOn]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_IncidentOperationalPeriods_Number' AND object_id = OBJECT_ID('IncidentOperationalPeriods')) CREATE UNIQUE NONCLUSTERED INDEX [UX_IncidentOperationalPeriods_Number] ON [IncidentOperationalPeriods] ([DepartmentId], [IncidentActivationId], [Number]) WHERE [IsDeleted] = 0;");
			}

			if (!Schema.Table("IncidentPlanVersions").Exists())
			{
				Create.Table("IncidentPlanVersions")
					.WithColumn("IncidentPlanVersionId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("IncidentOperationalPeriodId").AsString(36).NotNullable()
					.WithColumn("VersionNumber").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("RecordId").AsString(36).Nullable()
					.WithColumn("ManifestJson").AsString(int.MaxValue).Nullable()
					.WithColumn("DistributionJson").AsString(int.MaxValue).Nullable()
					.WithColumn("PrintCopies").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ApprovedOn").AsDateTime2().Nullable()
					.WithColumn("ApprovedByUserId").AsString(128).Nullable()
					.WithColumn("DistributedOn").AsDateTime2().Nullable()
					.WithColumn("SupersededByPlanVersionId").AsString(36).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentPlanVersions_Activation' AND object_id = OBJECT_ID('IncidentPlanVersions')) CREATE NONCLUSTERED INDEX [IX_IncidentPlanVersions_Activation] ON [IncidentPlanVersions] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentPlanVersions_Period' AND object_id = OBJECT_ID('IncidentPlanVersions')) CREATE NONCLUSTERED INDEX [IX_IncidentPlanVersions_Period] ON [IncidentPlanVersions] ([DepartmentId], [IncidentActivationId], [IncidentOperationalPeriodId]);");
			}

			if (!Schema.Table("IncidentPlanWeatherBlocks").Exists())
			{
				Create.Table("IncidentPlanWeatherBlocks")
					.WithColumn("IncidentPlanWeatherBlockId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("IncidentPlanVersionId").AsString(36).Nullable()
					.WithColumn("IncidentOperationalPeriodId").AsString(36).Nullable()
					.WithColumn("ForecastSource").AsString(200).Nullable()
					.WithColumn("IssuedOn").AsDateTime2().Nullable()
					.WithColumn("ForecastPeriod").AsString(200).Nullable()
					.WithColumn("TemperatureSummary").AsString(500).Nullable()
					.WithColumn("HumiditySummary").AsString(500).Nullable()
					.WithColumn("WindSummary").AsString(500).Nullable()
					.WithColumn("PrecipitationSummary").AsString(500).Nullable()
					.WithColumn("SpotForecastReference").AsString(500).Nullable()
					.WithColumn("ActiveAlertIdsJson").AsString(int.MaxValue).Nullable()
					.WithColumn("Narrative").AsString(int.MaxValue).Nullable()
					.WithColumn("SnapshottedOn").AsDateTime2().Nullable()
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentPlanWeatherBlocks_Activation' AND object_id = OBJECT_ID('IncidentPlanWeatherBlocks')) CREATE NONCLUSTERED INDEX [IX_IncidentPlanWeatherBlocks_Activation] ON [IncidentPlanWeatherBlocks] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentPlanWeatherBlocks_Plan' AND object_id = OBJECT_ID('IncidentPlanWeatherBlocks')) CREATE NONCLUSTERED INDEX [IX_IncidentPlanWeatherBlocks_Plan] ON [IncidentPlanWeatherBlocks] ([DepartmentId], [IncidentPlanVersionId]);");
			}

			if (!Schema.Table("IncidentObjectives").Exists())
			{
				Create.Table("IncidentObjectives")
					.WithColumn("IncidentObjectiveId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("IncidentOperationalPeriodId").AsString(36).Nullable()
					.WithColumn("Number").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Text").AsString(int.MaxValue).NotNullable()
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("CarriedFromObjectiveId").AsString(36).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentObjectives_Activation' AND object_id = OBJECT_ID('IncidentObjectives')) CREATE NONCLUSTERED INDEX [IX_IncidentObjectives_Activation] ON [IncidentObjectives] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentObjectives_Period' AND object_id = OBJECT_ID('IncidentObjectives')) CREATE NONCLUSTERED INDEX [IX_IncidentObjectives_Period] ON [IncidentObjectives] ([DepartmentId], [IncidentActivationId], [IncidentOperationalPeriodId]);");
			}

			if (!Schema.Table("IncidentPositionDefinitions").Exists())
			{
				Create.Table("IncidentPositionDefinitions")
					.WithColumn("IncidentPositionDefinitionId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).Nullable()
					.WithColumn("Code").AsString(32).NotNullable()
					.WithColumn("Name").AsString(200).NotNullable()
					.WithColumn("Section").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Branch").AsString(100).Nullable()
					.WithColumn("UnitName").AsString(100).Nullable()
					.WithColumn("Description").AsString(int.MaxValue).Nullable()
					.WithColumn("SourceCatalog").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("SourceVersion").AsString(32).Nullable()
					.WithColumn("RequirementsJson").AsString(int.MaxValue).Nullable()
					.WithColumn("DefaultWorkspace").AsString(64).Nullable()
					.WithColumn("DefaultIncidentRoleType").AsInt32().Nullable()
					.WithColumn("SupervisorCode").AsString(32).Nullable()
					.WithColumn("IsStandard").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("IsActive").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("SortOrder").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentPositionDefinitions_Department' AND object_id = OBJECT_ID('IncidentPositionDefinitions')) CREATE NONCLUSTERED INDEX [IX_IncidentPositionDefinitions_Department] ON [IncidentPositionDefinitions] ([DepartmentId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentPositionDefinitions_Section' AND object_id = OBJECT_ID('IncidentPositionDefinitions')) CREATE NONCLUSTERED INDEX [IX_IncidentPositionDefinitions_Section] ON [IncidentPositionDefinitions] ([DepartmentId], [Section], [SortOrder]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_IncidentPositionDefinitions_Code' AND object_id = OBJECT_ID('IncidentPositionDefinitions')) CREATE UNIQUE NONCLUSTERED INDEX [UX_IncidentPositionDefinitions_Code] ON [IncidentPositionDefinitions] ([DepartmentId], [Code]) WHERE [IsDeleted] = 0 AND [IncidentActivationId] IS NULL;");
			}

			if (!Schema.Table("IncidentPositionCapabilities").Exists())
			{
				Create.Table("IncidentPositionCapabilities")
					.WithColumn("IncidentPositionCapabilityId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentPositionDefinitionId").AsString(36).NotNullable()
					.WithColumn("CapabilityKey").AsString(64).NotNullable()
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentPositionCapabilities_Department' AND object_id = OBJECT_ID('IncidentPositionCapabilities')) CREATE NONCLUSTERED INDEX [IX_IncidentPositionCapabilities_Department] ON [IncidentPositionCapabilities] ([DepartmentId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentPositionCapabilities_Position' AND object_id = OBJECT_ID('IncidentPositionCapabilities')) CREATE NONCLUSTERED INDEX [IX_IncidentPositionCapabilities_Position] ON [IncidentPositionCapabilities] ([DepartmentId], [IncidentPositionDefinitionId]);");
			}

			if (!Schema.Table("IncidentOrganizationNodes").Exists())
			{
				Create.Table("IncidentOrganizationNodes")
					.WithColumn("IncidentOrganizationNodeId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("IncidentOperationalPeriodId").AsString(36).Nullable()
					.WithColumn("ParentNodeId").AsString(36).Nullable()
					.WithColumn("Kind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Name").AsString(200).NotNullable()
					.WithColumn("Section").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("SortOrder").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentOrganizationNodes_Activation' AND object_id = OBJECT_ID('IncidentOrganizationNodes')) CREATE NONCLUSTERED INDEX [IX_IncidentOrganizationNodes_Activation] ON [IncidentOrganizationNodes] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentOrganizationNodes_Period' AND object_id = OBJECT_ID('IncidentOrganizationNodes')) CREATE NONCLUSTERED INDEX [IX_IncidentOrganizationNodes_Period] ON [IncidentOrganizationNodes] ([DepartmentId], [IncidentActivationId], [IncidentOperationalPeriodId]);");
			}

			if (!Schema.Table("IncidentPositionAssignments").Exists())
			{
				Create.Table("IncidentPositionAssignments")
					.WithColumn("IncidentPositionAssignmentId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("IncidentOperationalPeriodId").AsString(36).Nullable()
					.WithColumn("IncidentPositionDefinitionId").AsString(36).NotNullable()
					.WithColumn("IncidentOrganizationNodeId").AsString(36).Nullable()
					.WithColumn("SubjectType").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("UserId").AsString(128).Nullable()
					.WithColumn("IncidentParticipantId").AsString(36).Nullable()
					.WithColumn("PlaceholderLabel").AsString(200).Nullable()
					.WithColumn("AssignmentKind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Shift").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("StartOn").AsDateTime2().NotNullable()
					.WithColumn("EndOn").AsDateTime2().Nullable()
					.WithColumn("SupervisorAssignmentId").AsString(36).Nullable()
					.WithColumn("IncidentRoleAssignmentId").AsString(128).Nullable()
					.WithColumn("IsTrainee").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("TaskBookReference").AsString(100).Nullable()
					.WithColumn("TaskBookExpiresOn").AsDate().Nullable()
					.WithColumn("TrainingSpecialistAssignmentId").AsString(36).Nullable()
					.WithColumn("TraineePriority").AsInt32().Nullable()
					.WithColumn("QualificationState").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("QualificationNote").AsString(500).Nullable()
					.WithColumn("TeamActivationBatchId").AsString(36).Nullable()
					.WithColumn("Notes").AsString(int.MaxValue).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentPositionAssignments_Activation' AND object_id = OBJECT_ID('IncidentPositionAssignments')) CREATE NONCLUSTERED INDEX [IX_IncidentPositionAssignments_Activation] ON [IncidentPositionAssignments] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentPositionAssignments_Position' AND object_id = OBJECT_ID('IncidentPositionAssignments')) CREATE NONCLUSTERED INDEX [IX_IncidentPositionAssignments_Position] ON [IncidentPositionAssignments] ([DepartmentId], [IncidentActivationId], [IncidentPositionDefinitionId], [StartOn]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentPositionAssignments_User' AND object_id = OBJECT_ID('IncidentPositionAssignments')) CREATE NONCLUSTERED INDEX [IX_IncidentPositionAssignments_User] ON [IncidentPositionAssignments] ([DepartmentId], [UserId], [StartOn]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentPositionAssignments_Period' AND object_id = OBJECT_ID('IncidentPositionAssignments')) CREATE NONCLUSTERED INDEX [IX_IncidentPositionAssignments_Period] ON [IncidentPositionAssignments] ([DepartmentId], [IncidentActivationId], [IncidentOperationalPeriodId]);");
			}

			if (!Schema.Table("IncidentParticipants").Exists())
			{
				Create.Table("IncidentParticipants")
					.WithColumn("IncidentParticipantId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("UserId").AsString(128).Nullable()
					.WithColumn("ContactId").AsString(128).Nullable()
					.WithColumn("IncidentAdHocPersonnelId").AsString(128).Nullable()
					.WithColumn("DisplayName").AsString(200).NotNullable()
					.WithColumn("Agency").AsString(200).Nullable()
					.WithColumn("HomeUnit").AsString(200).Nullable()
					.WithColumn("PositionTitle").AsString(200).Nullable()
					.WithColumn("OrderNumber").AsString(100).Nullable()
					.WithColumn("RequestNumber").AsString(100).Nullable()
					.WithColumn("PaymentClass").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("SupportStatus").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("EnrollmentSource").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("IsDraft").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ExpectedArrivalOn").AsDateTime2().Nullable()
					.WithColumn("ArrivedOn").AsDateTime2().Nullable()
					.WithColumn("CheckedInOn").AsDateTime2().Nullable()
					.WithColumn("AssignmentStartOn").AsDate().Nullable()
					.WithColumn("MaxAssignmentDays").AsInt32().Nullable()
					.WithColumn("TimeoutOn").AsDate().Nullable()
					.WithColumn("TentativeReleaseOn").AsDateTime2().Nullable()
					.WithColumn("ReleasedOn").AsDateTime2().Nullable()
					.WithColumn("DepartedOn").AsDateTime2().Nullable()
					.WithColumn("TravelMode").AsString(64).Nullable()
					.WithColumn("IsSelfSufficient").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("NeedsLodging").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("NeedsMeals").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("CheckInCode").AsString(32).Nullable()
					.WithColumn("PersonalPhone").AsString(int.MaxValue).Nullable()
					.WithColumn("PersonalEmail").AsString(int.MaxValue).Nullable()
					.WithColumn("HomeAddress").AsString(int.MaxValue).Nullable()
					.WithColumn("DateOfBirth").AsString(int.MaxValue).Nullable()
					.WithColumn("ExternalIdentifier").AsString(int.MaxValue).Nullable()
					.WithColumn("Notes").AsString(int.MaxValue).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedCatalogVersion").AsInt32().Nullable()
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentParticipants_Activation' AND object_id = OBJECT_ID('IncidentParticipants')) CREATE NONCLUSTERED INDEX [IX_IncidentParticipants_Activation] ON [IncidentParticipants] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentParticipants_Status' AND object_id = OBJECT_ID('IncidentParticipants')) CREATE NONCLUSTERED INDEX [IX_IncidentParticipants_Status] ON [IncidentParticipants] ([DepartmentId], [IncidentActivationId], [SupportStatus]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentParticipants_User' AND object_id = OBJECT_ID('IncidentParticipants')) CREATE NONCLUSTERED INDEX [IX_IncidentParticipants_User] ON [IncidentParticipants] ([DepartmentId], [UserId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_IncidentParticipants_AdHoc' AND object_id = OBJECT_ID('IncidentParticipants')) CREATE UNIQUE NONCLUSTERED INDEX [UX_IncidentParticipants_AdHoc] ON [IncidentParticipants] ([DepartmentId], [IncidentActivationId], [IncidentAdHocPersonnelId]) WHERE [IsDeleted] = 0 AND [IncidentAdHocPersonnelId] IS NOT NULL;");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_IncidentParticipants_Member' AND object_id = OBJECT_ID('IncidentParticipants')) CREATE UNIQUE NONCLUSTERED INDEX [UX_IncidentParticipants_Member] ON [IncidentParticipants] ([DepartmentId], [IncidentActivationId], [UserId]) WHERE [IsDeleted] = 0 AND [UserId] IS NOT NULL;");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_IncidentParticipants_CheckInCode' AND object_id = OBJECT_ID('IncidentParticipants')) CREATE UNIQUE NONCLUSTERED INDEX [UX_IncidentParticipants_CheckInCode] ON [IncidentParticipants] ([DepartmentId], [CheckInCode]) WHERE [IsDeleted] = 0 AND [CheckInCode] IS NOT NULL;");
			}

			if (!Schema.Table("IncidentParticipantSensitive").Exists())
			{
				Create.Table("IncidentParticipantSensitive")
					.WithColumn("IncidentParticipantSensitiveId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("IncidentParticipantId").AsString(36).NotNullable()
					.WithColumn("EmergencyContactName").AsString(int.MaxValue).Nullable()
					.WithColumn("EmergencyContactPhone").AsString(int.MaxValue).Nullable()
					.WithColumn("EmergencyContactRelationship").AsString(int.MaxValue).Nullable()
					.WithColumn("MedicalAccommodation").AsString(int.MaxValue).Nullable()
					.WithColumn("AccessibilityNeed").AsString(int.MaxValue).Nullable()
					.WithColumn("AllergenDetail").AsString(int.MaxValue).Nullable()
					.WithColumn("DietaryRestriction").AsString(int.MaxValue).Nullable()
					.WithColumn("DietaryCategoryCode").AsString(int.MaxValue).Nullable()
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedCatalogVersion").AsInt32().Nullable()
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentParticipantSensitive_Activation' AND object_id = OBJECT_ID('IncidentParticipantSensitive')) CREATE NONCLUSTERED INDEX [IX_IncidentParticipantSensitive_Activation] ON [IncidentParticipantSensitive] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_IncidentParticipantSensitive_Participant' AND object_id = OBJECT_ID('IncidentParticipantSensitive')) CREATE UNIQUE NONCLUSTERED INDEX [UX_IncidentParticipantSensitive_Participant] ON [IncidentParticipantSensitive] ([DepartmentId], [IncidentParticipantId]);");
			}

			if (!Schema.Table("IncidentParticipantQualifications").Exists())
			{
				Create.Table("IncidentParticipantQualifications")
					.WithColumn("IncidentParticipantQualificationId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("IncidentParticipantId").AsString(36).NotNullable()
					.WithColumn("Code").AsString(64).NotNullable()
					.WithColumn("Name").AsString(200).Nullable()
					.WithColumn("Source").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("SourceName").AsString(200).Nullable()
					.WithColumn("VerificationState").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("EffectiveOn").AsDate().Nullable()
					.WithColumn("ExpiresOn").AsDate().Nullable()
					.WithColumn("SnapshotOn").AsDateTime2().NotNullable()
					.WithColumn("CredentialNumber").AsString(int.MaxValue).Nullable()
					.WithColumn("VerificationNotes").AsString(int.MaxValue).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedCatalogVersion").AsInt32().Nullable()
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentParticipantQualifications_Activation' AND object_id = OBJECT_ID('IncidentParticipantQualifications')) CREATE NONCLUSTERED INDEX [IX_IncidentParticipantQualifications_Activation] ON [IncidentParticipantQualifications] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentParticipantQualifications_Participant' AND object_id = OBJECT_ID('IncidentParticipantQualifications')) CREATE NONCLUSTERED INDEX [IX_IncidentParticipantQualifications_Participant] ON [IncidentParticipantQualifications] ([DepartmentId], [IncidentParticipantId]);");
			}

			if (!Schema.Table("IncidentExternalOrganizationRefs").Exists())
			{
				Create.Table("IncidentExternalOrganizationRefs")
					.WithColumn("IncidentExternalOrganizationRefId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("ContactId").AsString(128).NotNullable()
					.WithColumn("Role").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Name").AsString(200).NotNullable()
					.WithColumn("Notes").AsString(int.MaxValue).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentExternalOrganizationRefs_Activation' AND object_id = OBJECT_ID('IncidentExternalOrganizationRefs')) CREATE NONCLUSTERED INDEX [IX_IncidentExternalOrganizationRefs_Activation] ON [IncidentExternalOrganizationRefs] ([DepartmentId], [IncidentActivationId]);");
			}

			if (!Schema.Table("IncidentOperationalSnapshots").Exists())
			{
				Create.Table("IncidentOperationalSnapshots")
					.WithColumn("IncidentOperationalSnapshotId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("Cursor").AsInt64().NotNullable().WithDefaultValue(0)
					.WithColumn("TakenOn").AsDateTime2().NotNullable()
					.WithColumn("Kind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("OldestSourceOn").AsDateTime2().Nullable()
					.WithColumn("LagSeconds").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("DriftDetected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("SummaryJson").AsString(int.MaxValue).Nullable()
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentOperationalSnapshots_Activation' AND object_id = OBJECT_ID('IncidentOperationalSnapshots')) CREATE NONCLUSTERED INDEX [IX_IncidentOperationalSnapshots_Activation] ON [IncidentOperationalSnapshots] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentOperationalSnapshots_Cursor' AND object_id = OBJECT_ID('IncidentOperationalSnapshots')) CREATE NONCLUSTERED INDEX [IX_IncidentOperationalSnapshots_Cursor] ON [IncidentOperationalSnapshots] ([DepartmentId], [IncidentActivationId], [Cursor]);");
			}

			if (!Schema.Table("IncidentMetricValues").Exists())
			{
				Create.Table("IncidentMetricValues")
					.WithColumn("IncidentMetricValueId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("IncidentOperationalSnapshotId").AsString(36).NotNullable()
					.WithColumn("MetricKey").AsString(64).NotNullable()
					.WithColumn("MetricVersion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Bucket").AsString(64).Nullable()
					.WithColumn("Value").AsDecimal(18, 4).NotNullable().WithDefaultValue(0)
					.WithColumn("Unit").AsString(16).Nullable()
					.WithColumn("SourceCount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("AsOf").AsDateTime2().NotNullable()
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentMetricValues_Activation' AND object_id = OBJECT_ID('IncidentMetricValues')) CREATE NONCLUSTERED INDEX [IX_IncidentMetricValues_Activation] ON [IncidentMetricValues] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentMetricValues_Snapshot' AND object_id = OBJECT_ID('IncidentMetricValues')) CREATE NONCLUSTERED INDEX [IX_IncidentMetricValues_Snapshot] ON [IncidentMetricValues] ([DepartmentId], [IncidentOperationalSnapshotId], [MetricKey]);");
			}

			if (!Schema.Table("IncidentDataFreshness").Exists())
			{
				Create.Table("IncidentDataFreshness")
					.WithColumn("IncidentDataFreshnessId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("SourceKey").AsString(64).NotNullable()
					.WithColumn("LastObservedOn").AsDateTime2().Nullable()
					.WithColumn("LastChangeOn").AsDateTime2().Nullable()
					.WithColumn("StalenessSeconds").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("IsStale").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("UpdatedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentDataFreshness_Activation' AND object_id = OBJECT_ID('IncidentDataFreshness')) CREATE NONCLUSTERED INDEX [IX_IncidentDataFreshness_Activation] ON [IncidentDataFreshness] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_IncidentDataFreshness_Source' AND object_id = OBJECT_ID('IncidentDataFreshness')) CREATE UNIQUE NONCLUSTERED INDEX [UX_IncidentDataFreshness_Source] ON [IncidentDataFreshness] ([DepartmentId], [IncidentActivationId], [SourceKey]);");
			}

			if (!Schema.Table("IncidentSavedDashboardViews").Exists())
			{
				Create.Table("IncidentSavedDashboardViews")
					.WithColumn("IncidentSavedDashboardViewId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).Nullable()
					.WithColumn("UserId").AsString(128).Nullable()
					.WithColumn("Name").AsString(200).NotNullable()
					.WithColumn("DeskKey").AsString(64).NotNullable()
					.WithColumn("FiltersJson").AsString(int.MaxValue).Nullable()
					.WithColumn("IsShared").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentSavedDashboardViews_Department' AND object_id = OBJECT_ID('IncidentSavedDashboardViews')) CREATE NONCLUSTERED INDEX [IX_IncidentSavedDashboardViews_Department] ON [IncidentSavedDashboardViews] ([DepartmentId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentSavedDashboardViews_User' AND object_id = OBJECT_ID('IncidentSavedDashboardViews')) CREATE NONCLUSTERED INDEX [IX_IncidentSavedDashboardViews_User] ON [IncidentSavedDashboardViews] ([DepartmentId], [UserId]);");
			}

			if (!Schema.Table("IncidentProjectionCursors").Exists())
			{
				Create.Table("IncidentProjectionCursors")
					.WithColumn("IncidentProjectionCursorId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("ContractKey").AsString(64).NotNullable()
					.WithColumn("ContractVersion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Cursor").AsInt64().NotNullable().WithDefaultValue(0)
					.WithColumn("OldestSourceOn").AsDateTime2().Nullable()
					.WithColumn("UpdatedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentProjectionCursors_Activation' AND object_id = OBJECT_ID('IncidentProjectionCursors')) CREATE NONCLUSTERED INDEX [IX_IncidentProjectionCursors_Activation] ON [IncidentProjectionCursors] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_IncidentProjectionCursors_Contract' AND object_id = OBJECT_ID('IncidentProjectionCursors')) CREATE UNIQUE NONCLUSTERED INDEX [UX_IncidentProjectionCursors_Contract] ON [IncidentProjectionCursors] ([DepartmentId], [IncidentActivationId], [ContractKey]);");
			}

			if (!Schema.Table("IncidentNotificationLogs").Exists())
			{
				Create.Table("IncidentNotificationLogs")
					.WithColumn("IncidentNotificationLogId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("EventType").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("PositionCode").AsString(32).Nullable()
					.WithColumn("HoldersJson").AsString(int.MaxValue).Nullable()
					.WithColumn("Fallback").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("RecipientCount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Summary").AsString(500).Nullable()
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentNotificationLogs_Activation' AND object_id = OBJECT_ID('IncidentNotificationLogs')) CREATE NONCLUSTERED INDEX [IX_IncidentNotificationLogs_Activation] ON [IncidentNotificationLogs] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentNotificationLogs_Sent' AND object_id = OBJECT_ID('IncidentNotificationLogs')) CREATE NONCLUSTERED INDEX [IX_IncidentNotificationLogs_Sent] ON [IncidentNotificationLogs] ([DepartmentId], [IncidentActivationId], [AddedOn]);");
			}
		}

		public override void Down()
		{
			if (Schema.Table("IncidentNotificationLogs").Exists())
				Delete.Table("IncidentNotificationLogs");
			if (Schema.Table("IncidentProjectionCursors").Exists())
				Delete.Table("IncidentProjectionCursors");
			if (Schema.Table("IncidentSavedDashboardViews").Exists())
				Delete.Table("IncidentSavedDashboardViews");
			if (Schema.Table("IncidentDataFreshness").Exists())
				Delete.Table("IncidentDataFreshness");
			if (Schema.Table("IncidentMetricValues").Exists())
				Delete.Table("IncidentMetricValues");
			if (Schema.Table("IncidentOperationalSnapshots").Exists())
				Delete.Table("IncidentOperationalSnapshots");
			if (Schema.Table("IncidentExternalOrganizationRefs").Exists())
				Delete.Table("IncidentExternalOrganizationRefs");
			if (Schema.Table("IncidentParticipantQualifications").Exists())
				Delete.Table("IncidentParticipantQualifications");
			if (Schema.Table("IncidentParticipantSensitive").Exists())
				Delete.Table("IncidentParticipantSensitive");
			if (Schema.Table("IncidentParticipants").Exists())
				Delete.Table("IncidentParticipants");
			if (Schema.Table("IncidentPositionAssignments").Exists())
				Delete.Table("IncidentPositionAssignments");
			if (Schema.Table("IncidentOrganizationNodes").Exists())
				Delete.Table("IncidentOrganizationNodes");
			if (Schema.Table("IncidentPositionCapabilities").Exists())
				Delete.Table("IncidentPositionCapabilities");
			if (Schema.Table("IncidentPositionDefinitions").Exists())
				Delete.Table("IncidentPositionDefinitions");
			if (Schema.Table("IncidentObjectives").Exists())
				Delete.Table("IncidentObjectives");
			if (Schema.Table("IncidentPlanWeatherBlocks").Exists())
				Delete.Table("IncidentPlanWeatherBlocks");
			if (Schema.Table("IncidentPlanVersions").Exists())
				Delete.Table("IncidentPlanVersions");
			if (Schema.Table("IncidentOperationalPeriods").Exists())
				Delete.Table("IncidentOperationalPeriods");
			if (Schema.Table("DepartmentIncidentTeamCandidates").Exists())
				Delete.Table("DepartmentIncidentTeamCandidates");
			if (Schema.Table("DepartmentIncidentTeamSlots").Exists())
				Delete.Table("DepartmentIncidentTeamSlots");
			if (Schema.Table("DepartmentIncidentTeamRotations").Exists())
				Delete.Table("DepartmentIncidentTeamRotations");
			if (Schema.Table("DepartmentIncidentTeams").Exists())
				Delete.Table("DepartmentIncidentTeams");
			if (Schema.Table("IncidentReconciliationItems").Exists())
				Delete.Table("IncidentReconciliationItems");
			if (Schema.Table("IncidentAuthorityProfileSnapshots").Exists())
				Delete.Table("IncidentAuthorityProfileSnapshots");
			if (Schema.Table("IncidentDomainAuthorities").Exists())
				Delete.Table("IncidentDomainAuthorities");
			if (Schema.Table("DepartmentIncidentAuthorityProfiles").Exists())
				Delete.Table("DepartmentIncidentAuthorityProfiles");
			if (Schema.Table("IncidentActivations").Exists())
				Delete.Table("IncidentActivations");
		}
	}
}
