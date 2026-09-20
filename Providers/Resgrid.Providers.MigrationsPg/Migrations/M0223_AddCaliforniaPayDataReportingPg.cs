using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0223 (Workforce &amp; Business Operations plan, Phase E). Same number, lower-case identifiers, guarded for safe retry.
	/// </summary>
	[Migration(223)]
	public class M0223_AddCaliforniaPayDataReportingPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("paydatareportingdemographics").Exists())
			{
				Create.Table("paydatareportingdemographics")
					.WithColumn("paydatareportingdemographicid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("workforceworkerid").AsString(36).Nullable()
					.WithColumn("effectiveon").AsDateTime2().NotNullable()
					.WithColumn("expireson").AsDateTime2().Nullable()
					.WithColumn("hispaniclatino").AsCustom("text").Nullable()
					.WithColumn("raceethnicitycodes").AsCustom("text").Nullable()
					.WithColumn("sexcode").AsCustom("text").Nullable()
					.WithColumn("declinedraceethnicity").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("declinedsex").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("collectionsource").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("collectedon").AsDateTime2().Nullable()
					.WithColumn("collectedbyuserid").AsString(128).Nullable()
					.WithColumn("reviewedon").AsDateTime2().Nullable()
					.WithColumn("reviewedbyuserid").AsString(128).Nullable()
					.WithColumn("version").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsString(128).Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsString(128).Nullable()
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_paydatareportingdemographics_worker ON paydatareportingdemographics (workforceworkerid, effectiveon);");
			}
			if (!Schema.Table("paydatareportruns").Exists())
			{
				Create.Table("paydatareportruns")
					.WithColumn("paydatareportrunid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("reporttype").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("reportingyear").AsInt32().NotNullable()
					.WithColumn("schemaprofilecode").AsString(50).Nullable()
					.WithColumn("schemaprofilehash").AsString(128).Nullable()
					.WithColumn("snapshotstart").AsDateTime2().NotNullable()
					.WithColumn("snapshotend").AsDateTime2().NotNullable()
					.WithColumn("employersnapshotjson").AsCustom("text").Nullable()
					.WithColumn("sourcecutoff").AsDateTime2().Nullable()
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("employeecount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("rowcount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("exceptioncount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("warningcount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("validationsummaryjson").AsCustom("text").Nullable()
					.WithColumn("runremarks").AsCustom("text").Nullable()
					.WithColumn("supersedesrunid").AsString(36).Nullable()
					.WithColumn("reviewedbyuserid").AsString(128).Nullable()
					.WithColumn("reviewedon").AsDateTime2().Nullable()
					.WithColumn("frozenbyuserid").AsString(128).Nullable()
					.WithColumn("frozenon").AsDateTime2().Nullable()
					.WithColumn("exportedbyuserid").AsString(128).Nullable()
					.WithColumn("exportedon").AsDateTime2().Nullable()
					.WithColumn("certifiedbyuserid").AsString(128).Nullable()
					.WithColumn("certifiedon").AsDateTime2().Nullable()
					.WithColumn("certificationreference").AsString(200).Nullable()
					.WithColumn("certifiedartifactchecksum").AsString(128).Nullable()
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsString(128).Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsString(128).Nullable()
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_paydatareportruns_department ON paydatareportruns (departmentid, reportingyear, reporttype, isdeleted);");
			}
			if (!Schema.Table("paydatareportemployeesnapshots").Exists())
			{
				Create.Table("paydatareportemployeesnapshots")
					.WithColumn("paydatareportemployeesnapshotid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("paydatareportrunid").AsString(36).Nullable()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("workforceworkerid").AsString(36).Nullable()
					.WithColumn("workforceemploymentid").AsString(36).Nullable()
					.WithColumn("workforceestablishmentid").AsString(36).Nullable()
					.WithColumn("workforcelaborcontractorid").AsString(36).Nullable()
					.WithColumn("jobcategorycode").AsString(10).Nullable()
					.WithColumn("demographiccode").AsCustom("text").Nullable()
					.WithColumn("paybandcode").AsString(10).Nullable()
					.WithColumn("exemptioncode").AsString(10).Nullable()
					.WithColumn("employmenttypecode").AsString(10).Nullable()
					.WithColumn("workmode").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("annualearnings").AsCustom("text").Nullable()
					.WithColumn("earningssource").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("annualhours").AsDecimal(12,2).NotNullable().WithDefaultValue(0)
					.WithColumn("annualweeks").AsDecimal(9,2).NotNullable().WithDefaultValue(0)
					.WithColumn("hourlyrate").AsCustom("text").Nullable()
					.WithColumn("isincluded").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("exceptioncodescsv").AsString(500).Nullable()
					.WithColumn("overridereason").AsString(500).Nullable()
					.WithColumn("overridebyuserid").AsString(128).Nullable()
					.WithColumn("sourceversions").AsCustom("text").Nullable()
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsString(128).Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsString(128).Nullable()
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_paydatareportemployeesnapshots_run ON paydatareportemployeesnapshots (paydatareportrunid);");
			}
			if (!Schema.Table("paydatareportrows").Exists())
			{
				Create.Table("paydatareportrows")
					.WithColumn("paydatareportrowid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("paydatareportrunid").AsString(36).Nullable()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("workforceestablishmentid").AsString(36).Nullable()
					.WithColumn("workforcelaborcontractorid").AsString(36).Nullable()
					.WithColumn("jobcategorycode").AsString(10).Nullable()
					.WithColumn("demographiccode").AsCustom("text").Nullable()
					.WithColumn("paybandcode").AsString(10).Nullable()
					.WithColumn("exemptioncode").AsString(10).Nullable()
					.WithColumn("employmenttypecode").AsString(10).Nullable()
					.WithColumn("employeecount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("annualhours").AsDecimal(12,2).NotNullable().WithDefaultValue(0)
					.WithColumn("annualweeks").AsDecimal(9,2).NotNullable().WithDefaultValue(0)
					.WithColumn("meanhourlyrate").AsCustom("text").Nullable()
					.WithColumn("medianhourlyrate").AsCustom("text").Nullable()
					.WithColumn("nonremotecount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("remotewithincaliforniacount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("remoteoutsidecaliforniacount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("rowremarks").AsCustom("text").Nullable()
					.WithColumn("contributingsnapshotidscsv").AsCustom("text").Nullable()
					.WithColumn("sortorder").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_paydatareportrows_run ON paydatareportrows (paydatareportrunid, sortorder);");
			}
			if (!Schema.Table("paydataexportartifacts").Exists())
			{
				Create.Table("paydataexportartifacts")
					.WithColumn("paydataexportartifactid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("paydatareportrunid").AsString(36).Nullable()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("schemaprofilecode").AsString(50).Nullable()
					.WithColumn("schemaprofilehash").AsString(128).Nullable()
					.WithColumn("format").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("filename").AsString(250).Nullable()
					.WithColumn("checksum").AsString(128).Nullable()
					.WithColumn("data").AsCustom("bytea").Nullable()
					.WithColumn("size").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("expireson").AsDateTime2().NotNullable()
					.WithColumn("purgedon").AsDateTime2().Nullable()
					.WithColumn("exportedbyuserid").AsString(128).Nullable()
					.WithColumn("downloadcount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("lastdownloadedon").AsDateTime2().Nullable()
					.WithColumn("lastdownloadedbyuserid").AsString(128).Nullable()
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_paydataexportartifacts_run ON paydataexportartifacts (paydatareportrunid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_paydataexportartifacts_expiry ON paydataexportartifacts (expireson, purgedon);");
			}
		}

		public override void Down()
		{
			if (Schema.Table("paydataexportartifacts").Exists()) Delete.Table("paydataexportartifacts");
			if (Schema.Table("paydatareportrows").Exists()) Delete.Table("paydatareportrows");
			if (Schema.Table("paydatareportemployeesnapshots").Exists()) Delete.Table("paydatareportemployeesnapshots");
			if (Schema.Table("paydatareportruns").Exists()) Delete.Table("paydatareportruns");
			if (Schema.Table("paydatareportingdemographics").Exists()) Delete.Table("paydatareportingdemographics");
		}
	}
}
