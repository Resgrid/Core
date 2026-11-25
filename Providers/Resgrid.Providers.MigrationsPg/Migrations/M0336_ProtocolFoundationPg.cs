using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0336. Code and description are citext here (no length to widen). Adds dispatchprotocoltriggers.calltypeid
	/// (filled from the stored name where the department has a type of that name; the English "No Type" label becomes a null
	/// name) and the callprotocols questionnaire, source, content hash, attach and removal columns.
	/// <para>
	/// Trigger windows were saved as the department's wall-clock time and are now stored in UTC. They are converted once, in
	/// the same block that adds calltypeid (so a retry never converts twice), for departments whose time zone name PostgreSQL
	/// knows. Departments store Windows zone names, which PostgreSQL does not, so on most installs existing windows are left as
	/// they were (still compared as UTC, as before this migration); re-saving the protocol stores them in UTC.
	/// </para>
	/// Guarded for safe retry.
	/// </summary>
	[Migration(336)]
	public class M0336_ProtocolFoundationPg : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
DO $$
BEGIN
	IF NOT EXISTS (SELECT 1 FROM information_schema.columns
		WHERE table_schema = 'public' AND table_name = 'dispatchprotocoltriggers' AND column_name = 'calltypeid') THEN

		UPDATE public.dispatchprotocoltriggers t
			SET startson = CASE WHEN t.startson IS NULL THEN NULL ELSE (t.startson AT TIME ZONE d.timezone::text) AT TIME ZONE 'UTC' END,
				endson = CASE WHEN t.endson IS NULL THEN NULL ELSE (t.endson AT TIME ZONE d.timezone::text) AT TIME ZONE 'UTC' END
			FROM public.dispatchprotocols p, public.departments d
			WHERE p.dispatchprotocolid = t.dispatchprotocolid
				AND d.departmentid = p.departmentid
				AND (t.startson IS NOT NULL OR t.endson IS NOT NULL)
				AND d.timezone::text IN (SELECT name FROM pg_timezone_names);

		ALTER TABLE public.dispatchprotocoltriggers ADD COLUMN calltypeid integer NULL;
	END IF;
END $$;");

			Execute.Sql("UPDATE public.dispatchprotocoltriggers SET calltype = NULL WHERE calltypeid IS NULL AND lower(trim(calltype::text)) = 'no type';");

			Execute.Sql(@"
UPDATE public.dispatchprotocoltriggers t
	SET calltypeid = (SELECT c.calltypeid FROM public.calltypes c
		WHERE c.departmentid = p.departmentid AND lower(trim(c.type::text)) = lower(trim(t.calltype::text))
		ORDER BY c.calltypeid LIMIT 1)
	FROM public.dispatchprotocols p
	WHERE p.dispatchprotocolid = t.dispatchprotocolid
		AND t.calltypeid IS NULL
		AND t.calltype IS NOT NULL
		AND EXISTS (SELECT 1 FROM public.calltypes c
			WHERE c.departmentid = p.departmentid AND lower(trim(c.type::text)) = lower(trim(t.calltype::text)));");

			Execute.Sql("ALTER TABLE callprotocols ADD COLUMN IF NOT EXISTS answersjson text NULL;");
			Execute.Sql("ALTER TABLE callprotocols ADD COLUMN IF NOT EXISTS source integer NULL;");
			Execute.Sql("ALTER TABLE callprotocols ADD COLUMN IF NOT EXISTS contenthash varchar(64) NULL;");
			Execute.Sql("ALTER TABLE callprotocols ADD COLUMN IF NOT EXISTS attachedon timestamp without time zone NULL;");
			Execute.Sql("ALTER TABLE callprotocols ADD COLUMN IF NOT EXISTS attachedbyuserid citext NULL;");
			Execute.Sql("ALTER TABLE callprotocols ADD COLUMN IF NOT EXISTS removedon timestamp without time zone NULL;");
			Execute.Sql("ALTER TABLE callprotocols ADD COLUMN IF NOT EXISTS removedbyuserid citext NULL;");
			Execute.Sql("ALTER TABLE callprotocols ADD COLUMN IF NOT EXISTS removedreason text NULL;");
		}

		public override void Down()
		{
			foreach (var column in new[] { "removedreason", "removedbyuserid", "removedon", "attachedbyuserid", "attachedon", "contenthash", "source", "answersjson" })
				Execute.Sql($"ALTER TABLE callprotocols DROP COLUMN IF EXISTS {column};");

			// The windows stay in UTC (see M0336 on SQL Server).
			Execute.Sql("ALTER TABLE dispatchprotocoltriggers DROP COLUMN IF EXISTS calltypeid;");
		}
	}
}
