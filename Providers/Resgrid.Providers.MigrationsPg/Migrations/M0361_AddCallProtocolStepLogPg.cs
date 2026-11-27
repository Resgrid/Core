using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>PostgreSQL twin of M0361_AddCallProtocolStepLog: callprotocols.steplogjson and steplogrevision. Guarded for safe retry.</summary>
	[Migration(361)]
	public class M0361_AddCallProtocolStepLogPg : Migration
	{
		public override void Up()
		{
			Execute.Sql("ALTER TABLE public.callprotocols ADD COLUMN IF NOT EXISTS steplogjson text NULL;");
			Execute.Sql("ALTER TABLE public.callprotocols ADD COLUMN IF NOT EXISTS steplogrevision integer NOT NULL DEFAULT 0;");
		}

		public override void Down()
		{
			Execute.Sql("ALTER TABLE public.callprotocols DROP COLUMN IF EXISTS steplogrevision;");
			Execute.Sql("ALTER TABLE public.callprotocols DROP COLUMN IF EXISTS steplogjson;");
		}
	}
}
