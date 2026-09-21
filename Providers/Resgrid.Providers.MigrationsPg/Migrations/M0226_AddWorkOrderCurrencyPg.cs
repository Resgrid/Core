using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
    [Migration(226)]
    public class M0226_AddWorkOrderCurrencyPg : Migration
    {
        public override void Up()
        {
            Execute.Sql("ALTER TABLE workorderpolicies ADD COLUMN IF NOT EXISTS currencycode varchar(3) NULL;");
            Execute.Sql("ALTER TABLE workorders ADD COLUMN IF NOT EXISTS currencycode varchar(3) NULL;");
        }

        public override void Down()
        {
            Execute.Sql("DO $$ BEGIN IF EXISTS (SELECT 1 FROM workorderpolicies WHERE currencycode IS NOT NULL) OR EXISTS (SELECT 1 FROM workorders WHERE currencycode IS NOT NULL) THEN RAISE EXCEPTION 'Retained department currency settings and work-order snapshots prevent rollback.'; END IF; END $$;");
            Delete.Column("currencycode").FromTable("workorders");
            Delete.Column("currencycode").FromTable("workorderpolicies");
        }
    }
}
