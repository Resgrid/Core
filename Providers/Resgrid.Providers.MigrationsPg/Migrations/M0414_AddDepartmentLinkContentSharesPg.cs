using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0414_AddDepartmentLinkContentShares: departmentlinks.departmentshareprotocols, departmentsharetraining,
	/// linkeddepartmentshareprotocols and linkeddepartmentsharetraining (boolean, false). Placeholder number. Guarded for safe retry.
	/// </summary>
	[Migration(414)]
	public class M0414_AddDepartmentLinkContentSharesPg : Migration
	{
		private static readonly string[] Columns =
		{
			"departmentshareprotocols", "departmentsharetraining", "linkeddepartmentshareprotocols", "linkeddepartmentsharetraining"
		};

		public override void Up()
		{
			foreach (var column in Columns)
				Execute.Sql($"ALTER TABLE IF EXISTS public.departmentlinks ADD COLUMN IF NOT EXISTS {column} boolean NOT NULL DEFAULT false;");
		}

		public override void Down()
		{
			foreach (var column in Columns)
				Execute.Sql($"ALTER TABLE IF EXISTS public.departmentlinks DROP COLUMN IF EXISTS {column};");
		}
	}
}
