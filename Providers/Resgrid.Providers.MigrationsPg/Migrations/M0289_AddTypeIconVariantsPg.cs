using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Status and priority map icons: typeicons.variant (p{priority} on a call type, s{built-in status} or o{custom option
	/// id} on a unit type or role; NULL for the type's own icon). PostgreSQL twin of the SQL Server migration; varchar like
	/// M0285's iconkey so the string parameter compares exactly.
	/// </summary>
	[Migration(289)]
	public class M0289_AddTypeIconVariantsPg : Migration
	{
		public override void Up()
		{
			Execute.Sql("ALTER TABLE typeicons ADD COLUMN IF NOT EXISTS variant varchar(32) NULL;");
		}

		public override void Down()
		{
			Execute.Sql("ALTER TABLE typeicons DROP COLUMN IF EXISTS variant;");
		}
	}
}
