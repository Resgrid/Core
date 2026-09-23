using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Crew and individual time reports (see the SQL Server M0227). PostgreSQL treats NULLs as distinct in a unique index, so
	/// the scope columns are coalesced to keep the deployment-wide report one per day.
	/// </summary>
	[Migration(227)]
	public class M0227_AddTimeReportScopesPg : Migration
	{
		public override void Up()
		{
			Execute.Sql("ALTER TABLE deploymenttimereports ADD COLUMN IF NOT EXISTS deploymentunitid varchar(36) NULL;");
			Execute.Sql("ALTER TABLE deploymenttimereports ADD COLUMN IF NOT EXISTS deploymentpersonnelid varchar(36) NULL;");
			Execute.Sql("DROP INDEX IF EXISTS ux_deploymenttimereports_date;");
			Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_deploymenttimereports_scope ON deploymenttimereports (deploymentid, reportdate, COALESCE(deploymentunitid, ''), COALESCE(deploymentpersonnelid, '')) WHERE isdeleted = FALSE AND status <> 4;");
		}

		public override void Down()
		{
			Execute.Sql("DO $$ BEGIN IF EXISTS (SELECT 1 FROM deploymenttimereports WHERE deploymentunitid IS NOT NULL OR deploymentpersonnelid IS NOT NULL) THEN RAISE EXCEPTION 'Crew and individual time reports exist; rolling back would merge them into one report per day.'; END IF; END $$;");
			Execute.Sql("DROP INDEX IF EXISTS ux_deploymenttimereports_scope;");
			Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_deploymenttimereports_date ON deploymenttimereports (deploymentid, reportdate) WHERE isdeleted = FALSE AND status <> 4;");
			Delete.Column("deploymentpersonnelid").FromTable("deploymenttimereports");
			Delete.Column("deploymentunitid").FromTable("deploymenttimereports");
		}
	}
}
