namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System.Data.Entity.Migrations;
    
    public partial class AddedIsDeletedToMessages : DbMigration
    {
        public override void Up()
        {
            AddColumn("Messages", "IsDeleted", c => c.Boolean(nullable: false));
        }
        
        public override void Down()
        {
            DropColumn("Messages", "IsDeleted");
        }
    }
}
