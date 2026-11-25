using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Dispatch protocols become soft deleted. CallProtocols referenced DispatchProtocols with ON DELETE CASCADE, so deleting
	/// a protocol removed it from every call it had been used on: the call record was rewritten after the fact. IsDeleted
	/// keeps the protocol out of lists, matching and new calls while the row stays for the calls that cite it. The foreign key
	/// is recreated without the cascade so a hard delete can never erase call history again (it now fails while calls
	/// reference the protocol). The existing key is found by its tables, not its name, because the name differs between
	/// databases created by the initial script and older ones. Guarded for safe retry.
	/// </summary>
	[Migration(333)]
	public class M0333_AddProtocolSoftDelete : Migration
	{
		public override void Up()
		{
			Execute.Sql("IF COL_LENGTH('DispatchProtocols', 'IsDeleted') IS NULL ALTER TABLE [DispatchProtocols] ADD [IsDeleted] bit NOT NULL CONSTRAINT [DF_DispatchProtocols_IsDeleted] DEFAULT 0;");
			Execute.Sql("IF COL_LENGTH('DispatchProtocols', 'DeletedOn') IS NULL ALTER TABLE [DispatchProtocols] ADD [DeletedOn] datetime2 NULL;");

			Execute.Sql(@"
DECLARE @fk sysname;

SELECT TOP 1 @fk = fk.name
	FROM sys.foreign_keys fk
	WHERE fk.parent_object_id = OBJECT_ID('dbo.CallProtocols')
		AND fk.referenced_object_id = OBJECT_ID('dbo.DispatchProtocols')
		AND fk.delete_referential_action <> 0;

IF @fk IS NOT NULL
BEGIN
	EXEC('ALTER TABLE [dbo].[CallProtocols] DROP CONSTRAINT [' + @fk + ']');

	ALTER TABLE [dbo].[CallProtocols] WITH CHECK ADD CONSTRAINT [FK_CallProtocols_DispatchProtocols_DispatchProtocolId]
		FOREIGN KEY ([DispatchProtocolId]) REFERENCES [dbo].[DispatchProtocols] ([DispatchProtocolId]);
END");
		}

		public override void Down()
		{
			Execute.Sql(@"
IF OBJECT_ID('FK_CallProtocols_DispatchProtocols_DispatchProtocolId', 'F') IS NOT NULL
BEGIN
	ALTER TABLE [dbo].[CallProtocols] DROP CONSTRAINT [FK_CallProtocols_DispatchProtocols_DispatchProtocolId];

	ALTER TABLE [dbo].[CallProtocols] WITH CHECK ADD CONSTRAINT [FK_dbo.CallProtocols_dbo.DispatchProtocols_DispatchProtocolId]
		FOREIGN KEY ([DispatchProtocolId]) REFERENCES [dbo].[DispatchProtocols] ([DispatchProtocolId]) ON DELETE CASCADE;
END");

			Execute.Sql("IF COL_LENGTH('DispatchProtocols', 'DeletedOn') IS NOT NULL ALTER TABLE [DispatchProtocols] DROP COLUMN [DeletedOn];");
			Execute.Sql("IF OBJECT_ID('DF_DispatchProtocols_IsDeleted', 'D') IS NOT NULL ALTER TABLE [DispatchProtocols] DROP CONSTRAINT [DF_DispatchProtocols_IsDeleted];");
			Execute.Sql("IF COL_LENGTH('DispatchProtocols', 'IsDeleted') IS NOT NULL ALTER TABLE [DispatchProtocols] DROP COLUMN [IsDeleted];");
		}
	}
}
