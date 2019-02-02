namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System.Data.Entity.Migrations;
    
    public partial class AddRecipientsToMessageTable : DbMigration
    {
        public override void Up()
        {
            AddColumn("Messages", "Recipients", c => c.String());
        }
        
        public override void Down()
        {
            DropColumn("Messages", "Recipients");
        }
    }
}
