using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(55)]
	public class M0055_AddGdprDataExportRequests : Migration
	{
		public override void Up()
		{
			Create.Table("GdprDataExportRequests")
				.WithColumn("GdprDataExportRequestId").AsString(128).NotNullable().PrimaryKey()
				.WithColumn("UserId").AsString(128).Nullable()
				.WithColumn("DepartmentId").AsInt32().NotNullable()
				.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("RequestedOn").AsDateTime().NotNullable()
				.WithColumn("ProcessingStartedOn").AsDateTime().Nullable()
				.WithColumn("CompletedOn").AsDateTime().Nullable()
				.WithColumn("DownloadToken").AsString(100).Nullable()
				.WithColumn("TokenExpiresAt").AsDateTime().Nullable()
				.WithColumn("ExportData").AsBinary(int.MaxValue).Nullable()
				.WithColumn("FileSizeBytes").AsInt64().Nullable()
				.WithColumn("ErrorMessage").AsString(2000).Nullable();
		}

		public override void Down()
		{
			Delete.Table("GdprDataExportRequests");
		}
	}
}
