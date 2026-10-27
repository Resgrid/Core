using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Incident Back Office LI-D Coordination (plan §9.25, §9.25.3a): boards, lanes and interagency share grants; lifeline
	/// status history, open actions, decisions in flight and situational features. No protected column: boards are shared with
	/// partner agencies. No foreign keys (the IC precedent): tenant ownership is enforced in the service layer and the
	/// department purge (IncidentBackOfficeDepartmentCleanup) removes every row. Existence-guarded for safe retry.
	/// </summary>
	[Migration(297)]
	public class M0297_AddIncidentCoordination : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("CoordinationBoards").Exists())
			{
				Create.Table("CoordinationBoards")
					.WithColumn("CoordinationBoardId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).Nullable()
					.WithColumn("Name").AsString(200).NotNullable()
					.WithColumn("Scope").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("DefaultStalenessMinutes").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("IsArchived").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("IsFrozen").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("FrozenOn").AsDateTime2().Nullable()
					.WithColumn("SnapshotArtifactId").AsString(36).Nullable()
					.WithColumn("Description").AsString(1000).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CoordinationBoards_Department' AND object_id = OBJECT_ID('CoordinationBoards')) CREATE NONCLUSTERED INDEX [IX_CoordinationBoards_Department] ON [CoordinationBoards] ([DepartmentId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CoordinationBoards_Activation' AND object_id = OBJECT_ID('CoordinationBoards')) CREATE NONCLUSTERED INDEX [IX_CoordinationBoards_Activation] ON [CoordinationBoards] ([DepartmentId], [IncidentActivationId]);");
			}

			if (!Schema.Table("CoordinationBoardLanes").Exists())
			{
				Create.Table("CoordinationBoardLanes")
					.WithColumn("CoordinationBoardLaneId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("CoordinationBoardId").AsString(36).NotNullable()
					.WithColumn("LaneType").AsString(64).NotNullable()
					.WithColumn("Name").AsString(200).NotNullable()
					.WithColumn("SortOrder").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("FilterJson").AsString(int.MaxValue).Nullable()
					.WithColumn("DisplayRule").AsString(32).Nullable()
					.WithColumn("StalenessMinutes").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("AlarmOnStale").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("LastSourceOn").AsDateTime2().Nullable()
					.WithColumn("StaleSince").AsDateTime2().Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CoordinationBoardLanes_Department' AND object_id = OBJECT_ID('CoordinationBoardLanes')) CREATE NONCLUSTERED INDEX [IX_CoordinationBoardLanes_Department] ON [CoordinationBoardLanes] ([DepartmentId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CoordinationBoardLanes_Board' AND object_id = OBJECT_ID('CoordinationBoardLanes')) CREATE NONCLUSTERED INDEX [IX_CoordinationBoardLanes_Board] ON [CoordinationBoardLanes] ([DepartmentId], [CoordinationBoardId], [SortOrder]);");
			}

			if (!Schema.Table("CoordinationShareGrants").Exists())
			{
				Create.Table("CoordinationShareGrants")
					.WithColumn("CoordinationShareGrantId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("CoordinationBoardId").AsString(36).NotNullable()
					.WithColumn("CoordinationBoardLaneId").AsString(36).Nullable()
					.WithColumn("PartnerContactId").AsString(128).Nullable()
					.WithColumn("PartnerName").AsString(200).NotNullable()
					.WithColumn("Scope").AsString(32).Nullable()
					.WithColumn("TokenHash").AsString(128).NotNullable()
					.WithColumn("ExpiresOn").AsDateTime2().NotNullable()
					.WithColumn("RevokedOn").AsDateTime2().Nullable()
					.WithColumn("RevokedByUserId").AsString(128).Nullable()
					.WithColumn("LastAccessedOn").AsDateTime2().Nullable()
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CoordinationShareGrants_Department' AND object_id = OBJECT_ID('CoordinationShareGrants')) CREATE NONCLUSTERED INDEX [IX_CoordinationShareGrants_Department] ON [CoordinationShareGrants] ([DepartmentId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CoordinationShareGrants_Board' AND object_id = OBJECT_ID('CoordinationShareGrants')) CREATE NONCLUSTERED INDEX [IX_CoordinationShareGrants_Board] ON [CoordinationShareGrants] ([DepartmentId], [CoordinationBoardId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_CoordinationShareGrants_Token' AND object_id = OBJECT_ID('CoordinationShareGrants')) CREATE UNIQUE NONCLUSTERED INDEX [UX_CoordinationShareGrants_Token] ON [CoordinationShareGrants] ([TokenHash]);");
			}

			if (!Schema.Table("IncidentLifelineStatuses").Exists())
			{
				Create.Table("IncidentLifelineStatuses")
					.WithColumn("IncidentLifelineStatusId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("Lifeline").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Component").AsString(100).NotNullable()
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("AsOf").AsDateTime2().NotNullable()
					.WithColumn("Source").AsString(200).Nullable()
					.WithColumn("Notes").AsString(1000).Nullable()
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentLifelineStatuses_Activation' AND object_id = OBJECT_ID('IncidentLifelineStatuses')) CREATE NONCLUSTERED INDEX [IX_IncidentLifelineStatuses_Activation] ON [IncidentLifelineStatuses] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentLifelineStatuses_Lifeline' AND object_id = OBJECT_ID('IncidentLifelineStatuses')) CREATE NONCLUSTERED INDEX [IX_IncidentLifelineStatuses_Lifeline] ON [IncidentLifelineStatuses] ([DepartmentId], [IncidentActivationId], [Lifeline], [AsOf]);");
			}

			if (!Schema.Table("IncidentOpenActions").Exists())
			{
				Create.Table("IncidentOpenActions")
					.WithColumn("IncidentOpenActionId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("Title").AsString(200).NotNullable()
					.WithColumn("Description").AsString(2000).Nullable()
					.WithColumn("OwnerUserId").AsString(128).Nullable()
					.WithColumn("OwnerName").AsString(200).Nullable()
					.WithColumn("DueOn").AsDateTime2().Nullable()
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("NotifiedOn").AsDateTime2().Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentOpenActions_Activation' AND object_id = OBJECT_ID('IncidentOpenActions')) CREATE NONCLUSTERED INDEX [IX_IncidentOpenActions_Activation] ON [IncidentOpenActions] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentOpenActions_Due' AND object_id = OBJECT_ID('IncidentOpenActions')) CREATE NONCLUSTERED INDEX [IX_IncidentOpenActions_Due] ON [IncidentOpenActions] ([DepartmentId], [IncidentActivationId], [DueOn]);");
			}

			if (!Schema.Table("IncidentDecisions").Exists())
			{
				Create.Table("IncidentDecisions")
					.WithColumn("IncidentDecisionId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("Question").AsString(1000).NotNullable()
					.WithColumn("OptionsJson").AsString(int.MaxValue).Nullable()
					.WithColumn("OwnerUserId").AsString(128).Nullable()
					.WithColumn("OwnerName").AsString(200).Nullable()
					.WithColumn("DeadlineOn").AsDateTime2().Nullable()
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Decision").AsString(2000).Nullable()
					.WithColumn("DecidedOn").AsDateTime2().Nullable()
					.WithColumn("ClosedToRecordId").AsString(36).Nullable()
					.WithColumn("NotifiedOn").AsDateTime2().Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentDecisions_Activation' AND object_id = OBJECT_ID('IncidentDecisions')) CREATE NONCLUSTERED INDEX [IX_IncidentDecisions_Activation] ON [IncidentDecisions] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentDecisions_Deadline' AND object_id = OBJECT_ID('IncidentDecisions')) CREATE NONCLUSTERED INDEX [IX_IncidentDecisions_Deadline] ON [IncidentDecisions] ([DepartmentId], [IncidentActivationId], [DeadlineOn]);");
			}

			if (!Schema.Table("IncidentSituationalFeatures").Exists())
			{
				Create.Table("IncidentSituationalFeatures")
					.WithColumn("IncidentSituationalFeatureId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IncidentActivationId").AsString(36).NotNullable()
					.WithColumn("FeatureType").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Name").AsString(200).NotNullable()
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("GeometryJson").AsString(int.MaxValue).Nullable()
					.WithColumn("CapacityTotal").AsInt32().Nullable()
					.WithColumn("CapacityAvailable").AsInt32().Nullable()
					.WithColumn("ThresholdPercent").AsDecimal(9, 4).Nullable()
					.WithColumn("AsOf").AsDateTime2().NotNullable()
					.WithColumn("Source").AsString(200).Nullable()
					.WithColumn("Notes").AsString(1000).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentSituationalFeatures_Activation' AND object_id = OBJECT_ID('IncidentSituationalFeatures')) CREATE NONCLUSTERED INDEX [IX_IncidentSituationalFeatures_Activation] ON [IncidentSituationalFeatures] ([DepartmentId], [IncidentActivationId]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IncidentSituationalFeatures_Type' AND object_id = OBJECT_ID('IncidentSituationalFeatures')) CREATE NONCLUSTERED INDEX [IX_IncidentSituationalFeatures_Type] ON [IncidentSituationalFeatures] ([DepartmentId], [IncidentActivationId], [FeatureType]);");
			}
		}

		public override void Down()
		{
			if (Schema.Table("IncidentSituationalFeatures").Exists())
				Delete.Table("IncidentSituationalFeatures");
			if (Schema.Table("IncidentDecisions").Exists())
				Delete.Table("IncidentDecisions");
			if (Schema.Table("IncidentOpenActions").Exists())
				Delete.Table("IncidentOpenActions");
			if (Schema.Table("IncidentLifelineStatuses").Exists())
				Delete.Table("IncidentLifelineStatuses");
			if (Schema.Table("CoordinationShareGrants").Exists())
				Delete.Table("CoordinationShareGrants");
			if (Schema.Table("CoordinationBoardLanes").Exists())
				Delete.Table("CoordinationBoardLanes");
			if (Schema.Table("CoordinationBoards").Exists())
				Delete.Table("CoordinationBoards");
		}
	}
}
