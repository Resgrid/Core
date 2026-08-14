using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
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
	public class M0115_AddRunCardsPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("RunCards".ToLower()).Exists())
			{
				Create.Table("RunCards".ToLower())
					.WithColumn("RunCardId".ToLower()).AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("DepartmentId".ToLower()).AsInt32().NotNullable()
					.WithColumn("Name".ToLower()).AsCustom("citext").NotNullable()
					.WithColumn("Description".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("IsDisabled".ToLower()).AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("DispatchModeOverride".ToLower()).AsInt32().Nullable()
					.WithColumn("AutoDispatchOverride".ToLower()).AsInt32().Nullable()
					.WithColumn("MinimumStaffingLevelOverride".ToLower()).AsInt32().Nullable()
					.WithColumn("HomeStationGroupId".ToLower()).AsInt32().Nullable()
					.WithColumn("AddedOn".ToLower()).AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
					.WithColumn("AddedByUserId".ToLower()).AsCustom("citext").NotNullable()
					.WithColumn("UpdatedOn".ToLower()).AsDateTime2().Nullable()
					.WithColumn("UpdatedByUserId".ToLower()).AsCustom("citext").Nullable();

				Create.Index("IX_RunCards_DepartmentId".ToLower())
					.OnTable("RunCards".ToLower())
					.OnColumn("DepartmentId".ToLower()).Ascending();
			}

			if (!Schema.Table("RunCardTriggers".ToLower()).Exists())
			{
				Create.Table("RunCardTriggers".ToLower())
					.WithColumn("RunCardTriggerId".ToLower()).AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("RunCardId".ToLower()).AsInt32().NotNullable()
					.WithColumn("TriggerType".ToLower()).AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Priority".ToLower()).AsInt32().Nullable()
					.WithColumn("CallTypeId".ToLower()).AsInt32().Nullable()
					.WithColumn("StartsOn".ToLower()).AsDateTime2().Nullable()
					.WithColumn("EndsOn".ToLower()).AsDateTime2().Nullable();

				Create.Index("IX_RunCardTriggers_RunCardId".ToLower())
					.OnTable("RunCardTriggers".ToLower())
					.OnColumn("RunCardId".ToLower()).Ascending();
			}

			if (!Schema.Table("RunCardAlarmLevels".ToLower()).Exists())
			{
				Create.Table("RunCardAlarmLevels".ToLower())
					.WithColumn("RunCardAlarmLevelId".ToLower()).AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("RunCardId".ToLower()).AsInt32().NotNullable()
					.WithColumn("AlarmLevel".ToLower()).AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("Name".ToLower()).AsCustom("citext").Nullable();

				Create.Index("IX_RunCardAlarmLevels_RunCardId".ToLower())
					.OnTable("RunCardAlarmLevels".ToLower())
					.OnColumn("RunCardId".ToLower()).Ascending();

				// One row per level number per card.
				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_runcardalarmlevels_card_level ON runcardalarmlevels (runcardid, alarmlevel);");
			}

			if (!Schema.Table("RunCardUnitRequirements".ToLower()).Exists())
			{
				Create.Table("RunCardUnitRequirements".ToLower())
					.WithColumn("RunCardUnitRequirementId".ToLower()).AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("RunCardAlarmLevelId".ToLower()).AsInt32().NotNullable()
					.WithColumn("UnitTypeId".ToLower()).AsInt32().NotNullable()
					.WithColumn("RequiredCount".ToLower()).AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("SortOrder".ToLower()).AsInt32().NotNullable().WithDefaultValue(0);

				Create.Index("IX_RunCardUnitRequirements_LevelId".ToLower())
					.OnTable("RunCardUnitRequirements".ToLower())
					.OnColumn("RunCardAlarmLevelId".ToLower()).Ascending();
			}

			if (!Schema.Table("RunCardRoleRequirements".ToLower()).Exists())
			{
				Create.Table("RunCardRoleRequirements".ToLower())
					.WithColumn("RunCardRoleRequirementId".ToLower()).AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("RunCardAlarmLevelId".ToLower()).AsInt32().NotNullable()
					.WithColumn("PersonnelRoleId".ToLower()).AsInt32().NotNullable()
					.WithColumn("RequiredCount".ToLower()).AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("SortOrder".ToLower()).AsInt32().NotNullable().WithDefaultValue(0);

				Create.Index("IX_RunCardRoleRequirements_LevelId".ToLower())
					.OnTable("RunCardRoleRequirements".ToLower())
					.OnColumn("RunCardAlarmLevelId".ToLower()).Ascending();
			}

			if (!Schema.Table("RunCardAvailabilitySelections".ToLower()).Exists())
			{
				Create.Table("RunCardAvailabilitySelections".ToLower())
					.WithColumn("RunCardAvailabilitySelectionId".ToLower()).AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("RunCardId".ToLower()).AsInt32().NotNullable()
					.WithColumn("SelectionType".ToLower()).AsInt32().NotNullable()
					.WithColumn("UnitTypeId".ToLower()).AsInt32().Nullable()
					.WithColumn("IsCustomState".ToLower()).AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("StateId".ToLower()).AsInt32().NotNullable();

				Create.Index("IX_RunCardAvailabilitySelections_RunCardId".ToLower())
					.OnTable("RunCardAvailabilitySelections".ToLower())
					.OnColumn("RunCardId".ToLower()).Ascending();
			}

			if (!Schema.Table("StationCoverageRequirements".ToLower()).Exists())
			{
				Create.Table("StationCoverageRequirements".ToLower())
					.WithColumn("StationCoverageRequirementId".ToLower()).AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("DepartmentId".ToLower()).AsInt32().NotNullable()
					.WithColumn("DepartmentGroupId".ToLower()).AsInt32().NotNullable()
					.WithColumn("UnitTypeId".ToLower()).AsInt32().Nullable()
					.WithColumn("PersonnelRoleId".ToLower()).AsInt32().Nullable()
					.WithColumn("MinimumAvailableCount".ToLower()).AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("RadiusMeters".ToLower()).AsInt32().Nullable()
					.WithColumn("IsEnabled".ToLower()).AsBoolean().NotNullable().WithDefaultValue(true);

				Create.Index("IX_StationCoverageRequirements_DepartmentId".ToLower())
					.OnTable("StationCoverageRequirements".ToLower())
					.OnColumn("DepartmentId".ToLower()).Ascending();
			}

			if (!Schema.Table("Calls".ToLower()).Column("AlarmLevel".ToLower()).Exists())
			{
				Alter.Table("Calls".ToLower())
					.AddColumn("AlarmLevel".ToLower()).AsInt32().NotNullable().WithDefaultValue(1);
			}

			if (!Schema.Table("Calls".ToLower()).Column("ActiveRunCardId".ToLower()).Exists())
			{
				Alter.Table("Calls".ToLower())
					.AddColumn("ActiveRunCardId".ToLower()).AsInt32().Nullable();
			}
		}

		public override void Down()
		{
			if (Schema.Table("Calls".ToLower()).Column("ActiveRunCardId".ToLower()).Exists())
				Delete.Column("ActiveRunCardId".ToLower()).FromTable("Calls".ToLower());

			if (Schema.Table("Calls".ToLower()).Column("AlarmLevel".ToLower()).Exists())
				Delete.Column("AlarmLevel".ToLower()).FromTable("Calls".ToLower());

			if (Schema.Table("StationCoverageRequirements".ToLower()).Exists())
				Delete.Table("StationCoverageRequirements".ToLower());

			if (Schema.Table("RunCardAvailabilitySelections".ToLower()).Exists())
				Delete.Table("RunCardAvailabilitySelections".ToLower());

			if (Schema.Table("RunCardRoleRequirements".ToLower()).Exists())
				Delete.Table("RunCardRoleRequirements".ToLower());

			if (Schema.Table("RunCardUnitRequirements".ToLower()).Exists())
				Delete.Table("RunCardUnitRequirements".ToLower());

			if (Schema.Table("RunCardAlarmLevels".ToLower()).Exists())
			{
				Execute.Sql("DROP INDEX IF EXISTS ux_runcardalarmlevels_card_level;");
				Delete.Table("RunCardAlarmLevels".ToLower());
			}

			if (Schema.Table("RunCardTriggers".ToLower()).Exists())
				Delete.Table("RunCardTriggers".ToLower());

			if (Schema.Table("RunCards".ToLower()).Exists())
				Delete.Table("RunCards".ToLower());
		}
	}
}
