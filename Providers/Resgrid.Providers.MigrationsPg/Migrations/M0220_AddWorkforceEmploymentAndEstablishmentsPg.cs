using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0220 (Workforce &amp; Business Operations plan, Phase E). Same number, lower-case identifiers, guarded for safe retry.
	/// </summary>
	[Migration(220)]
	public class M0220_AddWorkforceEmploymentAndEstablishmentsPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("workforceemployerprofiles").Exists())
			{
				Create.Table("workforceemployerprofiles")
					.WithColumn("workforceemployerprofileid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("legalname").AsString(250).Nullable()
					.WithColumn("fein").AsCustom("text").Nullable()
					.WithColumn("sein").AsCustom("text").Nullable()
					.WithColumn("sosnumber").AsCustom("text").Nullable()
					.WithColumn("naics").AsString(6).Nullable()
					.WithColumn("eddaddress").AsCustom("text").Nullable()
					.WithColumn("headquartersaddress").AsCustom("text").Nullable()
					.WithColumn("isintegratedenterprise").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("filingcontactname").AsCustom("text").Nullable()
					.WithColumn("filingcontactemail").AsCustom("text").Nullable()
					.WithColumn("filingcontactphone").AsCustom("text").Nullable()
					.WithColumn("coveragestatus").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("usemployeecount").AsInt32().Nullable()
					.WithColumn("californiaemployeecount").AsInt32().Nullable()
					.WithColumn("effectiveon").AsDateTime2().Nullable()
					.WithColumn("expireson").AsDateTime2().Nullable()
					.WithColumn("isactive").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsString(128).Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsString(128).Nullable()
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_workforceemployerprofiles_department ON workforceemployerprofiles (departmentid, isdeleted);");
			}
			if (!Schema.Table("workforceaffiliatedentities").Exists())
			{
				Create.Table("workforceaffiliatedentities")
					.WithColumn("workforceaffiliatedentityid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("workforceemployerprofileid").AsString(36).Nullable()
					.WithColumn("legalname").AsString(250).Nullable()
					.WithColumn("fein").AsCustom("text").Nullable()
					.WithColumn("sein").AsCustom("text").Nullable()
					.WithColumn("sosnumber").AsCustom("text").Nullable()
					.WithColumn("headquartersaddress").AsCustom("text").Nullable()
					.WithColumn("effectiveon").AsDateTime2().Nullable()
					.WithColumn("expireson").AsDateTime2().Nullable()
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsString(128).Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsString(128).Nullable()
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_workforceaffiliatedentities_department ON workforceaffiliatedentities (departmentid, isdeleted);");
			}
			if (!Schema.Table("workforceestablishments").Exists())
			{
				Create.Table("workforceestablishments")
					.WithColumn("workforceestablishmentid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("workforceaffiliatedentityid").AsString(36).Nullable()
					.WithColumn("code").AsString(50).Nullable()
					.WithColumn("name").AsString(250).Nullable()
					.WithColumn("physicaladdress").AsCustom("text").Nullable()
					.WithColumn("city").AsString(100).Nullable()
					.WithColumn("statecode").AsString(2).Nullable()
					.WithColumn("postalcode").AsString(10).Nullable()
					.WithColumn("naics").AsString(6).Nullable()
					.WithColumn("majoractivity").AsString(250).Nullable()
					.WithColumn("isheadquarters").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("wasfiledprioryear").AsBoolean().Nullable()
					.WithColumn("activefrom").AsDateTime2().Nullable()
					.WithColumn("activeto").AsDateTime2().Nullable()
					.WithColumn("timezoneid").AsString(100).Nullable()
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsString(128).Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsString(128).Nullable()
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_workforceestablishments_department ON workforceestablishments (departmentid, isdeleted);");
				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_workforceestablishments_code ON workforceestablishments (departmentid, code) WHERE isdeleted = FALSE;");
			}
			if (!Schema.Table("workforcelaborcontractors").Exists())
			{
				Create.Table("workforcelaborcontractors")
					.WithColumn("workforcelaborcontractorid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("legalname").AsString(250).Nullable()
					.WithColumn("ownershipname").AsString(250).Nullable()
					.WithColumn("dba").AsString(250).Nullable()
					.WithColumn("fein").AsCustom("text").Nullable()
					.WithColumn("identifiertype").AsString(20).Nullable()
					.WithColumn("contactdetails").AsCustom("text").Nullable()
					.WithColumn("relationshipstarton").AsDateTime2().Nullable()
					.WithColumn("relationshipendon").AsDateTime2().Nullable()
					.WithColumn("provenance").AsString(500).Nullable()
					.WithColumn("isactive").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsString(128).Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsString(128).Nullable()
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_workforcelaborcontractors_department ON workforcelaborcontractors (departmentid, isdeleted);");
			}
			if (!Schema.Table("workforceworkers").Exists())
			{
				Create.Table("workforceworkers")
					.WithColumn("workforceworkerid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("userid").AsString(128).Nullable()
					.WithColumn("externalworkerkey").AsCustom("text").Nullable()
					.WithColumn("displaylabel").AsCustom("text").Nullable()
					.WithColumn("isactive").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsString(128).Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsString(128).Nullable()
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_workforceworkers_department ON workforceworkers (departmentid, isdeleted);");
				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_workforceworkers_user ON workforceworkers (departmentid, userid) WHERE userid is not null and isdeleted = FALSE;");
			}
			if (!Schema.Table("workforceemployments").Exists())
			{
				Create.Table("workforceemployments")
					.WithColumn("workforceemploymentid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("workforceworkerid").AsString(36).Nullable()
					.WithColumn("workforceaffiliatedentityid").AsString(36).Nullable()
					.WithColumn("workforcelaborcontractorid").AsString(36).Nullable()
					.WithColumn("workerkind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("starton").AsDateTime2().NotNullable()
					.WithColumn("endon").AsDateTime2().Nullable()
					.WithColumn("employmenttype").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("exemptionstatus").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("defaultestablishmentid").AsString(36).Nullable()
					.WithColumn("californiaemployeebasis").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("personnelroleid").AsInt32().Nullable()
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsString(128).Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsString(128).Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_workforceemployments_worker ON workforceemployments (workforceworkerid, starton);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_workforceemployments_department ON workforceemployments (departmentid, isdeleted);");
			}
			if (!Schema.Table("workforcejobassignments").Exists())
			{
				Create.Table("workforcejobassignments")
					.WithColumn("workforcejobassignmentid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("workforceemploymentid").AsString(36).Nullable()
					.WithColumn("effectiveon").AsDateTime2().NotNullable()
					.WithColumn("expireson").AsDateTime2().Nullable()
					.WithColumn("workforceestablishmentid").AsString(36).Nullable()
					.WithColumn("jobtitle").AsString(250).Nullable()
					.WithColumn("soccode").AsString(20).Nullable()
					.WithColumn("socversion").AsString(20).Nullable()
					.WithColumn("capaydataprofilecode").AsString(50).Nullable()
					.WithColumn("jobcategorycode").AsString(10).Nullable()
					.WithColumn("caloesmarsauthorityprofilecode").AsString(50).Nullable()
					.WithColumn("caloesmarsclassificationcode").AsString(100).Nullable()
					.WithColumn("mappingprovenance").AsString(500).Nullable()
					.WithColumn("workmode").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("workcountry").AsString(2).Nullable()
					.WithColumn("worksubdivision").AsString(10).Nullable()
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsString(128).Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsString(128).Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_workforcejobassignments_employment ON workforcejobassignments (workforceemploymentid, effectiveon);");
			}
		}

		public override void Down()
		{
			if (Schema.Table("workforcejobassignments").Exists()) Delete.Table("workforcejobassignments");
			if (Schema.Table("workforceemployments").Exists()) Delete.Table("workforceemployments");
			if (Schema.Table("workforceworkers").Exists()) Delete.Table("workforceworkers");
			if (Schema.Table("workforcelaborcontractors").Exists()) Delete.Table("workforcelaborcontractors");
			if (Schema.Table("workforceestablishments").Exists()) Delete.Table("workforceestablishments");
			if (Schema.Table("workforceaffiliatedentities").Exists()) Delete.Table("workforceaffiliatedentities");
			if (Schema.Table("workforceemployerprofiles").Exists()) Delete.Table("workforceemployerprofiles");
		}
	}
}
