using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(66)]
	public class M0066_AddingPoiDestinationsPg : Migration
	{
		public override void Up()
		{
			Alter.Table("Pois".ToLower())
				.AddColumn("Address".ToLower()).AsString(500).Nullable();

			Alter.Table("Calls".ToLower())
				.AddColumn("DestinationPoiId".ToLower()).AsInt32().Nullable();

			Alter.Table("UnitStates".ToLower())
				.AddColumn("DestinationType".ToLower()).AsInt32().Nullable();
		}

		public override void Down()
		{
			Delete.Column("Address".ToLower()).FromTable("Pois".ToLower());
			Delete.Column("DestinationPoiId".ToLower()).FromTable("Calls".ToLower());
			Delete.Column("DestinationType".ToLower()).FromTable("UnitStates".ToLower());
		}
	}
}
