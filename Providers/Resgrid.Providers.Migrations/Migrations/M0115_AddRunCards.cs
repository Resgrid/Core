using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Run card dispatch system tables: RunCards (response plan header with per-card mode/auto-dispatch/
	/// staffing overrides), RunCardTriggers (OR'd priority/type match conditions with optional time
	/// windows), RunCardAlarmLevels (1..N additive escalation levels), RunCardUnitRequirements /
	/// RunCardRoleRequirements (unit-type and personnel-role counts per level),
	/// RunCardAvailabilitySelections (which unit/personnel statuses and staffing levels count as
	/// dispatchable) and StationCoverageRequirements (minimum station coverage driving move-up/backfill
	/// recommendations). Also adds Calls.AlarmLevel and Calls.ActiveRunCardId for escalation tracking.
	/// </summary>
	[Migration(115)]
	public class M0115_AddRunCards : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("RunCards").Exists())
			{
				Create.Table("RunCards")
					.WithColumn("RunCardId").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("Name").AsString(100).NotNullable()
					.WithColumn("Description").AsString(500).Nullable()
					.WithColumn("IsDisabled").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("DispatchModeOverride").AsInt32().Nullable()
					.WithColumn("AutoDispatchOverride").AsInt32().Nullable()
					.WithColumn("MinimumStaffingLevelOverride").AsInt32().Nullable()
					.WithColumn("HomeStationGroupId").AsInt32().Nullable()
					.WithColumn("AddedOn").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
					.WithColumn("AddedByUserId").AsString(450).NotNullable()
					.WithColumn("UpdatedOn").AsDateTime2().Nullable()
					.WithColumn("UpdatedByUserId").AsString(450).Nullable();

				Create.Index("IX_RunCards_DepartmentId")
					.OnTable("RunCards")
					.OnColumn("DepartmentId").Ascending();
			}

			if (!Schema.Table("RunCardTriggers").Exists())
			{
				Create.Table("RunCardTriggers")
					.WithColumn("RunCardTriggerId").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("RunCardId").AsInt32().NotNullable()
					.WithColumn("TriggerType").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Priority").AsInt32().Nullable()
					.WithColumn("CallTypeId").AsInt32().Nullable()
					.WithColumn("StartsOn").AsDateTime2().Nullable()
					.WithColumn("EndsOn").AsDateTime2().Nullable();

				Create.Index("IX_RunCardTriggers_RunCardId")
					.OnTable("RunCardTriggers")
					.OnColumn("RunCardId").Ascending();
			}

			if (!Schema.Table("RunCardAlarmLevels").Exists())
			{
				Create.Table("RunCardAlarmLevels")
					.WithColumn("RunCardAlarmLevelId").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("RunCardId").AsInt32().NotNullable()
					.WithColumn("AlarmLevel").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("Name").AsString(100).Nullable();

				Create.Index("IX_RunCardAlarmLevels_RunCardId")
					.OnTable("RunCardAlarmLevels")
					.OnColumn("RunCardId").Ascending();

				// One row per level number per card.
				Execute.Sql("CREATE UNIQUE NONCLUSTERED INDEX UX_RunCardAlarmLevels_Card_Level ON RunCardAlarmLevels (RunCardId, AlarmLevel);");
			}

			if (!Schema.Table("RunCardUnitRequirements").Exists())
			{
				Create.Table("RunCardUnitRequirements")
					.WithColumn("RunCardUnitRequirementId").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("RunCardAlarmLevelId").AsInt32().NotNullable()
					.WithColumn("UnitTypeId").AsInt32().NotNullable()
					.WithColumn("RequiredCount").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("SortOrder").AsInt32().NotNullable().WithDefaultValue(0);

				Create.Index("IX_RunCardUnitRequirements_LevelId")
					.OnTable("RunCardUnitRequirements")
					.OnColumn("RunCardAlarmLevelId").Ascending();
			}

			if (!Schema.Table("RunCardRoleRequirements").Exists())
			{
				Create.Table("RunCardRoleRequirements")
					.WithColumn("RunCardRoleRequirementId").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("RunCardAlarmLevelId").AsInt32().NotNullable()
					.WithColumn("PersonnelRoleId").AsInt32().NotNullable()
					.WithColumn("RequiredCount").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("SortOrder").AsInt32().NotNullable().WithDefaultValue(0);

				Create.Index("IX_RunCardRoleRequirements_LevelId")
					.OnTable("RunCardRoleRequirements")
					.OnColumn("RunCardAlarmLevelId").Ascending();
			}

			if (!Schema.Table("RunCardAvailabilitySelections").Exists())
			{
				Create.Table("RunCardAvailabilitySelections")
					.WithColumn("RunCardAvailabilitySelectionId").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("RunCardId").AsInt32().NotNullable()
					.WithColumn("SelectionType").AsInt32().NotNullable()
					.WithColumn("UnitTypeId").AsInt32().Nullable()
					.WithColumn("IsCustomState").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("StateId").AsInt32().NotNullable();

				Create.Index("IX_RunCardAvailabilitySelections_RunCardId")
					.OnTable("RunCardAvailabilitySelections")
					.OnColumn("RunCardId").Ascending();
			}

			if (!Schema.Table("StationCoverageRequirements").Exists())
			{
				Create.Table("StationCoverageRequirements")
					.WithColumn("StationCoverageRequirementId").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("DepartmentGroupId").AsInt32().NotNullable()
					.WithColumn("UnitTypeId").AsInt32().Nullable()
					.WithColumn("PersonnelRoleId").AsInt32().Nullable()
					.WithColumn("MinimumAvailableCount").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("RadiusMeters").AsInt32().Nullable()
					.WithColumn("IsEnabled").AsBoolean().NotNullable().WithDefaultValue(true);

				Create.Index("IX_StationCoverageRequirements_DepartmentId")
					.OnTable("StationCoverageRequirements")
					.OnColumn("DepartmentId").Ascending();
			}

			if (!Schema.Table("Calls").Column("AlarmLevel").Exists())
			{
				Alter.Table("Calls")
					.AddColumn("AlarmLevel").AsInt32().NotNullable().WithDefaultValue(1);
			}

			if (!Schema.Table("Calls").Column("ActiveRunCardId").Exists())
			{
				Alter.Table("Calls")
					.AddColumn("ActiveRunCardId").AsInt32().Nullable();
			}
		}

		public override void Down()
		{
			if (Schema.Table("Calls").Column("ActiveRunCardId").Exists())
				Delete.Column("ActiveRunCardId").FromTable("Calls");

			if (Schema.Table("Calls").Column("AlarmLevel").Exists())
				Delete.Column("AlarmLevel").FromTable("Calls");

			if (Schema.Table("StationCoverageRequirements").Exists())
				Delete.Table("StationCoverageRequirements");

			if (Schema.Table("RunCardAvailabilitySelections").Exists())
				Delete.Table("RunCardAvailabilitySelections");

			if (Schema.Table("RunCardRoleRequirements").Exists())
				Delete.Table("RunCardRoleRequirements");

			if (Schema.Table("RunCardUnitRequirements").Exists())
				Delete.Table("RunCardUnitRequirements");

			if (Schema.Table("RunCardAlarmLevels").Exists())
			{
				Execute.Sql("DROP INDEX IF EXISTS UX_RunCardAlarmLevels_Card_Level ON RunCardAlarmLevels;");
				Delete.Table("RunCardAlarmLevels");
			}

			if (Schema.Table("RunCardTriggers").Exists())
				Delete.Table("RunCardTriggers");

			if (Schema.Table("RunCards").Exists())
				Delete.Table("RunCards");
		}
	}
}
