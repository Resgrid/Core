using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(80)]
	public class M0080_AddIncidentRoleAssignmentTable : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("IncidentRoleAssignments").Exists())
			{
				Create.Table("IncidentRoleAssignments")
					.WithColumn("IncidentRoleAssignmentId").AsString(128).NotNullable().PrimaryKey()
					.WithColumn("IncidentCommandId").AsString(128).NotNullable()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("CallId").AsInt32().NotNullable()
					.WithColumn("UserId").AsString(450).Nullable()
					.WithColumn("RoleType").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ScopeNodeId").AsString(128).Nullable()
					.WithColumn("AssignedByUserId").AsString(450).Nullable()
					.WithColumn("AssignedOn").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
					.WithColumn("RemovedOn").AsDateTime2().Nullable();

				Create.Index("IX_IncidentRoleAssignments_Department_Call")
					.OnTable("IncidentRoleAssignments")
					.OnColumn("DepartmentId").Ascending()
					.OnColumn("CallId").Ascending();
			}
		}

		public override void Down()
		{
			Delete.Table("IncidentRoleAssignments");
		}
	}
}
