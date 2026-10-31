using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Open shift trade requests (offered to everyone eligible, who then apply) and trades of static shift (workshift)
	/// days. A workshift day has no signup, so SourceShiftSignupId becomes nullable; its foreign key and index have to be
	/// dropped to change that and are put back with the same names and delete rule.
	/// </summary>
	[Migration(306)]
	public class M0306_AddOpenShiftTrades : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ShiftSignupTrades') AND name = 'SourceShiftSignupId' AND is_nullable = 0)
BEGIN
	DECLARE @dropFk nvarchar(max) = N'', @addFk nvarchar(max) = N'', @dropIx nvarchar(max) = N'', @addIx nvarchar(max) = N'';

	SELECT @dropFk = @dropFk + N'ALTER TABLE [ShiftSignupTrades] DROP CONSTRAINT ' + QUOTENAME(fk.name) + N';',
	       @addFk = @addFk + N'ALTER TABLE [ShiftSignupTrades] WITH NOCHECK ADD CONSTRAINT ' + QUOTENAME(fk.name) + N' FOREIGN KEY ([SourceShiftSignupId]) REFERENCES '
	                + QUOTENAME(OBJECT_SCHEMA_NAME(fk.referenced_object_id)) + N'.' + QUOTENAME(OBJECT_NAME(fk.referenced_object_id)) + N' ([ShiftSignupId])'
	                + CASE fk.delete_referential_action WHEN 1 THEN N' ON DELETE CASCADE' WHEN 2 THEN N' ON DELETE SET NULL' ELSE N'' END + N';'
	FROM sys.foreign_keys fk
	INNER JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
	INNER JOIN sys.columns c ON c.object_id = fkc.parent_object_id AND c.column_id = fkc.parent_column_id
	WHERE fk.parent_object_id = OBJECT_ID('ShiftSignupTrades') AND c.name = 'SourceShiftSignupId';

	SELECT @dropIx = @dropIx + N'DROP INDEX ' + QUOTENAME(i.name) + N' ON [ShiftSignupTrades];',
	       @addIx = @addIx + N'CREATE NONCLUSTERED INDEX ' + QUOTENAME(i.name) + N' ON [ShiftSignupTrades] ([SourceShiftSignupId]);'
	FROM sys.indexes i
	WHERE i.object_id = OBJECT_ID('ShiftSignupTrades') AND i.is_primary_key = 0 AND i.is_unique_constraint = 0 AND i.name IS NOT NULL
	  AND EXISTS (SELECT 1 FROM sys.index_columns ic INNER JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
	              WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND c.name = 'SourceShiftSignupId');

	EXEC sp_executesql @dropFk;
	EXEC sp_executesql @dropIx;
	ALTER TABLE [ShiftSignupTrades] ALTER COLUMN [SourceShiftSignupId] int NULL;
	EXEC sp_executesql @addIx;
	EXEC sp_executesql @addFk;
