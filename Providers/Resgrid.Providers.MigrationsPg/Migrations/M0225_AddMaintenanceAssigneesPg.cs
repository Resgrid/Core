using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
    // IDs are routing metadata, just like the existing single-user and role columns.
    [Migration(225)]
    public class M0225_AddMaintenanceAssigneesPg : Migration
    {
        public override void Up()
        {
            Execute.Sql("ALTER TABLE workorders ADD COLUMN IF NOT EXISTS assignedtouseridsjson text NULL;");
            Execute.Sql("ALTER TABLE workorders ADD COLUMN IF NOT EXISTS assignedtoroleidsjson text NULL;");
            Execute.Sql("ALTER TABLE workorderrecurrences ADD COLUMN IF NOT EXISTS assignedtouseridsjson text NULL;");
            Execute.Sql("ALTER TABLE workorderrecurrences ADD COLUMN IF NOT EXISTS assignedtoroleidsjson text NULL;");
        }

        public override void Down()
        {
            Execute.Sql("DO $$ BEGIN IF EXISTS (SELECT 1 FROM workorders WHERE COALESCE(assignedtouseridsjson, '[]') <> '[]' OR COALESCE(assignedtoroleidsjson, '[]') <> '[]') THEN RAISE EXCEPTION 'Remove multiple-assignee routing before rolling back maintenance assignments.'; END IF; END $$;");
            Execute.Sql("DO $$ BEGIN IF EXISTS (SELECT 1 FROM workorderrecurrences WHERE COALESCE(assignedtouseridsjson, '[]') <> '[]' OR COALESCE(assignedtoroleidsjson, '[]') <> '[]') THEN RAISE EXCEPTION 'Remove multiple-assignee routing before rolling back maintenance assignments.'; END IF; END $$;");
            Delete.Column("assignedtouseridsjson").FromTable("workorders");
            Delete.Column("assignedtoroleidsjson").FromTable("workorders");
            Delete.Column("assignedtouseridsjson").FromTable("workorderrecurrences");
            Delete.Column("assignedtoroleidsjson").FromTable("workorderrecurrences");
        }
    }
}

