using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0423_AddWorkforceProAddonStates: workforceproaddonstates, what the Workforce Pro gate last saw of a department's
	/// add-on (for the seven-day dunning grace), one row per department.
	/// </summary>
	[Migration(423)]
	public class M0423_AddWorkforceProAddonStatesPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("workforceproaddonstates").Exists())
			{
				Create.Table("workforceproaddonstates")
					.WithColumn("workforceproaddonstateid").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("packs").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("paidthroughutc").AsDateTime2().Nullable()
					.WithColumn("renewalcancelled").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("lastactiveon").AsDateTime2().Nullable()
					.WithColumn("lapsedsinceutc").AsDateTime2().Nullable()
					.WithColumn("updatedon").AsDateTime2().NotNullable();
			}

			Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_workforceproaddonstates_department ON workforceproaddonstates (departmentid);");
		}

		public override void Down()
		{
			Execute.Sql("DROP TABLE IF EXISTS workforceproaddonstates;");
		}
	}
}
