using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Append-only audit trail for incident-need fulfillment changes (partial fills, fill reductions,
	/// closing an unfilled need) with the caller's note, author, and timestamp.
	/// </summary>
	[Migration(95)]
	public class M0095_AddIncidentNeedUpdatesPg : Migration
	{
		public override void Up()
		{
			if (Schema.Table("incidentneedupdates").Exists())
				return;

			Create.Table("incidentneedupdates")
				.WithColumn("incidentneedupdateid").AsCustom("citext").NotNullable().PrimaryKey()
				.WithColumn("incidentneedid").AsCustom("citext").NotNullable()
				.WithColumn("incidentcommandid").AsCustom("citext").NotNullable()
				.WithColumn("departmentid").AsInt32().NotNullable()
				.WithColumn("callid").AsInt32().NotNullable()
				.WithColumn("previousstatus").AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("newstatus").AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("previousquantityfulfilled").AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("newquantityfulfilled").AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("note").AsCustom("citext").Nullable()
				.WithColumn("createdbyuserid").AsCustom("citext").Nullable()
				.WithColumn("createdon").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);

			Create.Index("ix_incidentneedupdates_need")
				.OnTable("incidentneedupdates")
				.OnColumn("incidentneedid").Ascending();

			Create.Index("ix_incidentneedupdates_department_call")
				.OnTable("incidentneedupdates")
				.OnColumn("departmentid").Ascending()
				.OnColumn("callid").Ascending();
		}

		public override void Down()
		{
			if (Schema.Table("incidentneedupdates").Exists())
				Delete.Table("incidentneedupdates");
		}
	}
}
