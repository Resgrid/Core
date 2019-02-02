namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedCallNotesTable : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.CallNotes",
                c => new
                    {
                        CallNoteId = c.Int(nullable: false, identity: true),
                        CallId = c.Int(nullable: false),
                        UserId = c.Guid(nullable: false),
                        Note = c.String(),
                        Source = c.Int(nullable: false),
                        Timestamp = c.DateTime(nullable: false),
                        Latitude = c.Decimal(precision: 18, scale: 2),
                        Longitude = c.Decimal(precision: 18, scale: 2),
                    })
                .PrimaryKey(t => t.CallNoteId)
                .ForeignKey("dbo.Calls", t => t.CallId, cascadeDelete: true)
                .ForeignKey("dbo.Users", t => t.UserId)
                .Index(t => t.CallId)
                .Index(t => t.UserId);
            
        }
        
        public override void Down()
        {
            DropForeignKey("dbo.CallNotes", "UserId", "dbo.Users");
            DropForeignKey("dbo.CallNotes", "CallId", "dbo.Calls");
            DropIndex("dbo.CallNotes", new[] { "UserId" });
            DropIndex("dbo.CallNotes", new[] { "CallId" });
            DropTable("dbo.CallNotes");
        }
    }
}
