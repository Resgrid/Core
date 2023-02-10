using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(19)]
	public class M0019_AddingDeleteReasonToCalls : Migration
	{
		public override void Up()
		{
			Alter.Table("Calls").AddColumn("DeletedReason").AsString().Nullable();
			Alter.Table("Calls").AddColumn("DeletedByUserId").AsString(128).Nullable();
			Alter.Table("Calls").AddColumn("DeletedOn").AsDateTime2().Nullable();
		}

		public override void Down()
		{

		}
	}
}
