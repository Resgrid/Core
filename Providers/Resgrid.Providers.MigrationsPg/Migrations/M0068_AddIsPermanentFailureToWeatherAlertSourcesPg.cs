using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(68)]
	public class M0068_AddIsPermanentFailureToWeatherAlertSourcesPg : Migration
	{
		public override void Up()
		{
			Alter.Table("weatheralertsources")
				.AddColumn("ispermanentfailure").AsBoolean().NotNullable().WithDefaultValue(false);
		}

		public override void Down()
		{
			Delete.Column("ispermanentfailure").FromTable("weatheralertsources");
		}
	}
}
