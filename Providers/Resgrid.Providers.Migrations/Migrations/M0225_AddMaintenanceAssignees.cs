using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
    // IDs are routing metadata, just like the existing single-user and role columns.
    [Migration(225)]
    public class M0225_AddMaintenanceAssignees : Migration
    {
        public override void Up()
        {
            Execute.Sql("IF COL_LENGTH('WorkOrders', 'AssignedToUserIdsJson') IS NULL ALTER TABLE [WorkOrders] ADD [AssignedToUserIdsJson] nvarchar(max) NULL;");
            Execute.Sql("IF COL_LENGTH('WorkOrders', 'AssignedToRoleIdsJson') IS NULL ALTER TABLE [WorkOrders] ADD [AssignedToRoleIdsJson] nvarchar(max) NULL;");
            Execute.Sql("IF COL_LENGTH('WorkOrderRecurrences', 'AssignedToUserIdsJson') IS NULL ALTER TABLE [WorkOrderRecurrences] ADD [AssignedToUserIdsJson] nvarchar(max) NULL;");
            Execute.Sql("IF COL_LENGTH('WorkOrderRecurrences', 'AssignedToRoleIdsJson') IS NULL ALTER TABLE [WorkOrderRecurrences] ADD [AssignedToRoleIdsJson] nvarchar(max) NULL;");
        }

        public override void Down()
        {
            Execute.Sql("IF EXISTS (SELECT 1 FROM [WorkOrders] WHERE COALESCE([AssignedToUserIdsJson], '[]') <> '[]' OR COALESCE([AssignedToRoleIdsJson], '[]') <> '[]') THROW 51000, 'Remove multiple-assignee routing before rolling back maintenance assignments.', 1;");
            Execute.Sql("IF EXISTS (SELECT 1 FROM [WorkOrderRecurrences] WHERE COALESCE([AssignedToUserIdsJson], '[]') <> '[]' OR COALESCE([AssignedToRoleIdsJson], '[]') <> '[]') THROW 51000, 'Remove multiple-assignee routing before rolling back maintenance assignments.', 1;");
            Delete.Column("AssignedToUserIdsJson").FromTable("WorkOrders");
            Delete.Column("AssignedToRoleIdsJson").FromTable("WorkOrders");
            Delete.Column("AssignedToUserIdsJson").FromTable("WorkOrderRecurrences");
            Delete.Column("AssignedToRoleIdsJson").FromTable("WorkOrderRecurrences");
        }
    }
}

