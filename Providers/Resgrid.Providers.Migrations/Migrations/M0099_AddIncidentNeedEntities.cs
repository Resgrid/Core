using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Entity-category needs: the specific units/users/roles/groups command requested for the incident,
	/// dispatched individually onto the call as "requested by command".
	/// </summary>
	[Migration(99)]
	public class M0099_AddIncidentNeedEntities : Migration
	{
		public override void Up()
		{
			if (Schema.Table("IncidentNeedEntities").Exists())
				return;

			Create.Table("IncidentNeedEntities")
				.WithColumn("IncidentNeedEntityId").AsString(128).NotNullable().PrimaryKey()
				.WithColumn("IncidentNeedId").AsString(128).NotNullable()
				.WithColumn("IncidentCommandId").AsString(128).NotNullable()
				.WithColumn("DepartmentId").AsInt32().NotNullable()
				.WithColumn("CallId").AsInt32().NotNullable()
				.WithColumn("EntityKind").AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("EntityId").AsString(450).NotNullable()
				.WithColumn("EntityName").AsString(500).Nullable()
				.WithColumn("DispatchedOn").AsDateTime2().Nullable()
				.WithColumn("CreatedByUserId").AsString(450).Nullable()
				.WithColumn("CreatedOn").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);

			Create.Index("IX_IncidentNeedEntities_Need")
				.OnTable("IncidentNeedEntities")
				.OnColumn("IncidentNeedId").Ascending();

			Create.Index("IX_IncidentNeedEntities_Department_Call")
				.OnTable("IncidentNeedEntities")
				.OnColumn("DepartmentId").Ascending()
				.OnColumn("CallId").Ascending();
		}

		public override void Down()
		{
			if (Schema.Table("IncidentNeedEntities").Exists())
				Delete.Table("IncidentNeedEntities");
		}
	}
}
