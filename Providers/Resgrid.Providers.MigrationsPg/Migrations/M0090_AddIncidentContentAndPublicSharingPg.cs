using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>PostgreSQL incident status notes, files, and token-scoped public information sharing.</summary>
	[Migration(90)]
	public class M0090_AddIncidentContentAndPublicSharingPg : Migration
	{
		public override void Up()
		{
			if (Schema.Table("incidentcommands").Exists())
			{
				if (!Schema.Table("incidentcommands").Column("publicshareenabled").Exists())
					Alter.Table("incidentcommands").AddColumn("publicshareenabled").AsBoolean().NotNullable().WithDefaultValue(false);
				if (!Schema.Table("incidentcommands").Column("publicsharetoken").Exists())
					Alter.Table("incidentcommands").AddColumn("publicsharetoken").AsCustom("citext").Nullable();
				if (!Schema.Table("incidentcommands").Index("ux_incidentcommands_publicsharetoken").Exists())
					Create.Index("ux_incidentcommands_publicsharetoken").OnTable("incidentcommands").OnColumn("publicsharetoken").Ascending().WithOptions().Unique();
			}

			if (!Schema.Table("incidentnotes").Exists())
			{
				Create.Table("incidentnotes")
					.WithColumn("incidentnoteid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("incidentcommandid").AsCustom("citext").NotNullable()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("callid").AsInt32().NotNullable()
					.WithColumn("notetype").AsInt32().NotNullable()
					.WithColumn("visibility").AsInt32().NotNullable()
					.WithColumn("title").AsCustom("citext").Nullable()
					.WithColumn("body").AsCustom("citext").NotNullable()
					.WithColumn("containmentpercent").AsDecimal(5, 2).Nullable()
					.WithColumn("createdbyuserid").AsCustom("citext").NotNullable()
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("deletedon").AsDateTime2().Nullable()
					.WithColumn("deletedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("modifiedon").AsDateTime2().Nullable();

				Create.ForeignKey("fk_incidentnotes_incidentcommands")
					.FromTable("incidentnotes").ForeignColumn("incidentcommandid")
					.ToTable("incidentcommands").PrimaryColumn("incidentcommandid");
				Create.Index("ix_incidentnotes_department_call_modified")
					.OnTable("incidentnotes")
					.OnColumn("departmentid").Ascending()
					.OnColumn("callid").Ascending()
					.OnColumn("modifiedon").Ascending();
			}

			if (!Schema.Table("incidentattachments").Exists())
			{
				Create.Table("incidentattachments")
					.WithColumn("incidentattachmentid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("incidentcommandid").AsCustom("citext").NotNullable()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("callid").AsInt32().NotNullable()
					.WithColumn("visibility").AsInt32().NotNullable()
					.WithColumn("filename").AsCustom("citext").NotNullable()
					.WithColumn("contenttype").AsCustom("citext").NotNullable()
					.WithColumn("contentlength").AsInt64().NotNullable()
					.WithColumn("sha256hash").AsCustom("citext").NotNullable()
					.WithColumn("description").AsCustom("citext").Nullable()
					.WithColumn("data").AsBinary(int.MaxValue).NotNullable()
					.WithColumn("uploadedbyuserid").AsCustom("citext").NotNullable()
					.WithColumn("uploadedon").AsDateTime2().NotNullable()
					.WithColumn("deletedon").AsDateTime2().Nullable()
					.WithColumn("deletedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("modifiedon").AsDateTime2().Nullable();

				Create.ForeignKey("fk_incidentattachments_incidentcommands")
					.FromTable("incidentattachments").ForeignColumn("incidentcommandid")
					.ToTable("incidentcommands").PrimaryColumn("incidentcommandid");
				Create.Index("ix_incidentattachments_department_call_modified")
					.OnTable("incidentattachments")
					.OnColumn("departmentid").Ascending()
					.OnColumn("callid").Ascending()
					.OnColumn("modifiedon").Ascending();
			}
		}

		public override void Down()
		{
			if (Schema.Table("incidentattachments").Exists()) Delete.Table("incidentattachments");
			if (Schema.Table("incidentnotes").Exists()) Delete.Table("incidentnotes");
			if (Schema.Table("incidentcommands").Exists())
			{
				if (Schema.Table("incidentcommands").Index("ux_incidentcommands_publicsharetoken").Exists())
					Delete.Index("ux_incidentcommands_publicsharetoken").OnTable("incidentcommands");
				if (Schema.Table("incidentcommands").Column("publicsharetoken").Exists())
					Delete.Column("publicsharetoken").FromTable("incidentcommands");
				if (Schema.Table("incidentcommands").Column("publicshareenabled").Exists())
					Delete.Column("publicshareenabled").FromTable("incidentcommands");
			}
		}
	}
}
