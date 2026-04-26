using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(67)]
	public class M0067_AddingPoiNamePg : Migration
	{
		public override void Up()
		{
			Alter.Table("Pois".ToLower())
				.AddColumn("Name".ToLower()).AsCustom("citext").Nullable();
		}

		public override void Down()
		{
			Delete.Column("Name".ToLower()).FromTable("Pois".ToLower());
		}
	}
}
