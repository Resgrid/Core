using FluentMigrator;

namespace Resgrid.Providers.Migrations.Maintenance
{
	/// <summary>
	/// Enforces the RMS SQL Server baseline before each database upgrade, including upgrades with
	/// no pending schema changes. ALTER DATABASE must execute outside a migration transaction.
	/// </summary>
	[Maintenance(MigrationStage.BeforeAll, TransactionBehavior.None)]
	public class EnsureSqlServerCompatibilityLevel : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
IF EXISTS (SELECT 1 FROM sys.databases WHERE database_id = DB_ID() AND compatibility_level < 150)
BEGIN
	-- Azure SQL uses a different product-version sequence from boxed SQL Server.
	IF CONVERT(int, SERVERPROPERTY('EngineEdition')) NOT IN (5, 8)
		AND CONVERT(int, SERVERPROPERTY('ProductMajorVersion')) < 15
	BEGIN
		;THROW 51000, 'Resgrid RMS requires database compatibility level 150 or higher. Upgrade to SQL Server 2019 or later before running the database upgrade.', 1;
	END;

	IF ISNULL(HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'ALTER'), 0) <> 1
	BEGIN
		;THROW 51001, 'Resgrid RMS requires database compatibility level 150 or higher. Run the database upgrade with ALTER permission on the core database, or have a database administrator raise its compatibility level first.', 1;
	END;

	DECLARE @sql nvarchar(max) = N'ALTER DATABASE ' + QUOTENAME(DB_NAME()) + N' SET COMPATIBILITY_LEVEL = 150;';
	EXEC sys.sp_executesql @sql;
END;");
		}

		public override void Down()
		{
			// Compatibility is an infrastructure prerequisite. Never lower it on schema rollback:
			// other deployed application versions may still depend on it, and the old level is unknown.
		}
	}
}
