using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0391_AddDispatchCaseReviews: dispatchcasereviews (member ids and the determinant citext, compared like SQL Server's
	/// case-insensitive collation; JSON as text) with the unique (departmentid, callprotocolid) index and the queue, dispatcher and call-date
	/// indexes. Existence-guarded.
	/// </summary>
	[Migration(391)]
	public class M0391_AddDispatchCaseReviewsPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("dispatchcasereviews").Exists())
			{
				Create.Table("dispatchcasereviews")
					.WithColumn("dispatchcasereviewid").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("callid").AsInt32().NotNullable()
					.WithColumn("callprotocolid").AsInt32().NotNullable()
					.WithColumn("dispatchprotocolid").AsInt32().NotNullable()
					.WithColumn("protocolversionid").AsInt32().Nullable()
					.WithColumn("determinant").AsCustom("citext").Nullable()
					.WithColumn("overridden").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("dispatcheruserid").AsCustom("citext").NotNullable()
					.WithColumn("callloggedon").AsDateTime2().NotNullable()
					.WithColumn("callnumber").AsCustom("citext").Nullable()
					.WithColumn("callname").AsString(200).Nullable()
					.WithColumn("protocolcode").AsCustom("citext").Nullable()
					.WithColumn("protocolname").AsString(200).Nullable()
					.WithColumn("versionlabel").AsString(50).Nullable()
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("rmsqualityrubricid").AsCustom("citext").Nullable()
					.WithColumn("rubricname").AsString(200).Nullable()
					.WithColumn("criteriajson").AsCustom("text").Nullable()
					.WithColumn("findingsjson").AsCustom("text").Nullable()
					.WithColumn("itemsjson").AsCustom("text").Nullable()
					.WithColumn("score").AsInt32().Nullable()
					.WithColumn("questioncount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("questionpoints").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("instructioncount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("instructionpoints").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("assignedtouserid").AsCustom("citext").Nullable()
					.WithColumn("sampledbyuserid").AsCustom("citext").Nullable()
					.WithColumn("sampledon").AsDateTime2().NotNullable()
					.WithColumn("revieweruserid").AsCustom("citext").Nullable()
					.WithColumn("reviewercomments").AsString(4000).Nullable()
					.WithColumn("senton").AsDateTime2().Nullable()
					.WithColumn("acknowledgedon").AsDateTime2().Nullable()
					.WithColumn("dispatchercomment").AsString(2000).Nullable()
					.WithColumn("dispatchercommenton").AsDateTime2().Nullable()
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(0);
			}

			Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_dispatchcasereviews_callprotocol ON dispatchcasereviews (departmentid, callprotocolid);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_dispatchcasereviews_status ON dispatchcasereviews (departmentid, status);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_dispatchcasereviews_dispatcher ON dispatchcasereviews (departmentid, dispatcheruserid, status);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_dispatchcasereviews_callloggedon ON dispatchcasereviews (departmentid, callloggedon);");
		}

		public override void Down()
		{
			Execute.Sql("DROP TABLE IF EXISTS dispatchcasereviews;");
		}
	}
}
