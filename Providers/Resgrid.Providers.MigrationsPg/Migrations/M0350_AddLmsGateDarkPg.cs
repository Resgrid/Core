using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0350_AddLmsGateDark: the Training.Lms flag (seeded off) and lmscertificatetemplates. No planaddons
	/// row: nothing is offered for sale before wave 3.
	/// </summary>
	[Migration(350)]
	public class M0350_AddLmsGateDarkPg : Migration
	{
		public const string FlagKey = "Training.Lms";

		public override void Up()
		{
			Execute.Sql(
				"INSERT INTO featureflags (flagkey, name, description, category, isenabledglobally) " +
				"SELECT '" + FlagKey + "', 'LMS add-on', " +
				"'LMS add-on operator rollout and kill switch. Off: the formal ISO FSRS and NREMT NCCP reports, certificate verification codes, the public verify page and custom certificate templates are hidden and their routes return 404. The purchase is PlanAddonTypes.Lms. Seeded off; switched on with wave 3.', " +
				"'Training', false " +
				"WHERE NOT EXISTS (SELECT 1 FROM featureflags WHERE flagkey = '" + FlagKey + "');");

			if (!Schema.Table("lmscertificatetemplates").Exists())
			{
				Create.Table("lmscertificatetemplates")
					.WithColumn("lmscertificatetemplateid").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("name").AsString(150).NotNullable()
					.WithColumn("body").AsCustom("text").Nullable()
					.WithColumn("isdefault").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("createdbyuserid").AsCustom("citext").Nullable()
					.WithColumn("updatedon").AsDateTime2().Nullable();
			}

			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_lmscertificatetemplates_department ON lmscertificatetemplates (departmentid);");
		}

		public override void Down()
		{
			Execute.Sql("DROP TABLE IF EXISTS lmscertificatetemplates;");
			Execute.Sql("DELETE FROM featureflags WHERE flagkey = '" + FlagKey + "';");
		}
	}
}
