using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Records (RMS-1B) department definitions (plan sections 4.1 and 5.2, registry M0158): stable definition identity, immutable-once-published versions (authored SchemaJson, checksum, capability floor, numbering/lifecycle/retention policy) and the section/field rows materialized at publish for stable query identity.
	/// PostgreSQL twin of the SQL Server migration; lower-case identifiers, citext keys, existence-guarded.
	/// </summary>
	[Migration(158)]
	public class M0158_AddRmsRecordDefinitionsPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("rmsrecorddefinitions").Exists())
			{
				Create.Table("rmsrecorddefinitions")
					.WithColumn("rmsrecorddefinitionid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("protectionid").AsCustom("citext").NotNullable()
					.WithColumn("definitionkey").AsCustom("citext").NotNullable()
					.WithColumn("owner").AsInt32().NotNullable()
					.WithColumn("name").AsCustom("citext").NotNullable()
					.WithColumn("category").AsCustom("citext").Nullable()
					.WithColumn("description").AsCustom("text").Nullable()
					.WithColumn("templatekey").AsCustom("citext").Nullable()
					.WithColumn("templatepackversion").AsInt32().Nullable()
					.WithColumn("jurisdictionprofilekey").AsCustom("citext").Nullable()
					.WithColumn("permittedsubjecttypes").AsCustom("citext").Nullable()
					.WithColumn("currentpublishedversion").AsInt32().Nullable()
					.WithColumn("latestversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("isretired").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("retiredon").AsDateTime2().Nullable()
					.WithColumn("retiredbyuserid").AsCustom("citext").Nullable()
					.WithColumn("retiredreason").AsCustom("text").Nullable()
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("createdbyuserid").AsCustom("citext").Nullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable()
					.WithColumn("modifiedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(1L)
					.WithColumn("deletedon").AsDateTime2().Nullable();
				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_rmsrecorddefinitions_department_key ON rmsrecorddefinitions (departmentid, definitionkey);");
			}

			if (!Schema.Table("rmsrecorddefinitionversions").Exists())
			{
				Create.Table("rmsrecorddefinitionversions")
					.WithColumn("rmsrecorddefinitionversionid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("protectionid").AsCustom("citext").NotNullable()
					.WithColumn("rmsrecorddefinitionid").AsCustom("citext").NotNullable()
					.WithColumn("definitionkey").AsCustom("citext").NotNullable()
					.WithColumn("version").AsInt32().NotNullable()
					.WithColumn("state").AsInt32().NotNullable()
					.WithColumn("lifecyclepreset").AsInt32().NotNullable()
					.WithColumn("reviewerroleids").AsCustom("citext").Nullable()
					.WithColumn("approverroleids").AsCustom("citext").Nullable()
					.WithColumn("reviewduehours").AsInt32().Nullable()
					.WithColumn("approveduehours").AsInt32().Nullable()
					.WithColumn("requireauthorattestation").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("numberingjson").AsCustom("text").Nullable()
					.WithColumn("retentionyears").AsInt32().Nullable()
					.WithColumn("classification").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("schemajson").AsCustom("text").Nullable()
					.WithColumn("schemachecksum").AsCustom("citext").Nullable()
					.WithColumn("minimumclientcapability").AsCustom("citext").Nullable()
					.WithColumn("clientsurfacejson").AsCustom("text").Nullable()
					.WithColumn("migrationmapjson").AsCustom("text").Nullable()
					.WithColumn("changenotes").AsCustom("text").Nullable()
					.WithColumn("publishedon").AsDateTime2().Nullable()
					.WithColumn("publishedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("retiredon").AsDateTime2().Nullable()
					.WithColumn("retiredbyuserid").AsCustom("citext").Nullable()
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("createdbyuserid").AsCustom("citext").Nullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable()
					.WithColumn("modifiedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(1L);
				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_rmsrecorddefinitionversions_department_key_version ON rmsrecorddefinitionversions (departmentid, definitionkey, version);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsrecorddefinitionversions_department_state ON rmsrecorddefinitionversions (departmentid, state);");
			}

			if (!Schema.Table("rmsrecordsectiondefinitions").Exists())
			{
				Create.Table("rmsrecordsectiondefinitions")
					.WithColumn("rmsrecordsectiondefinitionid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("protectionid").AsCustom("citext").NotNullable()
					.WithColumn("rmsrecorddefinitionversionid").AsCustom("citext").NotNullable()
					.WithColumn("definitionkey").AsCustom("citext").NotNullable()
					.WithColumn("definitionversion").AsInt32().NotNullable()
					.WithColumn("sectionkey").AsCustom("citext").NotNullable()
					.WithColumn("label").AsCustom("citext").Nullable()
					.WithColumn("help").AsCustom("text").Nullable()
					.WithColumn("ordinal").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("isrepeating").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("minrows").AsInt32().Nullable()
					.WithColumn("maxrows").AsInt32().Nullable()
					.WithColumn("rulesjson").AsCustom("text").Nullable()
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(1L);
				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_rmsrecordsectiondefinitions_version_key ON rmsrecordsectiondefinitions (departmentid, rmsrecorddefinitionversionid, sectionkey);");
			}

			if (!Schema.Table("rmsrecordfielddefinitions").Exists())
			{
				Create.Table("rmsrecordfielddefinitions")
					.WithColumn("rmsrecordfielddefinitionid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("protectionid").AsCustom("citext").NotNullable()
					.WithColumn("rmsrecorddefinitionversionid").AsCustom("citext").NotNullable()
					.WithColumn("definitionkey").AsCustom("citext").NotNullable()
					.WithColumn("definitionversion").AsInt32().NotNullable()
					.WithColumn("sectionkey").AsCustom("citext").NotNullable()
					.WithColumn("fieldkey").AsCustom("citext").NotNullable()
					.WithColumn("label").AsCustom("citext").Nullable()
					.WithColumn("datatype").AsInt32().NotNullable()
					.WithColumn("ordinal").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("required").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("requiredtofinalize").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("classification").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("referencetype").AsCustom("citext").Nullable()
					.WithColumn("searchable").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("filterable").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("sortable").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("groupable").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("aggregatable").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("workflowexposed").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("exportable").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("constraintsjson").AsCustom("text").Nullable()
					.WithColumn("rulesjson").AsCustom("text").Nullable()
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(1L);
				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_rmsrecordfielddefinitions_version_key ON rmsrecordfielddefinitions (departmentid, rmsrecorddefinitionversionid, fieldkey);");
			}

		}

		public override void Down()
		{
			if (Schema.Table("rmsrecordfielddefinitions").Exists())
				Delete.Table("rmsrecordfielddefinitions");
			if (Schema.Table("rmsrecordsectiondefinitions").Exists())
				Delete.Table("rmsrecordsectiondefinitions");
			if (Schema.Table("rmsrecorddefinitionversions").Exists())
				Delete.Table("rmsrecorddefinitionversions");
			if (Schema.Table("rmsrecorddefinitions").Exists())
				Delete.Table("rmsrecorddefinitions");
		}
	}
}
