namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedUse24HourTimeToDepartmentTable : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.Departments", "Use24HourTime", c => c.Boolean());
        }
        
        public override void Down()
        {
            DropColumn("dbo.Departments", "Use24HourTime");
        }
    }
}
