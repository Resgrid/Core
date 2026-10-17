using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0273: decision-model provenance columns on aidispatchaudits (metadata only). Same number,
	/// lower-case identifiers, guarded for safe retry.
	/// </summary>
	[Migration(273)]
	public class M0273_AddAiDispatchDecisionAuditPg : Migration
	{
		public override void Up()
		{
			Execute.Sql("ALTER TABLE aidispatchaudits ADD COLUMN IF NOT EXISTS decisionpromptversion varchar(64) NULL;");
			Execute.Sql("ALTER TABLE aidispatchaudits ADD COLUMN IF NOT EXISTS decisionmodelname varchar(128) NULL;");
			Execute.Sql("ALTER TABLE aidispatchaudits ADD COLUMN IF NOT EXISTS decisionmodelrevision varchar(64) NULL;");
			Execute.Sql("ALTER TABLE aidispatchaudits ADD COLUMN IF NOT EXISTS decisionruntimedigest varchar(80) NULL;");
			Execute.Sql("ALTER TABLE aidispatchaudits ADD COLUMN IF NOT EXISTS decisioninputtokens integer NOT NULL DEFAULT 0;");
			Execute.Sql("ALTER TABLE aidispatchaudits ADD COLUMN IF NOT EXISTS decisionlatencyms integer NOT NULL DEFAULT 0;");
		}

		public override void Down()
		{
			Execute.Sql("ALTER TABLE aidispatchaudits DROP COLUMN IF EXISTS decisionlatencyms;");
			Execute.Sql("ALTER TABLE aidispatchaudits DROP COLUMN IF EXISTS decisioninputtokens;");
			Execute.Sql("ALTER TABLE aidispatchaudits DROP COLUMN IF EXISTS decisionruntimedigest;");
			Execute.Sql("ALTER TABLE aidispatchaudits DROP COLUMN IF EXISTS decisionmodelrevision;");
			Execute.Sql("ALTER TABLE aidispatchaudits DROP COLUMN IF EXISTS decisionmodelname;");
			Execute.Sql("ALTER TABLE aidispatchaudits DROP COLUMN IF EXISTS decisionpromptversion;");
		}
	}
}
