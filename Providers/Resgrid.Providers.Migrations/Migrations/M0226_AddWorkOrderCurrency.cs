using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
    [Migration(226)]
    public class M0226_AddWorkOrderCurrency : Migration
    {
        public override void Up()
        {
            Execute.Sql("IF COL_LENGTH('WorkOrderPolicies', 'CurrencyCode') IS NULL ALTER TABLE [WorkOrderPolicies] ADD [CurrencyCode] nvarchar(3) NULL;");
            Execute.Sql("IF COL_LENGTH('WorkOrders', 'CurrencyCode') IS NULL ALTER TABLE [WorkOrders] ADD [CurrencyCode] nvarchar(3) NULL;");
        }

        public override void Down()
        {
            Execute.Sql("IF EXISTS (SELECT 1 FROM [WorkOrderPolicies] WHERE [CurrencyCode] IS NOT NULL) OR EXISTS (SELECT 1 FROM [WorkOrders] WHERE [CurrencyCode] IS NOT NULL) THROW 51000, 'Retained department currency settings and work-order snapshots prevent rollback.', 1;");
            Delete.Column("CurrencyCode").FromTable("WorkOrders");
            Delete.Column("CurrencyCode").FromTable("WorkOrderPolicies");
        }
    }
}
