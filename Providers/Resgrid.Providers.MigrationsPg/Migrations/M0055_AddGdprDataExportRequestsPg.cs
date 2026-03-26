using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(55)]
	public class M0055_AddGdprDataExportRequestsPg : Migration
	{
		public override void Up()
		{
			Create.Table("gdprdataexportrequests")
				.WithColumn("gdprdataexportrequestid").AsCustom("citext").NotNullable().PrimaryKey()
				.WithColumn("userid").AsCustom("citext").Nullable()
				.WithColumn("departmentid").AsInt32().NotNullable()
				.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("requestedon").AsDateTime().NotNullable()
				.WithColumn("processingstartedon").AsDateTime().Nullable()
				.WithColumn("completedon").AsDateTime().Nullable()
				.WithColumn("downloadtoken").AsCustom("citext").Nullable()
				.WithColumn("tokenexpiresat").AsDateTime().Nullable()
				.WithColumn("exportdata").AsBinary(int.MaxValue).Nullable()
				.WithColumn("filesizebytes").AsInt64().Nullable()
				.WithColumn("errormessage").AsCustom("text").Nullable();
		}

		public override void Down()
		{
			Delete.Table("gdprdataexportrequests");
		}
	}
}
