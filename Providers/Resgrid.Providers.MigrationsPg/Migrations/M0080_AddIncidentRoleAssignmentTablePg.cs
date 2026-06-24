using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(80)]
	public class M0080_AddIncidentRoleAssignmentTablePg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("IncidentRoleAssignments".ToLower()).Exists())
			{
				Create.Table("IncidentRoleAssignments".ToLower())
					.WithColumn("IncidentRoleAssignmentId".ToLower()).AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("IncidentCommandId".ToLower()).AsCustom("citext").NotNullable()
					.WithColumn("DepartmentId".ToLower()).AsInt32().NotNullable()
					.WithColumn("CallId".ToLower()).AsInt32().NotNullable()
					.WithColumn("UserId".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("RoleType".ToLower()).AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ScopeNodeId".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("AssignedByUserId".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("AssignedOn".ToLower()).AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
					.WithColumn("RemovedOn".ToLower()).AsDateTime2().Nullable();

				Create.Index("IX_IncidentRoleAssignments_Department_Call".ToLower())
					.OnTable("IncidentRoleAssignments".ToLower())
					.OnColumn("DepartmentId".ToLower()).Ascending()
					.OnColumn("CallId".ToLower()).Ascending();
			}
		}

		public override void Down()
		{
			Delete.Table("IncidentRoleAssignments".ToLower());
		}
	}
}
