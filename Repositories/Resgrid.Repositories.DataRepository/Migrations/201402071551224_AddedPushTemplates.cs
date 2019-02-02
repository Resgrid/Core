namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedPushTemplates : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.PushTemplates",
                c => new
                    {
                        PushTemplateId = c.Int(nullable: false, identity: true),
                        PlatformType = c.Int(nullable: false),
                        Template = c.String(maxLength: 1000),
                    })
                .PrimaryKey(t => t.PushTemplateId);
            
        }
        
        public override void Down()
        {
            DropTable("dbo.PushTemplates");
        }
    }
}
