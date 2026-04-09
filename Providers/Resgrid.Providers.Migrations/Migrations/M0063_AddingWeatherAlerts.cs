using FluentMigrator;
using System;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(63)]
	public class M0063_AddingWeatherAlerts : Migration
	{
		public override void Up()
		{
			// WeatherAlertSources table
			Create.Table("WeatherAlertSources")
				.WithColumn("WeatherAlertSourceId").AsGuid().NotNullable().PrimaryKey().WithDefault(SystemMethods.NewGuid)
				.WithColumn("DepartmentId").AsInt32().NotNullable()
				.WithColumn("Name").AsString(200).NotNullable()
				.WithColumn("SourceType").AsInt32().NotNullable()
				.WithColumn("AreaFilter").AsString(1000).Nullable()
				.WithColumn("ApiKey").AsString(500).Nullable()
				.WithColumn("CustomEndpoint").AsString(2000).Nullable()
				.WithColumn("PollIntervalMinutes").AsInt32().NotNullable().WithDefaultValue(5)
				.WithColumn("Active").AsBoolean().NotNullable().WithDefaultValue(true)
				.WithColumn("LastPollUtc").AsDateTime2().Nullable()
				.WithColumn("LastSuccessUtc").AsDateTime2().Nullable()
				.WithColumn("IsFailure").AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("ErrorMessage").AsString(2000).Nullable()
				.WithColumn("LastETag").AsString(500).Nullable()
				.WithColumn("CreatedOn").AsDateTime2().NotNullable()
				.WithColumn("CreatedByUserId").AsString(128).NotNullable();

			Create.ForeignKey("FK_WeatherAlertSources_Departments")
				.FromTable("WeatherAlertSources").ForeignColumn("DepartmentId")
				.ToTable("Departments").PrimaryColumn("DepartmentId");

			Create.Index("IX_WeatherAlertSources_DepartmentId")
				.OnTable("WeatherAlertSources").OnColumn("DepartmentId").Ascending();

			Create.Index("IX_WeatherAlertSources_Active")
				.OnTable("WeatherAlertSources").OnColumn("Active").Ascending();

			// WeatherAlerts table
			Create.Table("WeatherAlerts")
				.WithColumn("WeatherAlertId").AsGuid().NotNullable().PrimaryKey().WithDefault(SystemMethods.NewGuid)
				.WithColumn("DepartmentId").AsInt32().NotNullable()
				.WithColumn("WeatherAlertSourceId").AsGuid().NotNullable()
				.WithColumn("ExternalId").AsString(500).NotNullable()
				.WithColumn("Sender").AsString(500).Nullable()
				.WithColumn("Event").AsString(500).NotNullable()
				.WithColumn("AlertCategory").AsInt32().NotNullable()
				.WithColumn("Severity").AsInt32().NotNullable()
				.WithColumn("Urgency").AsInt32().NotNullable()
				.WithColumn("Certainty").AsInt32().NotNullable()
				.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("Headline").AsString(500).Nullable()
				.WithColumn("Description").AsString(int.MaxValue).Nullable()
				.WithColumn("Instruction").AsString(int.MaxValue).Nullable()
				.WithColumn("AreaDescription").AsString(500).Nullable()
				.WithColumn("Polygon").AsString(int.MaxValue).Nullable()
				.WithColumn("Geocodes").AsString(int.MaxValue).Nullable()
				.WithColumn("CenterGeoLocation").AsString(100).Nullable()
				.WithColumn("OnsetUtc").AsDateTime2().Nullable()
				.WithColumn("ExpiresUtc").AsDateTime2().Nullable()
				.WithColumn("EffectiveUtc").AsDateTime2().NotNullable()
				.WithColumn("SentUtc").AsDateTime2().Nullable()
				.WithColumn("FirstSeenUtc").AsDateTime2().NotNullable()
				.WithColumn("LastUpdatedUtc").AsDateTime2().NotNullable()
				.WithColumn("ReferencesExternalId").AsString(500).Nullable()
				.WithColumn("NotificationSent").AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("SystemMessageId").AsInt32().Nullable();

			Create.ForeignKey("FK_WeatherAlerts_Departments")
				.FromTable("WeatherAlerts").ForeignColumn("DepartmentId")
				.ToTable("Departments").PrimaryColumn("DepartmentId");

			Create.ForeignKey("FK_WeatherAlerts_WeatherAlertSources")
				.FromTable("WeatherAlerts").ForeignColumn("WeatherAlertSourceId")
				.ToTable("WeatherAlertSources").PrimaryColumn("WeatherAlertSourceId");

			Create.Index("IX_WeatherAlerts_DepartmentId")
				.OnTable("WeatherAlerts").OnColumn("DepartmentId").Ascending();

			Create.Index("IX_WeatherAlerts_ExternalId_SourceId")
				.OnTable("WeatherAlerts")
				.OnColumn("ExternalId").Ascending()
				.OnColumn("WeatherAlertSourceId").Ascending()
				.WithOptions().Unique();

			Create.Index("IX_WeatherAlerts_Status_ExpiresUtc")
				.OnTable("WeatherAlerts")
				.OnColumn("Status").Ascending()
				.OnColumn("ExpiresUtc").Ascending();

			Create.Index("IX_WeatherAlerts_NotificationSent")
				.OnTable("WeatherAlerts").OnColumn("NotificationSent").Ascending();

			// WeatherAlertZones table
			Create.Table("WeatherAlertZones")
				.WithColumn("WeatherAlertZoneId").AsGuid().NotNullable().PrimaryKey().WithDefault(SystemMethods.NewGuid)
				.WithColumn("DepartmentId").AsInt32().NotNullable()
				.WithColumn("Name").AsString(200).NotNullable()
				.WithColumn("ZoneCode").AsString(100).Nullable()
				.WithColumn("CenterGeoLocation").AsString(100).Nullable()
				.WithColumn("RadiusMiles").AsDouble().NotNullable()
				.WithColumn("IsActive").AsBoolean().NotNullable().WithDefaultValue(true)
				.WithColumn("IsPrimary").AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("CreatedOn").AsDateTime2().NotNullable();

			Create.ForeignKey("FK_WeatherAlertZones_Departments")
				.FromTable("WeatherAlertZones").ForeignColumn("DepartmentId")
				.ToTable("Departments").PrimaryColumn("DepartmentId");

			Create.Index("IX_WeatherAlertZones_DepartmentId")
				.OnTable("WeatherAlertZones").OnColumn("DepartmentId").Ascending();
		}

		public override void Down()
		{
			Delete.Table("WeatherAlertZones");
			Delete.Table("WeatherAlerts");
			Delete.Table("WeatherAlertSources");
		}
	}
}
