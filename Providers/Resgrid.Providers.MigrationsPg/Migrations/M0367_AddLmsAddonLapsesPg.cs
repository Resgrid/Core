using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0367_AddLmsAddonLapses: lmsaddonstates (one row per department) and lmsaddonlapses (at most one open per
	/// department, a partial unique index).
	/// </summary>
	[Migration(367)]
	public class M0367_AddLmsAddonLapsesPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("lmsaddonstates").Exists())
			{
				Create.Table("lmsaddonstates")
					.WithColumn("lmsaddonstateid").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("packs").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("paidthroughutc").AsDateTime2().Nullable()
					.WithColumn("renewalcancelled").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("lastactiveon").AsDateTime2().Nullable()
					.WithColumn("lapsedsinceutc").AsDateTime2().Nullable()
					.WithColumn("updatedon").AsDateTime2().NotNullable();
			}

			Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_lmsaddonstates_department ON lmsaddonstates (departmentid);");

			if (!Schema.Table("lmsaddonlapses").Exists())
			{
				Create.Table("lmsaddonlapses")
					.WithColumn("lmsaddonlapseid").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("startedon").AsDateTime2().NotNullable()
					.WithColumn("endedon").AsDateTime2().Nullable()
					.WithColumn("paidthroughutc").AsDateTime2().Nullable()
					.WithColumn("reason").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("packs").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("detectedon").AsDateTime2().NotNullable();
			}

			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_lmsaddonlapses_department_started ON lmsaddonlapses (departmentid, startedon);");
			Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_lmsaddonlapses_open ON lmsaddonlapses (departmentid) WHERE endedon IS NULL;");
		}

		public override void Down()
		{
			Execute.Sql("DROP TABLE IF EXISTS lmsaddonlapses;");
			Execute.Sql("DROP TABLE IF EXISTS lmsaddonstates;");
		}
	}
}
