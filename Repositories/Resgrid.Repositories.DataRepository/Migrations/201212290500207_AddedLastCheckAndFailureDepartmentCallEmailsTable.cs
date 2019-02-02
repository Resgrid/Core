namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedLastCheckAndFailureDepartmentCallEmailsTable : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.DepartmentCallEmails", "LastCheck", c => c.DateTime());
            AddColumn("dbo.DepartmentCallEmails", "IsFailure", c => c.Boolean(nullable: false));
            AddColumn("dbo.DepartmentCallEmails", "ErrorMessage", c => c.String(maxLength: 1000));
        }
        
        public override void Down()
        {
            DropColumn("dbo.DepartmentCallEmails", "ErrorMessage");
            DropColumn("dbo.DepartmentCallEmails", "IsFailure");
            DropColumn("dbo.DepartmentCallEmails", "LastCheck");
        }
    }
}
