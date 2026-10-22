using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Personnel role map icons: iconkey (mirrors the role's typeicons row, typekind 4) and sortorder (the department's
	/// role order; a person's map pin uses the icon of the first role they hold, in this order, that has one) on
	/// personnelroles. PostgreSQL twin of the SQL Server migration; iconkey is varchar like M0285's.
	/// </summary>
	[Migration(286)]
	public class M0286_AddPersonnelRoleIconsPg : Migration
	{
		public override void Up()
		{
			Execute.Sql("ALTER TABLE personnelroles ADD COLUMN IF NOT EXISTS iconkey varchar(64) NULL;");
			Execute.Sql("ALTER TABLE personnelroles ADD COLUMN IF NOT EXISTS sortorder integer NOT NULL DEFAULT 0;");
		}

		public override void Down()
		{
			Execute.Sql("ALTER TABLE personnelroles DROP COLUMN IF EXISTS sortorder;");
			Execute.Sql("ALTER TABLE personnelroles DROP COLUMN IF EXISTS iconkey;");
		}
	}
}
