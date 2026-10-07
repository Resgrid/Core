using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Units become soft deleted. A hard delete failed on every unit that had ever been dispatched, logged, certified or
	/// tracked (those rows reference Units), and removing the row would orphan the call, state and report history that
	/// names it. IsDeleted keeps the unit out of current lists while the row stays for point-in-time data. Unit names are
	/// unique per department only among non-deleted units, which is enforced in the application (there is no unique
	/// index on Units.Name to relax). DeletedByUserId has no foreign key: it is provenance and must survive the member leaving.
	/// </summary>
	[Migration(262)]
	public class M0262_AddUnitSoftDelete : Migration
	{
		public override void Up()
		{
			Execute.Sql("IF COL_LENGTH('Units', 'IsDeleted') IS NULL ALTER TABLE [Units] ADD [IsDeleted] bit NOT NULL CONSTRAINT [DF_Units_IsDeleted] DEFAULT 0;");
			Execute.Sql("IF COL_LENGTH('Units', 'DeletedOn') IS NULL ALTER TABLE [Units] ADD [DeletedOn] datetime2 NULL;");
			Execute.Sql("IF COL_LENGTH('Units', 'DeletedByUserId') IS NULL ALTER TABLE [Units] ADD [DeletedByUserId] nvarchar(128) NULL;");
		}

		public override void Down()
		{
			Execute.Sql("IF COL_LENGTH('Units', 'DeletedByUserId') IS NOT NULL ALTER TABLE [Units] DROP COLUMN [DeletedByUserId];");
			Execute.Sql("IF COL_LENGTH('Units', 'DeletedOn') IS NOT NULL ALTER TABLE [Units] DROP COLUMN [DeletedOn];");
			Execute.Sql("IF OBJECT_ID('DF_Units_IsDeleted', 'D') IS NOT NULL ALTER TABLE [Units] DROP CONSTRAINT [DF_Units_IsDeleted];");
			Execute.Sql("IF COL_LENGTH('Units', 'IsDeleted') IS NOT NULL ALTER TABLE [Units] DROP COLUMN [IsDeleted];");
		}
	}
}
