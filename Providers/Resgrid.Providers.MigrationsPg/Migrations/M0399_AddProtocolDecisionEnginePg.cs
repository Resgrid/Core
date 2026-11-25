using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0399_AddProtocolDecisionEngine: callprotocols.determinant / overridereason (citext, compared like SQL Server's
	/// case-insensitive collation) and casejson (text), the (dispatchprotocolid, determinant) partial index, and the shared trigger
	/// engine's criteria on dispatchprotocoltriggers and runcardtriggers (keywords citext; callsources an exact list of numbers).
	/// Guarded for safe retry.
	/// </summary>
	[Migration(399)]
	public class M0399_AddProtocolDecisionEnginePg : Migration
	{
		private static readonly (string Name, string Type)[] TriggerColumns =
		{
			("geofencegroupid", "integer NULL"),
			("daysofweek", "integer NULL"),
			("startminute", "integer NULL"),
			("endminute", "integer NULL"),
			("keywords", "citext NULL"),
			("poitypeid", "integer NULL"),
			("callsources", "varchar(50) NULL")
		};

		public override void Up()
		{
			Execute.Sql("ALTER TABLE public.callprotocols ADD COLUMN IF NOT EXISTS determinant citext NULL;");
			Execute.Sql("ALTER TABLE public.callprotocols ADD COLUMN IF NOT EXISTS overridereason citext NULL;");
			Execute.Sql("ALTER TABLE public.callprotocols ADD COLUMN IF NOT EXISTS casejson text NULL;");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_callprotocols_protocoldeterminant ON public.callprotocols (dispatchprotocolid, determinant) WHERE determinant IS NOT NULL;");

			foreach (var table in new[] { "dispatchprotocoltriggers", "runcardtriggers" })
			{
				foreach (var (name, type) in TriggerColumns)
					Execute.Sql($"ALTER TABLE IF EXISTS public.{table} ADD COLUMN IF NOT EXISTS {name} {type};");
			}
		}

		public override void Down()
		{
			Execute.Sql("DROP INDEX IF EXISTS public.ix_callprotocols_protocoldeterminant;");

			foreach (var column in new[] { "casejson", "overridereason", "determinant" })
				Execute.Sql($"ALTER TABLE public.callprotocols DROP COLUMN IF EXISTS {column};");

			foreach (var table in new[] { "dispatchprotocoltriggers", "runcardtriggers" })
			{
				foreach (var (name, _) in TriggerColumns)
					Execute.Sql($"ALTER TABLE IF EXISTS public.{table} DROP COLUMN IF EXISTS {name};");
			}
		}
	}
}
