namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedCustomStateDetailTable : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.CustomStateDetails",
                c => new
                    {
                        CustomStateDetailId = c.Int(nullable: false, identity: true),
                        CustomStateId = c.Int(nullable: false),
                        ButtonText = c.String(nullable: false),
                        ButtonColor = c.String(nullable: false),
                        GpsRequired = c.Boolean(nullable: false),
                        NoteType = c.Int(nullable: false),
                        DetailType = c.Int(nullable: false),
                        IsDeleted = c.Boolean(nullable: false),
                    })
                .PrimaryKey(t => t.CustomStateDetailId)
                .ForeignKey("dbo.CustomStates", t => t.CustomStateId, cascadeDelete: true)
                .Index(t => t.CustomStateId);
            
            DropColumn("dbo.CustomStates", "ButtonText");
            DropColumn("dbo.CustomStates", "ButtonColor");
            DropColumn("dbo.CustomStates", "GpsRequired");
            DropColumn("dbo.CustomStates", "NoteType");
            DropColumn("dbo.CustomStates", "DetailType");
        }
        
        public override void Down()
        {
            AddColumn("dbo.CustomStates", "DetailType", c => c.Int(nullable: false));
            AddColumn("dbo.CustomStates", "NoteType", c => c.Int(nullable: false));
            AddColumn("dbo.CustomStates", "GpsRequired", c => c.Boolean(nullable: false));
            AddColumn("dbo.CustomStates", "ButtonColor", c => c.String(nullable: false));
            AddColumn("dbo.CustomStates", "ButtonText", c => c.String(nullable: false));
            DropForeignKey("dbo.CustomStateDetails", "CustomStateId", "dbo.CustomStates");
            DropIndex("dbo.CustomStateDetails", new[] { "CustomStateId" });
            DropTable("dbo.CustomStateDetails");
        }
    }
}
