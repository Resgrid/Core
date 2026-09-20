using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0221 (Workforce &amp; Business Operations plan, Phase E). Same number, lower-case identifiers, guarded for safe retry.
	/// </summary>
	[Migration(221)]
	public class M0221_AddWorkforceCompensationAndAnnualFactsPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("employeecompensationprofiles").Exists())
			{
				Create.Table("employeecompensationprofiles")
					.WithColumn("employeecompensationprofileid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("scope").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("workforceemploymentid").AsString(36).Nullable()
					.WithColumn("personnelroleid").AsInt32().Nullable()
					.WithColumn("effectiveon").AsDateTime2().NotNullable()
					.WithColumn("expireson").AsDateTime2().Nullable()
					.WithColumn("currency").AsString(3).Nullable()
					.WithColumn("paybasis").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("baseamount").AsCustom("text").Nullable()
					.WithColumn("regularhourlyequivalent").AsCustom("text").Nullable()
					.WithColumn("standardhoursperday").AsDecimal(9,2).Nullable()
					.WithColumn("standardhoursperweek").AsDecimal(9,2).Nullable()
					.WithColumn("standardhoursperyear").AsDecimal(9,2).Nullable()
					.WithColumn("ratemultipliersjson").AsCustom("text").Nullable()
					.WithColumn("source").AsString(100).Nullable()
					.WithColumn("importbatchid").AsString(36).Nullable()
					.WithColumn("sourcechecksum").AsString(128).Nullable()
					.WithColumn("isapproved").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("approvedbyuserid").AsString(128).Nullable()
					.WithColumn("approvedon").AsDateTime2().Nullable()
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsString(128).Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsString(128).Nullable()
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_employeecompensationprofiles_employment ON employeecompensationprofiles (workforceemploymentid, effectiveon);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_employeecompensationprofiles_department ON employeecompensationprofiles (departmentid, scope, isdeleted);");
			}
			if (!Schema.Table("employeepaycomponents").Exists())
			{
				Create.Table("employeepaycomponents")
					.WithColumn("employeepaycomponentid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("employeecompensationprofileid").AsString(36).Nullable()
					.WithColumn("effectiveon").AsDateTime2().Nullable()
					.WithColumn("expireson").AsDateTime2().Nullable()
					.WithColumn("category").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("name").AsString(100).Nullable()
					.WithColumn("basis").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("amount").AsCustom("text").Nullable()
					.WithColumn("eligiblepaycodescsv").AsString(100).Nullable()
					.WithColumn("paidforeachovertimehour").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("sourceagreement").AsString(250).Nullable()
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsString(128).Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsString(128).Nullable()
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_employeepaycomponents_profile ON employeepaycomponents (employeecompensationprofileid);");
			}
			if (!Schema.Table("employeecostcomponents").Exists())
			{
				Create.Table("employeecostcomponents")
					.WithColumn("employeecostcomponentid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("employeecompensationprofileid").AsString(36).Nullable()
					.WithColumn("effectiveon").AsDateTime2().Nullable()
					.WithColumn("expireson").AsDateTime2().Nullable()
					.WithColumn("category").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("name").AsString(100).Nullable()
					.WithColumn("basis").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("rateamount").AsCustom("text").Nullable()
					.WithColumn("eligiblepaycodescsv").AsString(100).Nullable()
					.WithColumn("cap").AsCustom("text").Nullable()
					.WithColumn("source").AsString(250).Nullable()
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsString(128).Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsString(128).Nullable()
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_employeecostcomponents_profile ON employeecostcomponents (employeecompensationprofileid);");
			}
			if (!Schema.Table("workforceworkentries").Exists())
			{
				Create.Table("workforceworkentries")
					.WithColumn("workforceworkentryid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("workforceworkerid").AsString(36).Nullable()
					.WithColumn("workforceemploymentid").AsString(36).Nullable()
					.WithColumn("workdate").AsDateTime2().NotNullable()
					.WithColumn("starttime").AsDateTime2().Nullable()
					.WithColumn("endtime").AsDateTime2().Nullable()
					.WithColumn("hours").AsDecimal(9,2).NotNullable().WithDefaultValue(0)
					.WithColumn("hourstype").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("workforceestablishmentid").AsString(36).Nullable()
					.WithColumn("workcountry").AsString(2).Nullable()
					.WithColumn("worksubdivision").AsString(10).Nullable()
					.WithColumn("workmode").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("callid").AsInt32().Nullable()
					.WithColumn("deploymentid").AsString(36).Nullable()
					.WithColumn("deploymenttimereportid").AsString(36).Nullable()
					.WithColumn("approvedpayrollcost").AsCustom("text").Nullable()
					.WithColumn("externalsource").AsString(100).Nullable()
					.WithColumn("externalid").AsString(200).Nullable()
					.WithColumn("importbatchid").AsString(36).Nullable()
					.WithColumn("isapproved").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("isreconciled").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsString(128).Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsString(128).Nullable()
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_workforceworkentries_worker ON workforceworkentries (workforceworkerid, workdate);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_workforceworkentries_deployment ON workforceworkentries (deploymentid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_workforceworkentries_call ON workforceworkentries (callid);");
			}
			if (!Schema.Table("workforceannualpayfacts").Exists())
			{
				Create.Table("workforceannualpayfacts")
					.WithColumn("workforceannualpayfactid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("workforceemploymentid").AsString(36).Nullable()
					.WithColumn("reportingyear").AsInt32().NotNullable()
					.WithColumn("reporttype").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("clientallocationkey").AsString(100).Nullable()
					.WithColumn("w2box5").AsCustom("text").Nullable()
					.WithColumn("w2box1").AsCustom("text").Nullable()
					.WithColumn("earningsused").AsCustom("text").Nullable()
					.WithColumn("earningssource").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("actualworkedhours").AsDecimal(12,2).Nullable()
					.WithColumn("paidleavehours").AsDecimal(12,2).Nullable()
					.WithColumn("reportablehours").AsDecimal(12,2).Nullable()
					.WithColumn("daysworked").AsInt32().Nullable()
					.WithColumn("weeksworked").AsDecimal(9,2).Nullable()
					.WithColumn("exemptproxymethod").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("proxyaveragehoursperday").AsDecimal(9,2).Nullable()
					.WithColumn("clientallocatedearnings").AsCustom("text").Nullable()
					.WithColumn("clientallocatedhours").AsDecimal(12,2).Nullable()
					.WithColumn("clientallocatedweeks").AsDecimal(9,2).Nullable()
					.WithColumn("source").AsString(100).Nullable()
					.WithColumn("importbatchid").AsString(36).Nullable()
					.WithColumn("sourcechecksum").AsString(128).Nullable()
					.WithColumn("isreconciled").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("isapproved").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("version").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("supersedesfactid").AsString(36).Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsString(128).Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsString(128).Nullable()
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_workforceannualpayfacts_employment ON workforceannualpayfacts (workforceemploymentid, reportingyear);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_workforceannualpayfacts_department ON workforceannualpayfacts (departmentid, reportingyear, reporttype);");
			}
		}

		public override void Down()
		{
			if (Schema.Table("workforceannualpayfacts").Exists()) Delete.Table("workforceannualpayfacts");
			if (Schema.Table("workforceworkentries").Exists()) Delete.Table("workforceworkentries");
			if (Schema.Table("employeecostcomponents").Exists()) Delete.Table("employeecostcomponents");
			if (Schema.Table("employeepaycomponents").Exists()) Delete.Table("employeepaycomponents");
			if (Schema.Table("employeecompensationprofiles").Exists()) Delete.Table("employeecompensationprofiles");
		}
	}
}
