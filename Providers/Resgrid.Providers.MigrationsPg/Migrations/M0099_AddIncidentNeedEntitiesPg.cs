using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Entity-category needs: the specific units/users/roles/groups command requested for the incident,
	/// dispatched individually onto the call as "requested by command".
	/// </summary>
	[Migration(99)]
	public class M0099_AddIncidentNeedEntitiesPg : Migration
	{
		public override void Up()
		{
			if (Schema.Table("incidentneedentities").Exists())
				return;

			Create.Table("incidentneedentities")
				.WithColumn("incidentneedentityid").AsCustom("citext").NotNullable().PrimaryKey()
				.WithColumn("incidentneedid").AsCustom("citext").NotNullable()
				.WithColumn("incidentcommandid").AsCustom("citext").NotNullable()
				.WithColumn("departmentid").AsInt32().NotNullable()
				.WithColumn("callid").AsInt32().NotNullable()
				.WithColumn("entitykind").AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("entityid").AsCustom("citext").NotNullable()
				.WithColumn("entityname").AsCustom("citext").Nullable()
				.WithColumn("dispatchedon").AsDateTime2().Nullable()
				.WithColumn("createdbyuserid").AsCustom("citext").Nullable()
				.WithColumn("createdon").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);

			Create.Index("ix_incidentneedentities_need")
				.OnTable("incidentneedentities")
				.OnColumn("incidentneedid").Ascending();

			Create.Index("ix_incidentneedentities_department_call")
				.OnTable("incidentneedentities")
				.OnColumn("departmentid").Ascending()
				.OnColumn("callid").Ascending();
		}

		public override void Down()
		{
			if (Schema.Table("incidentneedentities").Exists())
				Delete.Table("incidentneedentities");
		}
	}
}
