namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System.Data.Entity.Migrations;
    
    public partial class AddLogEntries : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "LogEntries",
                c => new
                    {
                        id = c.Int(nullable: false, identity: true),
                        TimeStamp = c.DateTime(nullable: false),
                        Message = c.String(),
                        level = c.String(),
                        logger = c.String(),
                    })
                .PrimaryKey(t => t.id);
            
        }
        
        public override void Down()
        {
            DropTable("LogEntries");
        }
    }
}
