namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System.Data.Entity.Migrations;
    
    public partial class AddedReadOnToMessages : DbMigration
    {
        public override void Up()
        {
            AddColumn("Messages", "ReadOn", c => c.DateTime());
        }
        
        public override void Down()
        {
            DropColumn("Messages", "ReadOn");
        }
    }
}
