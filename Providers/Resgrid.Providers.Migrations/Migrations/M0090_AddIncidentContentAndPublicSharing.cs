using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>Incident status notes, files, and token-scoped public information sharing.</summary>
	[Migration(90)]
	public class M0090_AddIncidentContentAndPublicSharing : Migration
	{
		public override void Up()
		{
			if (Schema.Table("IncidentCommands").Exists())
			{
				if (!Schema.Table("IncidentCommands").Column("PublicShareEnabled").Exists())
					Alter.Table("IncidentCommands").AddColumn("PublicShareEnabled").AsBoolean().NotNullable().WithDefaultValue(false);
				if (!Schema.Table("IncidentCommands").Column("PublicShareToken").Exists())
					Alter.Table("IncidentCommands").AddColumn("PublicShareToken").AsString(64).Nullable();
				// SQL Server permits only one NULL in a plain unique index. Keep this lookup indexed; the 256-bit random
				// token plus rotation makes collisions impractical without blocking multiple legacy NULL rows.
				if (!Schema.Table("IncidentCommands").Index("IX_IncidentCommands_PublicShareToken").Exists())
					Create.Index("IX_IncidentCommands_PublicShareToken").OnTable("IncidentCommands").OnColumn("PublicShareToken").Ascending();
			}

			if (!Schema.Table("IncidentNotes").Exists())
			{
				Create.Table("IncidentNotes")
					.WithColumn("IncidentNoteId").AsString(128).NotNullable().PrimaryKey()
					.WithColumn("IncidentCommandId").AsString(128).NotNullable()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("CallId").AsInt32().NotNullable()
					.WithColumn("NoteType").AsInt32().NotNullable()
					.WithColumn("Visibility").AsInt32().NotNullable()
					.WithColumn("Title").AsString(250).Nullable()
					.WithColumn("Body").AsString(int.MaxValue).NotNullable()
					.WithColumn("ContainmentPercent").AsDecimal(5, 2).Nullable()
					.WithColumn("CreatedByUserId").AsString(128).NotNullable()
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("DeletedOn").AsDateTime2().Nullable()
					.WithColumn("DeletedByUserId").AsString(128).Nullable()
					.WithColumn("ModifiedOn").AsDateTime2().Nullable();

				Create.ForeignKey("FK_IncidentNotes_IncidentCommands")
					.FromTable("IncidentNotes").ForeignColumn("IncidentCommandId")
					.ToTable("IncidentCommands").PrimaryColumn("IncidentCommandId");
				Create.Index("IX_IncidentNotes_Department_Call_Modified")
					.OnTable("IncidentNotes")
					.OnColumn("DepartmentId").Ascending()
					.OnColumn("CallId").Ascending()
					.OnColumn("ModifiedOn").Ascending();
			}

			if (!Schema.Table("IncidentAttachments").Exists())
			{
				Create.Table("IncidentAttachments")
					.WithColumn("IncidentAttachmentId").AsString(128).NotNullable().PrimaryKey()
					.WithColumn("IncidentCommandId").AsString(128).NotNullable()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("CallId").AsInt32().NotNullable()
					.WithColumn("Visibility").AsInt32().NotNullable()
					.WithColumn("FileName").AsString(512).NotNullable()
					.WithColumn("ContentType").AsString(200).NotNullable()
					.WithColumn("ContentLength").AsInt64().NotNullable()
					.WithColumn("Sha256Hash").AsString(64).NotNullable()
					.WithColumn("Description").AsString(1000).Nullable()
					.WithColumn("Data").AsBinary(int.MaxValue).NotNullable()
					.WithColumn("UploadedByUserId").AsString(128).NotNullable()
					.WithColumn("UploadedOn").AsDateTime2().NotNullable()
					.WithColumn("DeletedOn").AsDateTime2().Nullable()
					.WithColumn("DeletedByUserId").AsString(128).Nullable()
					.WithColumn("ModifiedOn").AsDateTime2().Nullable();

				Create.ForeignKey("FK_IncidentAttachments_IncidentCommands")
					.FromTable("IncidentAttachments").ForeignColumn("IncidentCommandId")
					.ToTable("IncidentCommands").PrimaryColumn("IncidentCommandId");
				Create.Index("IX_IncidentAttachments_Department_Call_Modified")
					.OnTable("IncidentAttachments")
					.OnColumn("DepartmentId").Ascending()
					.OnColumn("CallId").Ascending()
					.OnColumn("ModifiedOn").Ascending();
			}
		}

		public override void Down()
		{
			if (Schema.Table("IncidentAttachments").Exists()) Delete.Table("IncidentAttachments");
			if (Schema.Table("IncidentNotes").Exists()) Delete.Table("IncidentNotes");
			if (Schema.Table("IncidentCommands").Exists())
			{
				if (Schema.Table("IncidentCommands").Index("IX_IncidentCommands_PublicShareToken").Exists())
					Delete.Index("IX_IncidentCommands_PublicShareToken").OnTable("IncidentCommands");
				if (Schema.Table("IncidentCommands").Column("PublicShareToken").Exists())
					Delete.Column("PublicShareToken").FromTable("IncidentCommands");
				if (Schema.Table("IncidentCommands").Column("PublicShareEnabled").Exists())
					Delete.Column("PublicShareEnabled").FromTable("IncidentCommands");
			}
		}
	}
}
