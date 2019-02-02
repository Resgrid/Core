namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedPlanLimitsTable : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.PlanLimits",
                c => new
                    {
                        PlanLimitId = c.Int(nullable: false, identity: true),
                        PlanId = c.Int(nullable: false),
                        LimitType = c.Int(nullable: false),
                        LimitValue = c.Int(nullable: false),
                    })
                .PrimaryKey(t => t.PlanLimitId)
                .ForeignKey("dbo.Plans", t => t.PlanId, cascadeDelete: true)
                .Index(t => t.PlanId);
            
        }
        
        public override void Down()
        {
            DropIndex("dbo.PlanLimits", new[] { "PlanId" });
            DropForeignKey("dbo.PlanLimits", "PlanId", "dbo.Plans");
            DropTable("dbo.PlanLimits");
        }
    }
}