END");

			Execute.Sql("IF COL_LENGTH('ShiftSignupTrades', 'DepartmentId') IS NULL ALTER TABLE [ShiftSignupTrades] ADD [DepartmentId] int NULL;");
			Execute.Sql("IF COL_LENGTH('ShiftSignupTrades', 'SourceUserId') IS NULL ALTER TABLE [ShiftSignupTrades] ADD [SourceUserId] nvarchar(128) NULL;");
			Execute.Sql("IF COL_LENGTH('ShiftSignupTrades', 'SourceDay') IS NULL ALTER TABLE [ShiftSignupTrades] ADD [SourceDay] datetime NULL;");
			Execute.Sql("IF COL_LENGTH('ShiftSignupTrades', 'IsOpen') IS NULL ALTER TABLE [ShiftSignupTrades] ADD [IsOpen] bit NOT NULL CONSTRAINT [DF_ShiftSignupTrades_IsOpen] DEFAULT(0);");
			Execute.Sql("IF COL_LENGTH('ShiftSignupTrades', 'RequestedOn') IS NULL ALTER TABLE [ShiftSignupTrades] ADD [RequestedOn] datetime NULL;");
			Execute.Sql("IF COL_LENGTH('ShiftSignupTrades', 'WorkshiftId') IS NULL ALTER TABLE [ShiftSignupTrades] ADD [WorkshiftId] nvarchar(128) NULL;");
			Execute.Sql("IF COL_LENGTH('ShiftSignupTrades', 'WorkshiftDayId') IS NULL ALTER TABLE [ShiftSignupTrades] ADD [WorkshiftDayId] nvarchar(128) NULL;");
			Execute.Sql("IF COL_LENGTH('ShiftSignupTrades', 'UnitId') IS NULL ALTER TABLE [ShiftSignupTrades] ADD [UnitId] int NULL;");
			Execute.Sql("IF COL_LENGTH('ShiftSignupTrades', 'DepartmentGroupId') IS NULL ALTER TABLE [ShiftSignupTrades] ADD [DepartmentGroupId] int NULL;");
			Execute.Sql("IF COL_LENGTH('ShiftSignupTradeUsers', 'RespondedOn') IS NULL ALTER TABLE [ShiftSignupTradeUsers] ADD [RespondedOn] datetime NULL;");

			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ShiftSignupTrades_DepartmentId_SourceDay' AND object_id = OBJECT_ID('ShiftSignupTrades')) CREATE NONCLUSTERED INDEX [IX_ShiftSignupTrades_DepartmentId_SourceDay] ON [ShiftSignupTrades] ([DepartmentId], [SourceDay]);");
		}

		public override void Down()
		{
			Execute.Sql("IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ShiftSignupTrades_DepartmentId_SourceDay' AND object_id = OBJECT_ID('ShiftSignupTrades')) DROP INDEX [IX_ShiftSignupTrades_DepartmentId_SourceDay] ON [ShiftSignupTrades];");
			Execute.Sql("IF COL_LENGTH('ShiftSignupTradeUsers', 'RespondedOn') IS NOT NULL ALTER TABLE [ShiftSignupTradeUsers] DROP COLUMN [RespondedOn];");
			Execute.Sql("IF COL_LENGTH('ShiftSignupTrades', 'DepartmentGroupId') IS NOT NULL ALTER TABLE [ShiftSignupTrades] DROP COLUMN [DepartmentGroupId];");
			Execute.Sql("IF COL_LENGTH('ShiftSignupTrades', 'UnitId') IS NOT NULL ALTER TABLE [ShiftSignupTrades] DROP COLUMN [UnitId];");
			Execute.Sql("IF COL_LENGTH('ShiftSignupTrades', 'WorkshiftDayId') IS NOT NULL ALTER TABLE [ShiftSignupTrades] DROP COLUMN [WorkshiftDayId];");
			Execute.Sql("IF COL_LENGTH('ShiftSignupTrades', 'WorkshiftId') IS NOT NULL ALTER TABLE [ShiftSignupTrades] DROP COLUMN [WorkshiftId];");
			Execute.Sql("IF COL_LENGTH('ShiftSignupTrades', 'RequestedOn') IS NOT NULL ALTER TABLE [ShiftSignupTrades] DROP COLUMN [RequestedOn];");
			Execute.Sql("IF COL_LENGTH('ShiftSignupTrades', 'IsOpen') IS NOT NULL BEGIN ALTER TABLE [ShiftSignupTrades] DROP CONSTRAINT [DF_ShiftSignupTrades_IsOpen]; ALTER TABLE [ShiftSignupTrades] DROP COLUMN [IsOpen]; END");
			Execute.Sql("IF COL_LENGTH('ShiftSignupTrades', 'SourceDay') IS NOT NULL ALTER TABLE [ShiftSignupTrades] DROP COLUMN [SourceDay];");
			Execute.Sql("IF COL_LENGTH('ShiftSignupTrades', 'SourceUserId') IS NOT NULL ALTER TABLE [ShiftSignupTrades] DROP COLUMN [SourceUserId];");
			Execute.Sql("IF COL_LENGTH('ShiftSignupTrades', 'DepartmentId') IS NOT NULL ALTER TABLE [ShiftSignupTrades] DROP COLUMN [DepartmentId];");

			// Static shift day trades have no source signup; the column stays nullable so older code still reads the rest.
			Execute.Sql("DELETE FROM [ShiftSignupTrades] WHERE [SourceShiftSignupId] IS NULL;");
		}
	}
}
