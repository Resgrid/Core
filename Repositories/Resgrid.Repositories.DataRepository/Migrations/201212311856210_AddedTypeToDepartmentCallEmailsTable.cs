namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedTypeToDepartmentCallEmailsTable : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.DepartmentCallEmails", "FormatType", c => c.Int(nullable: false));
        }
        
        public override void Down()
        {
            DropColumn("dbo.DepartmentCallEmails", "FormatType");
        }
    }
}
