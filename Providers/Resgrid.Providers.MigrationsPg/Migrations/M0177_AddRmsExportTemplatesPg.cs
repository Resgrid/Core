using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Department-authored report exports (RMS plan section 5.6): the export template a department designs and
	/// the stored runs a Workflow step or the schedule sweep (worker 45) renders from it.
	/// </summary>
	[Migration(177)]
	public class M0177_AddRmsExportTemplatesPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("rmsexporttemplates").Exists())
			{
				Create.Table("rmsexporttemplates")
					.WithColumn("rmsexporttemplateid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("protectionid").AsString(36).Nullable()
					.WithColumn("templatekey").AsString(64).NotNullable()
					.WithColumn("name").AsString(200).NotNullable()
					.WithColumn("description").AsString(1000).Nullable()
					.WithColumn("format").AsInt32().NotNullable()
					.WithColumn("scope").AsInt32().NotNullable()
					.WithColumn("definitionkeyscsv").AsString(1000).Nullable()
					.WithColumn("columnsjson").AsCustom("text").NotNullable()
					.WithColumn("includenarrative").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("includerestricted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("egressacknowledgedon").AsDateTime2().Nullable()
					.WithColumn("egressacknowledgedbyuserid").AsString(128).Nullable()
					.WithColumn("filenametemplate").AsString(200).Nullable()
					.WithColumn("includeheader").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("delimiter").AsString(4).Nullable()
					.WithColumn("schedulekind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("schedulehourlocal").AsInt32().NotNullable().WithDefaultValue(6)
					.WithColumn("scheduledayofweek").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("scheduledayofmonth").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("windowdays").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("nextrunon").AsDateTime2().Nullable()
					.WithColumn("lastrunon").AsDateTime2().Nullable()
					.WithColumn("isenabled").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("createdbyuserid").AsString(128).Nullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable()
					.WithColumn("modifiedbyuserid").AsString(128).Nullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(1)
					.WithColumn("deletedon").AsDateTime2().Nullable();
				Create.Index("ux_rmsexporttemplates_key").OnTable("rmsexporttemplates")
					.OnColumn("departmentid").Ascending().OnColumn("templatekey").Ascending().WithOptions().Unique();
				Create.Index("ix_rmsexporttemplates_due").OnTable("rmsexporttemplates")
					.OnColumn("isenabled").Ascending().OnColumn("nextrunon").Ascending();
			}

			if (!Schema.Table("rmsexportruns").Exists())
			{
				Create.Table("rmsexportruns")
					.WithColumn("rmsexportrunid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("protectionid").AsString(36).Nullable()
					.WithColumn("templateid").AsString(36).NotNullable()
					.WithColumn("templatekey").AsString(64).NotNullable()
					.WithColumn("trigger").AsInt32().NotNullable()
					.WithColumn("recordid").AsString(36).Nullable()
					.WithColumn("windowstart").AsDateTime2().Nullable()
					.WithColumn("windowend").AsDateTime2().Nullable()
					.WithColumn("recordcount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("filename").AsString(260).NotNullable()
					.WithColumn("contenttype").AsString(100).NotNullable()
					.WithColumn("bytesize").AsInt64().NotNullable().WithDefaultValue(0)
					.WithColumn("checksum").AsString(80).NotNullable()
					.WithColumn("data").AsCustom("bytea").Nullable()
					.WithColumn("redacted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("redactedfieldsjson").AsCustom("text").Nullable()
					.WithColumn("generatedon").AsDateTime2().NotNullable()
					.WithColumn("generatedbyuserid").AsString(128).Nullable()
					.WithColumn("workflowrunid").AsString(36).Nullable()
					.WithColumn("expireson").AsDateTime2().NotNullable()
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("deletedon").AsDateTime2().Nullable();
				Create.Index("ix_rmsexportruns_template").OnTable("rmsexportruns")
					.OnColumn("departmentid").Ascending().OnColumn("templateid").Ascending().OnColumn("generatedon").Descending();
				Create.Index("ix_rmsexportruns_expires").OnTable("rmsexportruns")
					.OnColumn("departmentid").Ascending().OnColumn("expireson").Ascending();
			}
		}

		public override void Down()
		{
			if (Schema.Table("rmsexportruns").Exists())
				Delete.Table("rmsexportruns");
			if (Schema.Table("rmsexporttemplates").Exists())
				Delete.Table("rmsexporttemplates");
		}
	}
}
