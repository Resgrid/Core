using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Work assignments on Records (RMS plan section 5.2 RmsRecordWorkAssignment, RMS-1D, registry M0179): an
	/// optional person / unit / group / command-role / dispatch-role assignment with purpose, due, acknowledged,
	/// completed and cancelled state, safe source context and the client origin. Guarded for safe retry.
	/// </summary>
	[Migration(179)]
	public class M0179_AddRmsRecordWorkAssignmentsPg : Migration
	{
		public override void Up()
		{
			if (Schema.Table("rmsrecordworkassignments").Exists())
				return;

			Create.Table("rmsrecordworkassignments")
				.WithColumn("rmsrecordworkassignmentid").AsString(36).NotNullable().PrimaryKey()
				.WithColumn("departmentid").AsInt32().NotNullable()
				.WithColumn("protectionid").AsString(36).Nullable()
				.WithColumn("recordid").AsString(36).NotNullable()
				.WithColumn("assigneekind").AsInt32().NotNullable()
				.WithColumn("assigneeuserid").AsString(128).Nullable()
				.WithColumn("assigneeunitid").AsInt32().Nullable()
				.WithColumn("assigneegroupid").AsInt32().Nullable()
				.WithColumn("assigneerole").AsString(100).Nullable()
				.WithColumn("purpose").AsString(32).NotNullable()
				.WithColumn("note").AsString(1000).Nullable()
				.WithColumn("sourcecontextjson").AsString(1000).Nullable()
				.WithColumn("dueon").AsDateTime2().Nullable()
				.WithColumn("state").AsInt32().NotNullable()
				.WithColumn("acknowledgedon").AsDateTime2().Nullable()
				.WithColumn("acknowledgedbyuserid").AsString(128).Nullable()
				.WithColumn("completedon").AsDateTime2().Nullable()
				.WithColumn("completedbyuserid").AsString(128).Nullable()
				.WithColumn("cancelledon").AsDateTime2().Nullable()
				.WithColumn("cancelledbyuserid").AsString(128).Nullable()
				.WithColumn("cancelreason").AsString(500).Nullable()
				.WithColumn("originclient").AsInt32().NotNullable().WithDefaultValue(1)
				.WithColumn("createdon").AsDateTime2().NotNullable()
				.WithColumn("createdbyuserid").AsString(128).Nullable()
				.WithColumn("modifiedon").AsDateTime2().NotNullable()
				.WithColumn("modifiedbyuserid").AsString(128).Nullable()
				.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(1)
				.WithColumn("deletedon").AsDateTime2().Nullable();

			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsrecordworkassignments_record ON rmsrecordworkassignments (departmentid, recordid);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsrecordworkassignments_person ON rmsrecordworkassignments (departmentid, assigneeuserid, state);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsrecordworkassignments_unit ON rmsrecordworkassignments (departmentid, assigneeunitid, state);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsrecordworkassignments_modified ON rmsrecordworkassignments (departmentid, modifiedon);");
		}

		public override void Down()
		{
			if (Schema.Table("rmsrecordworkassignments").Exists())
				Delete.Table("rmsrecordworkassignments");
		}
	}
}
