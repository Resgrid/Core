namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedUnitStatesTable : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.UnitStates",
                c => new
                    {
                        UnitStateId = c.Int(nullable: false, identity: true),
                        UnitId = c.Int(nullable: false),
                        State = c.Int(nullable: false),
                        Timestamp = c.DateTime(nullable: false),
                    })
                .PrimaryKey(t => t.UnitStateId)
                .ForeignKey("dbo.Units", t => t.UnitId, cascadeDelete: true)
                .Index(t => t.UnitId);
            
        }
        
        public override void Down()
        {
            DropIndex("dbo.UnitStates", new[] { "UnitId" });
            DropForeignKey("dbo.UnitStates", "UnitId", "dbo.Units");
            DropTable("dbo.UnitStates");
        }
    }
}
