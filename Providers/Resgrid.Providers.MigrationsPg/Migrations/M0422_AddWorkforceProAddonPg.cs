using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0422_AddWorkforceProAddon: the Workforce Pro planaddons row (type 8, cost 119, empty price ids so checkout fails
	/// closed until the owner sets them), the Workforce.Pro flag seeded off, and workforceproseats (citext member ids, unique per department
	/// and member).
	/// </summary>
	[Migration(422)]
	public class M0422_AddWorkforceProAddonPg : Migration
	{
		public const string WorkforceProAddonId = "65516545-bf65-47ce-9e3a-abaf18409d88";
		public const string FlagKey = "Workforce.Pro";

		public override void Up()
		{
			Execute.Sql(
				"INSERT INTO planaddons (planaddonid, addontype, cost, externalid, testexternalid) " +
				"SELECT '" + WorkforceProAddonId + "', 8, 119, '', '' " +
				"WHERE NOT EXISTS (SELECT 1 FROM planaddons WHERE planaddonid = '" + WorkforceProAddonId + "' OR addontype = 8);");

			Execute.Sql(
				"INSERT INTO featureflags (flagkey, name, description, category, isenabledglobally) " +
				"SELECT '" + FlagKey + "', 'Workforce Pro add-on', " +
				"'Workforce Pro add-on operator rollout and kill switch. Off: member wellness and workplace exposure (exposure log, exposure cases, injuries, medical clearances, their notices and sweeps) are hidden and their routes return 404; the data is kept. The purchase is PlanAddonTypes.WorkforcePro. Seeded off.', " +
				"'Workforce', false " +
				"WHERE NOT EXISTS (SELECT 1 FROM featureflags WHERE flagkey = '" + FlagKey + "');");

			if (!Schema.Table("workforceproseats").Exists())
			{
				Create.Table("workforceproseats")
					.WithColumn("workforceproseatid").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("userid").AsCustom("citext").NotNullable()
					.WithColumn("enabledon").AsDateTime2().NotNullable()
					.WithColumn("enabledbyuserid").AsCustom("citext").Nullable();
			}

			Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_workforceproseats_department_user ON workforceproseats (departmentid, userid);");
		}

		public override void Down()
		{
			Execute.Sql("DROP TABLE IF EXISTS workforceproseats;");
			Execute.Sql("DELETE FROM featureflags WHERE flagkey = '" + FlagKey + "';");
			Execute.Sql(
				"DELETE FROM planaddons WHERE planaddonid = '" + WorkforceProAddonId + "' " +
				"AND NOT EXISTS (SELECT 1 FROM paymentaddons WHERE planaddonid = '" + WorkforceProAddonId + "');");
		}
	}
}
