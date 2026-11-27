using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>PostgreSQL twin of M0417_AddMutualAidCountersignature (partner countersignature over a Department Link). Placeholder number. Guarded.</summary>
	[Migration(417)]
	public class M0417_AddMutualAidCountersignaturePg : Migration
	{
		private static readonly (string Name, string Type)[] Columns =
		{
			("requirescountersignature", "boolean NOT NULL DEFAULT false"),
			("countersignstatus", "integer NOT NULL DEFAULT 0"),
			("countersignrequestid", "integer NULL"),
			("countersignrequestedon", "timestamp NULL"),
			("countersignedon", "timestamp NULL"),
			("countersignedbyuserid", "citext NULL"),
			("countersignnote", "citext NULL")
		};

		public override void Up()
		{
			foreach (var (name, type) in Columns)
				Execute.Sql($"ALTER TABLE IF EXISTS public.mutualaidagreements ADD COLUMN IF NOT EXISTS {name} {type};");
			Execute.Sql(@"DO $$ BEGIN
IF to_regclass('public.mutualaidagreements') IS NOT NULL THEN
	CREATE INDEX IF NOT EXISTS ix_mutualaidagreements_countersign ON mutualaidagreements (partnerdepartmentid, countersignstatus);
END IF;
END $$;");
		}

		public override void Down()
		{
			Execute.Sql("DROP INDEX IF EXISTS ix_mutualaidagreements_countersign;");
			foreach (var (name, _) in Columns)
				Execute.Sql($"ALTER TABLE IF EXISTS public.mutualaidagreements DROP COLUMN IF EXISTS {name};");
		}
	}
}
