using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(63)]
	public class M0063_AddingWeatherAlertsPg : Migration
	{
		public override void Up()
		{
			// weatheralertsources table
			Create.Table("weatheralertsources")
				.WithColumn("weatheralertsourceid").AsCustom("citext").NotNullable().PrimaryKey()
				.WithColumn("departmentid").AsInt32().NotNullable()
				.WithColumn("name").AsCustom("citext").NotNullable()
				.WithColumn("sourcetype").AsInt32().NotNullable()
				.WithColumn("areafilter").AsCustom("citext").Nullable()
				.WithColumn("apikey").AsCustom("citext").Nullable()
				.WithColumn("customendpoint").AsCustom("citext").Nullable()
				.WithColumn("pollintervalminutes").AsInt32().NotNullable().WithDefaultValue(5)
				.WithColumn("active").AsBoolean().NotNullable().WithDefaultValue(true)
				.WithColumn("lastpollutc").AsDateTime().Nullable()
				.WithColumn("lastsuccessutc").AsDateTime().Nullable()
				.WithColumn("isfailure").AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("errormessage").AsCustom("citext").Nullable()
				.WithColumn("lastetag").AsCustom("citext").Nullable()
				.WithColumn("createdon").AsDateTime().NotNullable()
				.WithColumn("createdbyuserid").AsCustom("citext").NotNullable();

			Create.ForeignKey("fk_weatheralertsources_departments")
				.FromTable("weatheralertsources").ForeignColumn("departmentid")
				.ToTable("departments").PrimaryColumn("departmentid");

			Create.Index("ix_weatheralertsources_departmentid")
				.OnTable("weatheralertsources")
				.OnColumn("departmentid");

			Create.Index("ix_weatheralertsources_active")
				.OnTable("weatheralertsources")
				.OnColumn("active");

			// weatheralerts table
			Create.Table("weatheralerts")
				.WithColumn("weatheralertid").AsCustom("citext").NotNullable().PrimaryKey()
				.WithColumn("departmentid").AsInt32().NotNullable()
				.WithColumn("weatheralertsourceid").AsCustom("citext").NotNullable()
				.WithColumn("externalid").AsCustom("citext").NotNullable()
				.WithColumn("sender").AsCustom("citext").Nullable()
				.WithColumn("event").AsCustom("citext").NotNullable()
				.WithColumn("alertcategory").AsInt32().NotNullable()
				.WithColumn("severity").AsInt32().NotNullable()
				.WithColumn("urgency").AsInt32().NotNullable()
				.WithColumn("certainty").AsInt32().NotNullable()
				.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("headline").AsCustom("citext").Nullable()
				.WithColumn("description").AsCustom("text").Nullable()
				.WithColumn("instruction").AsCustom("text").Nullable()
				.WithColumn("areadescription").AsCustom("citext").Nullable()
				.WithColumn("polygon").AsCustom("text").Nullable()
				.WithColumn("geocodes").AsCustom("text").Nullable()
				.WithColumn("centergeolocation").AsCustom("citext").Nullable()
				.WithColumn("onsetutc").AsDateTime().Nullable()
				.WithColumn("expiresutc").AsDateTime().Nullable()
				.WithColumn("effectiveutc").AsDateTime().NotNullable()
				.WithColumn("sentutc").AsDateTime().Nullable()
				.WithColumn("firstseenutc").AsDateTime().NotNullable()
				.WithColumn("lastupdatedutc").AsDateTime().NotNullable()
				.WithColumn("referencesexternalid").AsCustom("citext").Nullable()
				.WithColumn("notificationsent").AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("systemmessageid").AsInt32().Nullable();

			Create.ForeignKey("fk_weatheralerts_departments")
				.FromTable("weatheralerts").ForeignColumn("departmentid")
				.ToTable("departments").PrimaryColumn("departmentid");

			Create.ForeignKey("fk_weatheralerts_weatheralertsources")
				.FromTable("weatheralerts").ForeignColumn("weatheralertsourceid")
				.ToTable("weatheralertsources").PrimaryColumn("weatheralertsourceid");

			Create.Index("ix_weatheralerts_departmentid")
				.OnTable("weatheralerts")
				.OnColumn("departmentid");

			Create.Index("ix_weatheralerts_externalid_sourceid")
				.OnTable("weatheralerts")
				.OnColumn("externalid").Ascending()
				.OnColumn("weatheralertsourceid").Ascending()
				.WithOptions().Unique();

			Create.Index("ix_weatheralerts_status_expiresutc")
				.OnTable("weatheralerts")
				.OnColumn("status").Ascending()
				.OnColumn("expiresutc").Ascending();

			Create.Index("ix_weatheralerts_notificationsent")
				.OnTable("weatheralerts")
				.OnColumn("notificationsent");

			// weatheralertzones table
			Create.Table("weatheralertzones")
				.WithColumn("weatheralertzoneid").AsCustom("citext").NotNullable().PrimaryKey()
				.WithColumn("departmentid").AsInt32().NotNullable()
				.WithColumn("name").AsCustom("citext").NotNullable()
				.WithColumn("zonecode").AsCustom("citext").Nullable()
				.WithColumn("centergeolocation").AsCustom("citext").Nullable()
				.WithColumn("radiusmiles").AsDouble().NotNullable()
				.WithColumn("isactive").AsBoolean().NotNullable().WithDefaultValue(true)
				.WithColumn("isprimary").AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("createdon").AsDateTime().NotNullable();

			Create.ForeignKey("fk_weatheralertzones_departments")
				.FromTable("weatheralertzones").ForeignColumn("departmentid")
				.ToTable("departments").PrimaryColumn("departmentid");

			Create.Index("ix_weatheralertzones_departmentid")
				.OnTable("weatheralertzones")
				.OnColumn("departmentid");
		}

		public override void Down()
		{
			Delete.Table("weatheralertzones");
			Delete.Table("weatheralerts");
			Delete.Table("weatheralertsources");
		}
	}
}
