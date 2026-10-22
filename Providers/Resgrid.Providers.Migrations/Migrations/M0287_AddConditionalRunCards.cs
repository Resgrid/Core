using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Conditional run cards (docs/architecture/run-cards-conditional-responses.md in int-Coordination):
	/// RunCardDecisionNodes (an alarm level's yes/no decision graph, pointers by node key), ResponsePackages and
	/// ResponsePackageRows (reusable named requirement sets), RunCardTargets ("any of" targets of package rows and condition
	/// operands), RunCardDecisions (pinned decisions per call, card and level), CallOutstandingRequirements (required
	/// resources still awaited by a call), RunCardAlarmLevels.ResponsePackageId, RunCards.RelaxUnfilledRows and
	/// WaitingCallAutoDispatchOverride, and the optional response code snapshot on CallDispatchUnits. Seeds the
	/// Dispatch.RunCardConditions feature flag off. Additive only: with the flag off every card behaves as before.
	/// </summary>
	[Migration(287)]
	public class M0287_AddConditionalRunCards : Migration
	{
		// Keep FlagKey in sync with Resgrid.Model.FeatureFlagKeys.DispatchRunCardConditions.
		private const string FlagKey = "Dispatch.RunCardConditions";

		public override void Up()
		{
			if (!Schema.Table("ResponsePackages").Exists())
			{
				Create.Table("ResponsePackages")
					.WithColumn("ResponsePackageId").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("Name").AsString(100).NotNullable()
					.WithColumn("Description").AsString(500).Nullable()
					.WithColumn("IsRetired").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("AddedOn").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
					.WithColumn("AddedByUserId").AsString(450).Nullable()
					.WithColumn("UpdatedOn").AsDateTime2().Nullable()
					.WithColumn("UpdatedByUserId").AsString(450).Nullable();

				Create.Index("IX_ResponsePackages_DepartmentId")
					.OnTable("ResponsePackages")
					.OnColumn("DepartmentId").Ascending();
			}

			if (!Schema.Table("ResponsePackageRows").Exists())
			{
				Create.Table("ResponsePackageRows")
					.WithColumn("ResponsePackageRowId").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("ResponsePackageId").AsInt32().NotNullable()
					.WithColumn("Label").AsString(100).Nullable()
					.WithColumn("FillMode").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("RequiredCount").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("StatusFilter").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("StatusBaseTypes").AsString(200).Nullable()
					.WithColumn("ResponseCodeId").AsInt32().Nullable()
					.WithColumn("SubstitutePackageId").AsInt32().Nullable()
					.WithColumn("KeepOutstanding").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("SortOrder").AsInt32().NotNullable().WithDefaultValue(0);

				Create.Index("IX_ResponsePackageRows_ResponsePackageId")
					.OnTable("ResponsePackageRows")
					.OnColumn("ResponsePackageId").Ascending();
			}

			if (!Schema.Table("RunCardTargets").Exists())
			{
				Create.Table("RunCardTargets")
					.WithColumn("RunCardTargetId").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("OwnerKind").AsInt32().NotNullable()
					.WithColumn("OwnerId").AsInt32().NotNullable()
					.WithColumn("TargetKind").AsInt32().NotNullable()
					.WithColumn("TargetRefId").AsInt32().Nullable();

				Create.Index("IX_RunCardTargets_Owner")
					.OnTable("RunCardTargets")
					.OnColumn("OwnerKind").Ascending()
					.OnColumn("OwnerId").Ascending();

				Create.Index("IX_RunCardTargets_DepartmentId")
					.OnTable("RunCardTargets")
					.OnColumn("DepartmentId").Ascending();
			}

			if (!Schema.Table("RunCardDecisionNodes").Exists())
			{
				Create.Table("RunCardDecisionNodes")
					.WithColumn("RunCardDecisionNodeId").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("RunCardAlarmLevelId").AsInt32().NotNullable()
					.WithColumn("NodeKey").AsString(36).NotNullable()
					.WithColumn("IsRoot").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("NodeKind").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("Label").AsString(200).Nullable()
					.WithColumn("ConditionType").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("SubjectStatusFilter").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("SubjectStatusBaseTypes").AsString(200).Nullable()
					.WithColumn("ReferenceStatusFilter").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ReferenceStatusBaseTypes").AsString(200).Nullable()
					.WithColumn("ThresholdSeconds").AsInt32().Nullable()
					.WithColumn("RequiredCount").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("ParametersJson").AsString(int.MaxValue).Nullable()
					.WithColumn("YesNodeKey").AsString(36).Nullable()
					.WithColumn("NoNodeKey").AsString(36).Nullable()
					.WithColumn("ResponsePackageId").AsInt32().Nullable()
					.WithColumn("AdviceText").AsString(1000).Nullable()
					.WithColumn("SortOrder").AsInt32().NotNullable().WithDefaultValue(0);

				Create.Index("IX_RunCardDecisionNodes_RunCardAlarmLevelId")
					.OnTable("RunCardDecisionNodes")
					.OnColumn("RunCardAlarmLevelId").Ascending();

				// A node key is what the Yes/No pointers refer to, so it is unique within its level.
				Execute.Sql("CREATE UNIQUE NONCLUSTERED INDEX UX_RunCardDecisionNodes_Level_Key ON RunCardDecisionNodes (RunCardAlarmLevelId, NodeKey);");
			}

			if (!Schema.Table("RunCardDecisions").Exists())
			{
				Create.Table("RunCardDecisions")
					.WithColumn("RunCardDecisionId").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("CallId").AsInt32().NotNullable()
					.WithColumn("RunCardId").AsInt32().NotNullable()
					.WithColumn("AlarmLevel").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("LeafNodeKey").AsString(36).Nullable()
					.WithColumn("LeafLabel").AsString(200).Nullable()
					.WithColumn("ResponsePackageId").AsInt32().Nullable()
					.WithColumn("PackageSnapshotJson").AsString(int.MaxValue).Nullable()
					.WithColumn("PathJson").AsString(int.MaxValue).Nullable()
					.WithColumn("Source").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("CreatedOn").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
					.WithColumn("CreatedByUserId").AsString(450).Nullable();

				Create.Index("IX_RunCardDecisions_Call_Card_Level")
					.OnTable("RunCardDecisions")
					.OnColumn("CallId").Ascending()
					.OnColumn("RunCardId").Ascending()
					.OnColumn("AlarmLevel").Ascending()
					.OnColumn("CreatedOn").Descending();

				Create.Index("IX_RunCardDecisions_DepartmentId")
					.OnTable("RunCardDecisions")
					.OnColumn("DepartmentId").Ascending();
			}

			if (!Schema.Table("CallOutstandingRequirements").Exists())
			{
				Create.Table("CallOutstandingRequirements")
					.WithColumn("CallOutstandingRequirementId").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("CallId").AsInt32().NotNullable()
					.WithColumn("Label").AsString(200).Nullable()
					.WithColumn("TargetsJson").AsString(int.MaxValue).Nullable()
					.WithColumn("RequiredCount").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("ResolvedCount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("StatusFilter").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("StatusBaseTypes").AsString(200).Nullable()
					.WithColumn("ResponseCodeId").AsInt32().Nullable()
					.WithColumn("ResponseCode").AsString(16).Nullable()
					.WithColumn("RunCardId").AsInt32().Nullable()
					.WithColumn("AlarmLevel").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("ResponsePackageRowId").AsInt32().Nullable()
					.WithColumn("SentInstead").AsString(200).Nullable()
					.WithColumn("CreatedOn").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
					.WithColumn("CreatedByUserId").AsString(450).Nullable()
					.WithColumn("ResolvedOn").AsDateTime2().Nullable()
					.WithColumn("ResolvedByUserId").AsString(450).Nullable()
					.WithColumn("Resolution").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ResolutionNote").AsString(500).Nullable()
					.WithColumn("ResolvedByUnitId").AsInt32().Nullable()
					.WithColumn("ClaimedOn").AsDateTime2().Nullable()
					.WithColumn("ClaimedByUnitId").AsInt32().Nullable();

				Create.Index("IX_CallOutstandingRequirements_CallId")
					.OnTable("CallOutstandingRequirements")
					.OnColumn("CallId").Ascending();

				// The unit-available check and the awaiting-resources list read the department's open requirements.
				Execute.Sql("CREATE NONCLUSTERED INDEX IX_CallOutstandingRequirements_Department_Open ON CallOutstandingRequirements (DepartmentId) WHERE ResolvedOn IS NULL;");
			}

			if (!Schema.Table("RunCardAlarmLevels").Column("ResponsePackageId").Exists())
				Alter.Table("RunCardAlarmLevels").AddColumn("ResponsePackageId").AsInt32().Nullable();

			if (!Schema.Table("RunCards").Column("RelaxUnfilledRows").Exists())
				Alter.Table("RunCards").AddColumn("RelaxUnfilledRows").AsBoolean().NotNullable().WithDefaultValue(false);

			if (!Schema.Table("RunCards").Column("WaitingCallAutoDispatchOverride").Exists())
				Alter.Table("RunCards").AddColumn("WaitingCallAutoDispatchOverride").AsInt32().Nullable();

			if (!Schema.Table("CallDispatchUnits").Column("ResponseCodeId").Exists())
				Alter.Table("CallDispatchUnits").AddColumn("ResponseCodeId").AsInt32().Nullable();

			if (!Schema.Table("CallDispatchUnits").Column("ResponseCode").Exists())
				Alter.Table("CallDispatchUnits").AddColumn("ResponseCode").AsString(16).Nullable();

			if (!Schema.Table("CallDispatchUnits").Column("ResponseCodeName").Exists())
				Alter.Table("CallDispatchUnits").AddColumn("ResponseCodeName").AsString(100).Nullable();

			// Seeded OFF: conditional features stay hidden until the flag is enabled globally or per department.
			Execute.Sql(
				"IF NOT EXISTS (SELECT 1 FROM [FeatureFlags] WHERE [FlagKey] = '" + FlagKey + "') " +
				"INSERT INTO [FeatureFlags] ([FlagKey], [Name], [Description], [Category], [IsEnabledGlobally]) " +
				"VALUES ('" + FlagKey + "', " +
				"'Conditional Run Cards', " +
				"'Decision graphs, reusable response packages, any-of targets, optional response codes, outstanding requirements with awaiting-resource alerts, and the run card template gallery. Requires Dispatch.RunCards. Seeded off; enable globally or per-department to roll out.', " +
				"'Dispatch', 0);");
		}

		public override void Down()
		{
			Delete.FromTable("FeatureFlags").Row(new { FlagKey = FlagKey });

			if (Schema.Table("CallDispatchUnits").Column("ResponseCodeName").Exists())
				Delete.Column("ResponseCodeName").FromTable("CallDispatchUnits");

			if (Schema.Table("CallDispatchUnits").Column("ResponseCode").Exists())
				Delete.Column("ResponseCode").FromTable("CallDispatchUnits");

			if (Schema.Table("CallDispatchUnits").Column("ResponseCodeId").Exists())
				Delete.Column("ResponseCodeId").FromTable("CallDispatchUnits");

			if (Schema.Table("RunCards").Column("WaitingCallAutoDispatchOverride").Exists())
				Delete.Column("WaitingCallAutoDispatchOverride").FromTable("RunCards");

			// FluentMigrator's SQL Server generator drops the column's default constraint before the column.
			if (Schema.Table("RunCards").Column("RelaxUnfilledRows").Exists())
				Delete.Column("RelaxUnfilledRows").FromTable("RunCards");

			if (Schema.Table("RunCardAlarmLevels").Column("ResponsePackageId").Exists())
				Delete.Column("ResponsePackageId").FromTable("RunCardAlarmLevels");

			if (Schema.Table("CallOutstandingRequirements").Exists())
				Delete.Table("CallOutstandingRequirements");

			if (Schema.Table("RunCardDecisions").Exists())
				Delete.Table("RunCardDecisions");

			if (Schema.Table("RunCardDecisionNodes").Exists())
				Delete.Table("RunCardDecisionNodes");

			if (Schema.Table("RunCardTargets").Exists())
				Delete.Table("RunCardTargets");

			if (Schema.Table("ResponsePackageRows").Exists())
				Delete.Table("ResponsePackageRows");

			if (Schema.Table("ResponsePackages").Exists())
				Delete.Table("ResponsePackages");
		}
	}
}
