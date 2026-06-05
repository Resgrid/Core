using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(74)]
	public class M0074_AddIsPermanentFailureToWeatherAlertSources : Migration
	{
		public override void Up()
		{
			Alter.Table("WeatherAlertSources")
				.AddColumn("IsPermanentFailure").AsBoolean().NotNullable().WithDefaultValue(false);
		}

		public override void Down()
		{
			Delete.Column("IsPermanentFailure").FromTable("WeatherAlertSources");
		}
	}
}
