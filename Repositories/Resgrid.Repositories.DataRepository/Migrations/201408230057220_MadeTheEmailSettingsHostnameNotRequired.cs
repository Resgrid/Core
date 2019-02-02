namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class MadeTheEmailSettingsHostnameNotRequired : DbMigration
    {
        public override void Up()
        {
            AlterColumn("dbo.DepartmentCallEmails", "Hostname", c => c.String(maxLength: 500));
        }
        
        public override void Down()
        {
            AlterColumn("dbo.DepartmentCallEmails", "Hostname", c => c.String(nullable: false, maxLength: 500));
        }
    }
}
