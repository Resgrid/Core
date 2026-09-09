using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(199)]
	public class M0199_FenceLegacyInventoryWrites : Migration
	{
		private static readonly string[] LegacyTables = { "Inventories", "InventoryTypes" };
		public override void Up()
		{
			if (!Schema.Table("InventoryOperations").Exists() || !Schema.Table("Departments").Exists()) return;
			foreach (var table in LegacyTables)
			{
				if (!Schema.Table(table).Exists()) continue;
				// AFTER triggers preserve the legacy statement's normal identity/FK behavior. Throwing rolls back every row.
				// Match InventoryStore.LockDepartmentAsync, then use a locking marker read even under row-versioned isolation.
				Execute.Sql($@"CREATE TRIGGER [dbo].[TR_{table}_InventoryModernizationFence]
ON [dbo].[{table}]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
	SET NOCOUNT ON;
	DECLARE @departmentId int, @lockedDepartmentId int;
	DECLARE affectedDepartments CURSOR LOCAL FAST_FORWARD FOR
		SELECT DepartmentId FROM (SELECT DepartmentId FROM inserted UNION SELECT DepartmentId FROM deleted) AS affected
		ORDER BY DepartmentId;
	OPEN affectedDepartments;
	FETCH NEXT FROM affectedDepartments INTO @departmentId;
	WHILE @@FETCH_STATUS = 0
	BEGIN
		SELECT @lockedDepartmentId = DepartmentId FROM [dbo].[Departments] WITH (UPDLOCK,HOLDLOCK)
			WHERE DepartmentId = @departmentId;
		IF EXISTS (SELECT 1 FROM [dbo].[InventoryOperations] WITH (UPDLOCK,HOLDLOCK)
			WHERE DepartmentId = @departmentId AND RequestId = '00000000-0000-0000-0000-000000000001' AND State = 2)
			THROW 51000, 'Legacy inventory writes are disabled after inventory modernization. Use the modern inventory command.', 1;
		FETCH NEXT FROM affectedDepartments INTO @departmentId;
	END;
	CLOSE affectedDepartments;
	DEALLOCATE affectedDepartments;
END;");
			}
		}
		public override void Down()
		{
			foreach (var table in LegacyTables) Execute.Sql($"DROP TRIGGER IF EXISTS [dbo].[TR_{table}_InventoryModernizationFence];");
		}
	}
}
