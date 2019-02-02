namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System.Data.Entity.Migrations;

	public partial class ModifiedCallsTable : DbMigration
	{
		public override void Up()
		{
			AlterColumn("Calls", "Notes", c => c.String(maxLength: 4000));
		}

		public override void Down()
		{
			AlterColumn("Calls", "Notes", c => c.String());
		}
	}
}
