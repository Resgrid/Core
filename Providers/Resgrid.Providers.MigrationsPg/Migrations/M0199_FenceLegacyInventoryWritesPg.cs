using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(199)]
	public class M0199_FenceLegacyInventoryWritesPg : Migration
	{
		private static readonly string[] LegacyTables = { "inventories", "inventorytypes" };
		public override void Up()
		{
			if (!Schema.Table("inventoryoperations").Exists() || !Schema.Table("departments").Exists()) return;
			// VOLATILE supplies a fresh Read Committed snapshot for the marker query after a lock wait.
			// An older Repeatable Read snapshot cannot prove absence of a committed marker, so refuse that writer.
			Execute.Sql(@"CREATE FUNCTION public.resgrid_inventory_legacy_write_fence() RETURNS trigger
LANGUAGE plpgsql VOLATILE AS $inventory_fence$
DECLARE
	old_department integer;
	new_department integer;
	affected_department integer;
BEGIN
	IF current_setting('transaction_isolation') NOT IN ('read committed', 'read uncommitted') THEN
		RAISE EXCEPTION 'Legacy inventory mutations require read committed isolation for the modernization fence.' USING ERRCODE = '55000';
	END IF;
	IF TG_OP <> 'INSERT' THEN old_department := OLD.departmentid; END IF;
	IF TG_OP <> 'DELETE' THEN new_department := NEW.departmentid; END IF;
	FOR affected_department IN
		SELECT DISTINCT holder FROM unnest(ARRAY[old_department,new_department]) AS changed(holder)
		WHERE holder IS NOT NULL ORDER BY holder
	LOOP
		PERFORM departmentid FROM public.departments WHERE departmentid = affected_department FOR UPDATE;
		IF EXISTS (SELECT 1 FROM public.inventoryoperations WHERE departmentid = affected_department
			AND requestid = '00000000-0000-0000-0000-000000000001' AND state = 2) THEN
			RAISE EXCEPTION 'Legacy inventory writes are disabled after inventory modernization. Use the modern inventory command.' USING ERRCODE = '55000';
		END IF;
	END LOOP;
	IF TG_OP = 'DELETE' THEN RETURN OLD; END IF;
	RETURN NEW;
END;
$inventory_fence$;");
			foreach (var table in LegacyTables)
			{
				if (!Schema.Table(table).Exists()) continue;
				Execute.Sql($@"CREATE TRIGGER tr_{table}_inventorymodernizationfence
BEFORE INSERT OR UPDATE OR DELETE ON public.{table}
FOR EACH ROW EXECUTE FUNCTION public.resgrid_inventory_legacy_write_fence();");
			}
		}
		public override void Down()
		{
			foreach (var table in LegacyTables)
				if (Schema.Table(table).Exists()) Execute.Sql($"DROP TRIGGER IF EXISTS tr_{table}_inventorymodernizationfence ON public.{table};");
			Execute.Sql("DROP FUNCTION IF EXISTS public.resgrid_inventory_legacy_write_fence();");
		}
	}
}
