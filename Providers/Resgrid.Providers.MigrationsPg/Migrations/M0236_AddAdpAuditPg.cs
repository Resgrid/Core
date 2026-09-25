using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(236)]
	public class M0236_AddAdpAuditPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("adpauditevents").Exists())
				Create.Table("adpauditevents")
					.WithColumn("eventid").AsString(32).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("sequence").AsInt64().NotNullable()
					.WithColumn("layer").AsString(32).NotNullable()
					.WithColumn("operation").AsString(64).NotNullable()
					.WithColumn("outcome").AsString(64).NotNullable()
					.WithColumn("actorid").AsString(128).Nullable()
					.WithColumn("correlationid").AsString(128).Nullable()
					.WithColumn("resourceid").AsString(128).Nullable()
					.WithColumn("policyepoch").AsInt64().NotNullable()
					.WithColumn("occurredutc").AsCustom("timestamp").NotNullable()
					.WithColumn("previoushash").AsString(64).NotNullable()
					.WithColumn("hash").AsString(64).NotNullable();
			Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_adpaudit_sequence ON adpauditevents(departmentid, sequence);");
			if (!Schema.Table("adpaccessstates").Exists())
				Create.Table("adpaccessstates")
					.WithColumn("stateid").AsString(200).NotNullable().PrimaryKey()
					.WithColumn("json").AsString(int.MaxValue).NotNullable()
					.WithColumn("version").AsInt64().NotNullable();
		}

		// Audit evidence must survive application rollback.
		public override void Down() { }
	}
}
