using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Department lists get the canonical meaning statuses already have (CustomStateDetails.BaseType), so logic can work on
	/// the built-in and the department's own values alike:
	///  * DepartmentCallPriorities.BaseType: the built-in CallPriority a department priority means (0 Low .. 3 Emergency).
	///  * CallTypes.BaseType: a coarse CallTypeClasses value (Fire, EMS, Rescue, ...), not NERIS: Resgrid is used worldwide.
	///  * UnitTypes.BaseType: a UnitTypeClasses value (Engine, Ladder, Ambulance, ...).
	/// -1 is "no meaning set", which every existing row gets; an administrator sets them (suggestions are never applied
	/// silently).
	///
	/// Calls.CallTypeId records which call type a call was logged with. Calls stored only the type's name, so renaming a
	/// type cut its old calls off from timers, protocols and run cards keyed on it. Existing calls are matched to their
	/// department's type of the same name, a CallId range at a time so no batch scans the whole table; a name that matches
	/// no type stays unlinked (null) and keeps working by name.
	/// </summary>
	// No wrapping transaction: the backfill commits a CallId range at a time, and every step is safe to re-run.
	[Migration(272, TransactionBehavior.None)]
	public class M0272_AddListBaseTypes : Migration
	{
		public override void Up()
		{
			Execute.Sql("IF COL_LENGTH('DepartmentCallPriorities', 'BaseType') IS NULL ALTER TABLE [DepartmentCallPriorities] ADD [BaseType] int NOT NULL CONSTRAINT [DF_DepartmentCallPriorities_BaseType] DEFAULT -1;");
			Execute.Sql("IF COL_LENGTH('CallTypes', 'BaseType') IS NULL ALTER TABLE [CallTypes] ADD [BaseType] int NOT NULL CONSTRAINT [DF_CallTypes_BaseType] DEFAULT -1;");
			Execute.Sql("IF COL_LENGTH('UnitTypes', 'BaseType') IS NULL ALTER TABLE [UnitTypes] ADD [BaseType] int NOT NULL CONSTRAINT [DF_UnitTypes_BaseType] DEFAULT -1;");
			Execute.Sql("IF COL_LENGTH('Calls', 'CallTypeId') IS NULL ALTER TABLE [Calls] ADD [CallTypeId] int NULL;");

			Execute.Sql(@"
DECLARE @lo int = 0;
DECLARE @maxId int = (SELECT ISNULL(MAX(CallId), 0) FROM Calls);

WHILE @lo < @maxId
BEGIN
	UPDATE c SET c.CallTypeId = m.CallTypeId
	FROM Calls c
	INNER JOIN (SELECT DepartmentId, [Type], MIN(CallTypeId) AS CallTypeId FROM CallTypes GROUP BY DepartmentId, [Type]) m
		ON m.DepartmentId = c.DepartmentId AND m.[Type] = c.[Type]
	WHERE c.CallId > @lo AND c.CallId <= @lo + 50000 AND c.CallTypeId IS NULL;

	SET @lo = @lo + 50000;
END");
		}

		public override void Down()
		{
			Execute.Sql("IF COL_LENGTH('Calls', 'CallTypeId') IS NOT NULL ALTER TABLE [Calls] DROP COLUMN [CallTypeId];");
			Execute.Sql("IF OBJECT_ID('DF_UnitTypes_BaseType', 'D') IS NOT NULL ALTER TABLE [UnitTypes] DROP CONSTRAINT [DF_UnitTypes_BaseType];");
			Execute.Sql("IF COL_LENGTH('UnitTypes', 'BaseType') IS NOT NULL ALTER TABLE [UnitTypes] DROP COLUMN [BaseType];");
			Execute.Sql("IF OBJECT_ID('DF_CallTypes_BaseType', 'D') IS NOT NULL ALTER TABLE [CallTypes] DROP CONSTRAINT [DF_CallTypes_BaseType];");
			Execute.Sql("IF COL_LENGTH('CallTypes', 'BaseType') IS NOT NULL ALTER TABLE [CallTypes] DROP COLUMN [BaseType];");
			Execute.Sql("IF OBJECT_ID('DF_DepartmentCallPriorities_BaseType', 'D') IS NOT NULL ALTER TABLE [DepartmentCallPriorities] DROP CONSTRAINT [DF_DepartmentCallPriorities_BaseType];");
			Execute.Sql("IF COL_LENGTH('DepartmentCallPriorities', 'BaseType') IS NOT NULL ALTER TABLE [DepartmentCallPriorities] DROP COLUMN [BaseType];");
		}
	}
}
