using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(77)]
	public class M0077_AddIncidentCommandTables : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("IncidentCommands").Exists())
			{
				Create.Table("IncidentCommands")
					.WithColumn("IncidentCommandId").AsString(128).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("CallId").AsInt32().NotNullable()
					.WithColumn("SourceCommandDefinitionId").AsInt32().Nullable()
					.WithColumn("EstablishedByUserId").AsString(450).Nullable()
					.WithColumn("EstablishedOn").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
					.WithColumn("CurrentCommanderUserId").AsString(450).Nullable()
					.WithColumn("CommandPostLatitude").AsString(int.MaxValue).Nullable()
					.WithColumn("CommandPostLongitude").AsString(int.MaxValue).Nullable()
					.WithColumn("IncidentActionPlan").AsString(int.MaxValue).Nullable()
					.WithColumn("IcsLevel").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ClosedOn").AsDateTime2().Nullable();

				Create.Index("IX_IncidentCommands_Department_Call")
					.OnTable("IncidentCommands")
					.OnColumn("DepartmentId").Ascending()
					.OnColumn("CallId").Ascending();
			}

			if (!Schema.Table("CommandStructureNodes").Exists())
			{
				Create.Table("CommandStructureNodes")
					.WithColumn("CommandStructureNodeId").AsString(128).NotNullable().PrimaryKey()
					.WithColumn("IncidentCommandId").AsString(128).NotNullable()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("CallId").AsInt32().NotNullable()
					.WithColumn("NodeType").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Name").AsString(int.MaxValue).Nullable()
					.WithColumn("ParentNodeId").AsString(128).Nullable()
					.WithColumn("SupervisorUserId").AsString(450).Nullable()
					.WithColumn("SupervisorUnitId").AsInt32().Nullable()
					.WithColumn("SortOrder").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("SourceRoleId").AsInt32().Nullable();

				Create.Index("IX_CommandStructureNodes_Department_Call")
					.OnTable("CommandStructureNodes")
					.OnColumn("DepartmentId").Ascending()
					.OnColumn("CallId").Ascending();
			}

			if (!Schema.Table("ResourceAssignments").Exists())
			{
				Create.Table("ResourceAssignments")
					.WithColumn("ResourceAssignmentId").AsString(128).NotNullable().PrimaryKey()
					.WithColumn("IncidentCommandId").AsString(128).NotNullable()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("CallId").AsInt32().NotNullable()
					.WithColumn("CommandStructureNodeId").AsString(128).Nullable()
					.WithColumn("ResourceKind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ResourceId").AsString(450).Nullable()
					.WithColumn("AssignedByUserId").AsString(450).Nullable()
					.WithColumn("AssignedOn").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
					.WithColumn("ReleasedOn").AsDateTime2().Nullable();

				Create.Index("IX_ResourceAssignments_Department_Call")
					.OnTable("ResourceAssignments")
					.OnColumn("DepartmentId").Ascending()
					.OnColumn("CallId").Ascending();
			}

			if (!Schema.Table("TacticalObjectives").Exists())
			{
				Create.Table("TacticalObjectives")
					.WithColumn("TacticalObjectiveId").AsString(128).NotNullable().PrimaryKey()
					.WithColumn("IncidentCommandId").AsString(128).NotNullable()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("CallId").AsInt32().NotNullable()
					.WithColumn("Name").AsString(int.MaxValue).Nullable()
					.WithColumn("ObjectiveType").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("AutoPopulated").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("CompletedByUserId").AsString(450).Nullable()
					.WithColumn("CompletedOn").AsDateTime2().Nullable()
					.WithColumn("SortOrder").AsInt32().NotNullable().WithDefaultValue(0);

				Create.Index("IX_TacticalObjectives_Department_Call")
					.OnTable("TacticalObjectives")
					.OnColumn("DepartmentId").Ascending()
					.OnColumn("CallId").Ascending();
			}

			if (!Schema.Table("IncidentTimers").Exists())
			{
				Create.Table("IncidentTimers")
					.WithColumn("IncidentTimerId").AsString(128).NotNullable().PrimaryKey()
					.WithColumn("IncidentCommandId").AsString(128).NotNullable()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("CallId").AsInt32().NotNullable()
					.WithColumn("TimerType").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ScopeType").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ScopeId").AsString(128).Nullable()
					.WithColumn("Name").AsString(int.MaxValue).Nullable()
					.WithColumn("IntervalSeconds").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("StartedOn").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
					.WithColumn("NextDueOn").AsDateTime2().Nullable()
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("AcknowledgedOn").AsDateTime2().Nullable();

				Create.Index("IX_IncidentTimers_Department_Call")
					.OnTable("IncidentTimers")
					.OnColumn("DepartmentId").Ascending()
					.OnColumn("CallId").Ascending();
			}

			if (!Schema.Table("IncidentMapAnnotations").Exists())
			{
				Create.Table("IncidentMapAnnotations")
					.WithColumn("IncidentMapAnnotationId").AsString(128).NotNullable().PrimaryKey()
					.WithColumn("IncidentCommandId").AsString(128).NotNullable()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("CallId").AsInt32().NotNullable()
					.WithColumn("AnnotationType").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("GeoJson").AsString(int.MaxValue).Nullable()
					.WithColumn("IcsSymbolCode").AsString(int.MaxValue).Nullable()
					.WithColumn("Label").AsString(int.MaxValue).Nullable()
					.WithColumn("CreatedByUserId").AsString(450).Nullable()
					.WithColumn("CreatedOn").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
					.WithColumn("DeletedOn").AsDateTime2().Nullable();

				Create.Index("IX_IncidentMapAnnotations_Department_Call")
					.OnTable("IncidentMapAnnotations")
					.OnColumn("DepartmentId").Ascending()
					.OnColumn("CallId").Ascending();
			}

			if (!Schema.Table("CommandLogEntries").Exists())
			{
				Create.Table("CommandLogEntries")
					.WithColumn("CommandLogEntryId").AsString(128).NotNullable().PrimaryKey()
					.WithColumn("IncidentCommandId").AsString(128).NotNullable()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("CallId").AsInt32().NotNullable()
					.WithColumn("EntryType").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Description").AsString(int.MaxValue).Nullable()
					.WithColumn("UserId").AsString(450).Nullable()
					.WithColumn("Latitude").AsString(int.MaxValue).Nullable()
					.WithColumn("Longitude").AsString(int.MaxValue).Nullable()
					.WithColumn("OccurredOn").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);

				Create.Index("IX_CommandLogEntries_Department_Call")
					.OnTable("CommandLogEntries")
					.OnColumn("DepartmentId").Ascending()
					.OnColumn("CallId").Ascending();
			}

			if (!Schema.Table("CommandTransfers").Exists())
			{
				Create.Table("CommandTransfers")
					.WithColumn("CommandTransferId").AsString(128).NotNullable().PrimaryKey()
					.WithColumn("IncidentCommandId").AsString(128).NotNullable()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("CallId").AsInt32().NotNullable()
					.WithColumn("FromUserId").AsString(450).Nullable()
					.WithColumn("ToUserId").AsString(450).Nullable()
					.WithColumn("TransferredOn").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
					.WithColumn("Notes").AsString(int.MaxValue).Nullable();

				Create.Index("IX_CommandTransfers_Department_Call")
					.OnTable("CommandTransfers")
					.OnColumn("DepartmentId").Ascending()
					.OnColumn("CallId").Ascending();
			}
		}

		public override void Down()
		{
			Delete.Table("CommandTransfers");
			Delete.Table("CommandLogEntries");
			Delete.Table("IncidentMapAnnotations");
			Delete.Table("IncidentTimers");
			Delete.Table("TacticalObjectives");
			Delete.Table("ResourceAssignments");
			Delete.Table("CommandStructureNodes");
			Delete.Table("IncidentCommands");
		}
	}
}
