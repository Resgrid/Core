using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(54)]
	public class M0054_AddContactIdToRouteStopsPg : Migration
	{
		public override void Up()
		{
			Alter.Table("routestops")
				.AddColumn("contactid").AsCustom("citext").Nullable();
		}

		public override void Down()
		{
			Delete.Column("contactid").FromTable("routestops");
		}
	}
}
