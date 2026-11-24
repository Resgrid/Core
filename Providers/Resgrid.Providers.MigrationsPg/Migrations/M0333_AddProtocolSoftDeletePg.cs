using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0333: dispatch protocols become soft deleted, and the callprotocols foreign key to
	/// dispatchprotocols loses ON DELETE CASCADE so deleting a protocol can no longer remove it from the calls that used it.
	/// The existing key is found by its tables (confdeltype 'c' = cascade), not its name. Guarded for safe retry.
	/// </summary>
	[Migration(333)]
	public class M0333_AddProtocolSoftDeletePg : Migration
	{
		public override void Up()
		{
			Execute.Sql("ALTER TABLE dispatchprotocols ADD COLUMN IF NOT EXISTS isdeleted boolean NOT NULL DEFAULT false;");
			Execute.Sql("ALTER TABLE dispatchprotocols ADD COLUMN IF NOT EXISTS deletedon timestamp without time zone NULL;");

			Execute.Sql(@"
DO $$
DECLARE fk text;
BEGIN
	SELECT c.conname INTO fk
		FROM pg_constraint c
		WHERE c.contype = 'f'
			AND c.conrelid = 'public.callprotocols'::regclass
			AND c.confrelid = 'public.dispatchprotocols'::regclass
			AND c.confdeltype <> 'a'
		LIMIT 1;

	IF fk IS NOT NULL THEN
		EXECUTE format('ALTER TABLE public.callprotocols DROP CONSTRAINT %I', fk);

		ALTER TABLE public.callprotocols ADD CONSTRAINT fk_callprotocols_dispatchprotocols_dispatchprotocolid
			FOREIGN KEY (dispatchprotocolid) REFERENCES public.dispatchprotocols (dispatchprotocolid) ON DELETE NO ACTION;
	END IF;
END $$;");
		}

		public override void Down()
		{
			Execute.Sql(@"
DO $$
BEGIN
	IF EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_callprotocols_dispatchprotocols_dispatchprotocolid') THEN
		ALTER TABLE public.callprotocols DROP CONSTRAINT fk_callprotocols_dispatchprotocols_dispatchprotocolid;

		ALTER TABLE public.callprotocols ADD CONSTRAINT ""fk_dbo.callprotocols_dbo.dispatchprotocols_dispatchprotocolid""
			FOREIGN KEY (dispatchprotocolid) REFERENCES public.dispatchprotocols (dispatchprotocolid) ON DELETE CASCADE;
	END IF;
END $$;");

			Execute.Sql("ALTER TABLE dispatchprotocols DROP COLUMN IF EXISTS deletedon;");
			Execute.Sql("ALTER TABLE dispatchprotocols DROP COLUMN IF EXISTS isdeleted;");
		}
	}
}
