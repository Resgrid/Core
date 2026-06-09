using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(74)]
	public class M0074_AddIsPermanentFailureToWeatherAlertSources : Migration
	{
		public override void Up()
		{
			// Guarded: this migration was renumbered (68 -> 74), so databases upgraded before the
			// renumber already added this column under version 68. Skip the add if it exists.
			if (!Schema.Table("WeatherAlertSources").Column("IsPermanentFailure").Exists())
			{
				Alter.Table("WeatherAlertSources")
					.AddColumn("IsPermanentFailure").AsBoolean().NotNullable().WithDefaultValue(false);
			}
		}

		public override void Down()
		{
			Delete.Column("IsPermanentFailure").FromTable("WeatherAlertSources");
		}
	}
}
