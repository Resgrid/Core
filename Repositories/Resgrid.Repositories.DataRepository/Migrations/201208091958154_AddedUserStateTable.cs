namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System.Data.Entity.Migrations;
    
    public partial class AddedUserStateTable : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "UserStates",
                c => new
                    {
                        UserStateId = c.Int(nullable: false, identity: true),
                        UserId = c.Guid(nullable: false),
                        State = c.Int(nullable: false),
                        Timestamp = c.DateTime(nullable: false),
                    })
                .PrimaryKey(t => t.UserStateId)
                .ForeignKey("Users", t => t.UserId)
                .Index(t => t.UserId);
            
        }
        
        public override void Down()
        {
            DropIndex("UserStates", new[] { "UserId" });
            DropForeignKey("UserStates", "UserId", "Users");
            DropTable("UserStates");
        }
    }
}
