namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedMessageEmailToDepartmentGroup : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.DepartmentGroups", "MessageEmail", c => c.String());
        }
        
        public override void Down()
        {
            DropColumn("dbo.DepartmentGroups", "MessageEmail");
        }
    }
}
