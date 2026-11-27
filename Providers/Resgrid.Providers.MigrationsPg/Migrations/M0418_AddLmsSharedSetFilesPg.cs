using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0418_AddLmsSharedSetFiles: lmsmediaassets.sharedfromdepartmentid and lmscontentpackages.sharedfromdepartmentid
	/// (integer NULL). Placeholder number. Guarded for safe retry.
	/// </summary>
	[Migration(418)]
	public class M0418_AddLmsSharedSetFilesPg : Migration
	{
		private static readonly string[] Tables = { "lmsmediaassets", "lmscontentpackages" };

		public override void Up()
		{
			foreach (var table in Tables)
				Execute.Sql($"ALTER TABLE IF EXISTS public.{table} ADD COLUMN IF NOT EXISTS sharedfromdepartmentid integer NULL;");
		}

		public override void Down()
		{
			foreach (var table in Tables)
				Execute.Sql($"ALTER TABLE IF EXISTS public.{table} DROP COLUMN IF EXISTS sharedfromdepartmentid;");
		}
	}
}
