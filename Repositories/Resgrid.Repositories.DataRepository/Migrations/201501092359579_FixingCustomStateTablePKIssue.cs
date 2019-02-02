namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class FixingCustomStateTablePKIssue : DbMigration
    {
        public override void Up()
        {
            DropForeignKey("dbo.CustomStateDetails", "CustomStateId", "dbo.CustomStates");
            DropPrimaryKey("dbo.CustomStates");
						DropColumn("dbo.CustomStates", "CustomStateTypeId");
            AddColumn("dbo.CustomStates", "CustomStateId", c => c.Int(nullable: false, identity: true));
            AddPrimaryKey("dbo.CustomStates", "CustomStateId");
            AddForeignKey("dbo.CustomStateDetails", "CustomStateId", "dbo.CustomStates", "CustomStateId", cascadeDelete: true);
        }
        
        public override void Down()
        {
            AddColumn("dbo.CustomStates", "CustomStateTypeId", c => c.Int(nullable: false, identity: true));
            DropForeignKey("dbo.CustomStateDetails", "CustomStateId", "dbo.CustomStates");
            DropPrimaryKey("dbo.CustomStates");
            DropColumn("dbo.CustomStates", "CustomStateId");
            AddPrimaryKey("dbo.CustomStates", "CustomStateTypeId");
            AddForeignKey("dbo.CustomStateDetails", "CustomStateId", "dbo.CustomStates", "CustomStateId", cascadeDelete: true);
        }
    }
}
