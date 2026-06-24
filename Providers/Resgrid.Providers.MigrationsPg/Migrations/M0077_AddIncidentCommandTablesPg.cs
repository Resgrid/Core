using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(77)]
	public class M0077_AddIncidentCommandTablesPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("IncidentCommands".ToLower()).Exists())
			{
				Create.Table("IncidentCommands".ToLower())
					.WithColumn("IncidentCommandId".ToLower()).AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("DepartmentId".ToLower()).AsInt32().NotNullable()
					.WithColumn("CallId".ToLower()).AsInt32().NotNullable()
					.WithColumn("SourceCommandDefinitionId".ToLower()).AsInt32().Nullable()
					.WithColumn("EstablishedByUserId".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("EstablishedOn".ToLower()).AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
					.WithColumn("CurrentCommanderUserId".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("CommandPostLatitude".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("CommandPostLongitude".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("IncidentActionPlan".ToLower()).AsCustom("text").Nullable()
					.WithColumn("IcsLevel".ToLower()).AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Status".ToLower()).AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ClosedOn".ToLower()).AsDateTime2().Nullable();

				Create.Index("IX_IncidentCommands_Department_Call".ToLower())
					.OnTable("IncidentCommands".ToLower())
					.OnColumn("DepartmentId".ToLower()).Ascending()
					.OnColumn("CallId".ToLower()).Ascending();

				// At most one ACTIVE command per (department, call). Partial so a closed command and a
				// re-established active command can coexist. Backstops the check-then-insert race in
				// IncidentCommandService.EstablishCommandAsync (which adopts the winner on violation).
				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_incidentcommands_department_call_active ON incidentcommands (departmentid, callid) WHERE status = 0;");
			}

			if (!Schema.Table("CommandStructureNodes".ToLower()).Exists())
			{
				Create.Table("CommandStructureNodes".ToLower())
					.WithColumn("CommandStructureNodeId".ToLower()).AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("IncidentCommandId".ToLower()).AsCustom("citext").NotNullable()
					.WithColumn("DepartmentId".ToLower()).AsInt32().NotNullable()
					.WithColumn("CallId".ToLower()).AsInt32().NotNullable()
					.WithColumn("NodeType".ToLower()).AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Name".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("ParentNodeId".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("SupervisorUserId".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("SupervisorUnitId".ToLower()).AsInt32().Nullable()
					.WithColumn("SortOrder".ToLower()).AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("SourceRoleId".ToLower()).AsInt32().Nullable();

				Create.Index("IX_CommandStructureNodes_Department_Call".ToLower())
					.OnTable("CommandStructureNodes".ToLower())
					.OnColumn("DepartmentId".ToLower()).Ascending()
					.OnColumn("CallId".ToLower()).Ascending();
			}

			if (!Schema.Table("ResourceAssignments".ToLower()).Exists())
			{
				Create.Table("ResourceAssignments".ToLower())
					.WithColumn("ResourceAssignmentId".ToLower()).AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("IncidentCommandId".ToLower()).AsCustom("citext").NotNullable()
					.WithColumn("DepartmentId".ToLower()).AsInt32().NotNullable()
					.WithColumn("CallId".ToLower()).AsInt32().NotNullable()
					.WithColumn("CommandStructureNodeId".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("ResourceKind".ToLower()).AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ResourceId".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("AssignedByUserId".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("AssignedOn".ToLower()).AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
					.WithColumn("ReleasedOn".ToLower()).AsDateTime2().Nullable();

				Create.Index("IX_ResourceAssignments_Department_Call".ToLower())
					.OnTable("ResourceAssignments".ToLower())
					.OnColumn("DepartmentId".ToLower()).Ascending()
					.OnColumn("CallId".ToLower()).Ascending();
			}

			if (!Schema.Table("TacticalObjectives".ToLower()).Exists())
			{
				Create.Table("TacticalObjectives".ToLower())
					.WithColumn("TacticalObjectiveId".ToLower()).AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("IncidentCommandId".ToLower()).AsCustom("citext").NotNullable()
					.WithColumn("DepartmentId".ToLower()).AsInt32().NotNullable()
					.WithColumn("CallId".ToLower()).AsInt32().NotNullable()
					.WithColumn("Name".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("ObjectiveType".ToLower()).AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Status".ToLower()).AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("AutoPopulated".ToLower()).AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("CompletedByUserId".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("CompletedOn".ToLower()).AsDateTime2().Nullable()
					.WithColumn("SortOrder".ToLower()).AsInt32().NotNullable().WithDefaultValue(0);

				Create.Index("IX_TacticalObjectives_Department_Call".ToLower())
					.OnTable("TacticalObjectives".ToLower())
					.OnColumn("DepartmentId".ToLower()).Ascending()
					.OnColumn("CallId".ToLower()).Ascending();
			}

			if (!Schema.Table("IncidentTimers".ToLower()).Exists())
			{
				Create.Table("IncidentTimers".ToLower())
					.WithColumn("IncidentTimerId".ToLower()).AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("IncidentCommandId".ToLower()).AsCustom("citext").NotNullable()
					.WithColumn("DepartmentId".ToLower()).AsInt32().NotNullable()
					.WithColumn("CallId".ToLower()).AsInt32().NotNullable()
					.WithColumn("TimerType".ToLower()).AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ScopeType".ToLower()).AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ScopeId".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("Name".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("IntervalSeconds".ToLower()).AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("StartedOn".ToLower()).AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
					.WithColumn("NextDueOn".ToLower()).AsDateTime2().Nullable()
					.WithColumn("Status".ToLower()).AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("AcknowledgedOn".ToLower()).AsDateTime2().Nullable();

				Create.Index("IX_IncidentTimers_Department_Call".ToLower())
					.OnTable("IncidentTimers".ToLower())
					.OnColumn("DepartmentId".ToLower()).Ascending()
					.OnColumn("CallId".ToLower()).Ascending();
			}

			if (!Schema.Table("IncidentMapAnnotations".ToLower()).Exists())
			{
				Create.Table("IncidentMapAnnotations".ToLower())
					.WithColumn("IncidentMapAnnotationId".ToLower()).AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("IncidentCommandId".ToLower()).AsCustom("citext").NotNullable()
					.WithColumn("DepartmentId".ToLower()).AsInt32().NotNullable()
					.WithColumn("CallId".ToLower()).AsInt32().NotNullable()
					.WithColumn("AnnotationType".ToLower()).AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("GeoJson".ToLower()).AsCustom("text").Nullable()
					.WithColumn("IcsSymbolCode".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("Label".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("CreatedByUserId".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("CreatedOn".ToLower()).AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
					.WithColumn("DeletedOn".ToLower()).AsDateTime2().Nullable();

				Create.Index("IX_IncidentMapAnnotations_Department_Call".ToLower())
					.OnTable("IncidentMapAnnotations".ToLower())
					.OnColumn("DepartmentId".ToLower()).Ascending()
					.OnColumn("CallId".ToLower()).Ascending();
			}

			if (!Schema.Table("CommandLogEntries".ToLower()).Exists())
			{
				Create.Table("CommandLogEntries".ToLower())
					.WithColumn("CommandLogEntryId".ToLower()).AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("IncidentCommandId".ToLower()).AsCustom("citext").NotNullable()
					.WithColumn("DepartmentId".ToLower()).AsInt32().NotNullable()
					.WithColumn("CallId".ToLower()).AsInt32().NotNullable()
					.WithColumn("EntryType".ToLower()).AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Description".ToLower()).AsCustom("text").Nullable()
					.WithColumn("UserId".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("Latitude".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("Longitude".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("OccurredOn".ToLower()).AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);

				Create.Index("IX_CommandLogEntries_Department_Call".ToLower())
					.OnTable("CommandLogEntries".ToLower())
					.OnColumn("DepartmentId".ToLower()).Ascending()
					.OnColumn("CallId".ToLower()).Ascending();
			}

			if (!Schema.Table("CommandTransfers".ToLower()).Exists())
			{
				Create.Table("CommandTransfers".ToLower())
					.WithColumn("CommandTransferId".ToLower()).AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("IncidentCommandId".ToLower()).AsCustom("citext").NotNullable()
					.WithColumn("DepartmentId".ToLower()).AsInt32().NotNullable()
					.WithColumn("CallId".ToLower()).AsInt32().NotNullable()
					.WithColumn("FromUserId".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("ToUserId".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("TransferredOn".ToLower()).AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
					.WithColumn("Notes".ToLower()).AsCustom("text").Nullable();

				Create.Index("IX_CommandTransfers_Department_Call".ToLower())
					.OnTable("CommandTransfers".ToLower())
					.OnColumn("DepartmentId".ToLower()).Ascending()
					.OnColumn("CallId".ToLower()).Ascending();
			}
		}

		public override void Down()
		{
			// Explicit drop (the table drop below would also remove it, but be explicit to mirror the codebase pattern).
			Execute.Sql("DROP INDEX IF EXISTS ux_incidentcommands_department_call_active;");

			Delete.Table("CommandTransfers".ToLower());
			Delete.Table("CommandLogEntries".ToLower());
			Delete.Table("IncidentMapAnnotations".ToLower());
			Delete.Table("IncidentTimers".ToLower());
			Delete.Table("TacticalObjectives".ToLower());
			Delete.Table("ResourceAssignments".ToLower());
			Delete.Table("CommandStructureNodes".ToLower());
			Delete.Table("IncidentCommands".ToLower());
		}
	}
}
