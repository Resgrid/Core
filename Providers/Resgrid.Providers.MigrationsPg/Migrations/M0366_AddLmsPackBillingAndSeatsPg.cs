using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0366_AddLmsPackBillingAndSeats: the LMS planaddons row (type 9, cost 79, empty price ids so checkout fails
	/// closed until set) and lmsmemberseats (citext member ids, unique per department and member). The Training.Lms flag stays off.
	/// </summary>
	[Migration(366)]
	public class M0366_AddLmsPackBillingAndSeatsPg : Migration
	{
		public const string LmsAddonId = "7d4e2b91-3c58-4f0a-9e16-5b8a2c4d7f93";

		public override void Up()
		{
			Execute.Sql(
				"INSERT INTO planaddons (planaddonid, addontype, cost, externalid, testexternalid) " +
				"SELECT '" + LmsAddonId + "', 9, 79, '', '' " +
				"WHERE NOT EXISTS (SELECT 1 FROM planaddons WHERE planaddonid = '" + LmsAddonId + "' OR addontype = 9);");

			if (!Schema.Table("lmsmemberseats").Exists())
			{
				Create.Table("lmsmemberseats")
					.WithColumn("lmsmemberseatid").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("userid").AsCustom("citext").NotNullable()
					.WithColumn("enabledon").AsDateTime2().NotNullable()
					.WithColumn("enabledbyuserid").AsCustom("citext").Nullable();
			}

			Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_lmsmemberseats_department_user ON lmsmemberseats (departmentid, userid);");
		}

		public override void Down()
		{
			Execute.Sql("DROP TABLE IF EXISTS lmsmemberseats;");
			Execute.Sql(
				"DELETE FROM planaddons WHERE planaddonid = '" + LmsAddonId + "' " +
				"AND NOT EXISTS (SELECT 1 FROM paymentaddons WHERE planaddonid = '" + LmsAddonId + "');");
		}
	}
}
