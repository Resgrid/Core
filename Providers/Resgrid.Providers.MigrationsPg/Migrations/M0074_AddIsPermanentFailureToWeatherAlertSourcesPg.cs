using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(74)]
	public class M0074_AddIsPermanentFailureToWeatherAlertSourcesPg : Migration
	{
		public override void Up()
		{
			// Guarded: this migration was renumbered (68 -> 74), so databases upgraded before the
			// renumber already added this column under version 68. Skip the add if it exists.
			if (!Schema.Table("weatheralertsources").Column("ispermanentfailure").Exists())
			{
				Alter.Table("weatheralertsources")
					.AddColumn("ispermanentfailure").AsBoolean().NotNullable().WithDefaultValue(false);
			}
		}

		public override void Down()
		{
			Delete.Column("ispermanentfailure").FromTable("weatheralertsources");
		}
	}
}
