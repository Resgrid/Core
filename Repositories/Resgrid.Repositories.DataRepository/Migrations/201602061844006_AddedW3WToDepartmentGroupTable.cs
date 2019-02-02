namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedW3WToDepartmentGroupTable : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.DepartmentGroups", "What3Words", c => c.String());
        }
        
        public override void Down()
        {
            DropColumn("dbo.DepartmentGroups", "What3Words");
        }
    }
}
