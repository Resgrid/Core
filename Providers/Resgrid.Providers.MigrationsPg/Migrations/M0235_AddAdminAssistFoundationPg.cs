using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>Registry §4G: deterministic setup, evidence review and trace storage. No model payloads.</summary>
	[Migration(235)]
	public class M0235_AddAdminAssistFoundationPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("adminassistworkspaces").Exists())
				Create.Table("adminassistworkspaces")
					.WithColumn("departmentid").AsInt32().PrimaryKey().NotNullable()
					.WithColumn("revision").AsInt64().NotNullable()
					.WithColumn("mode").AsInt32().NotNullable()
					.WithColumn("areasjson").AsString(int.MaxValue).NotNullable()
					.WithColumn("catalogversion").AsString(64).NotNullable()
					.WithColumn("reviewedon").AsDateTime().Nullable()
					.WithColumn("modifiedon").AsDateTime().NotNullable();
			if (!Schema.Table("adminassistlearning").Exists())
				Create.Table("adminassistlearning")
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("userid").AsString(128).NotNullable()
					.WithColumn("capabilityid").AsString(128).NotNullable()
					.WithColumn("catalogversion").AsString(64).NotNullable()
					.WithColumn("learned").AsBoolean().NotNullable()
					.WithColumn("interested").AsBoolean().NotNullable()
					.WithColumn("modifiedon").AsDateTime().NotNullable();
			if (!Schema.Table("adminassisthistory").Exists())
				Create.Table("adminassisthistory")
					.WithColumn("adminassisthistoryid").AsString(36).PrimaryKey().NotNullable()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("actorid").AsString(128).Nullable()
					.WithColumn("occurredonutc").AsDateTime().NotNullable()
					.WithColumn("source").AsString(64).NotNullable()
					.WithColumn("correlationid").AsString(64).Nullable()
					.WithColumn("action").AsString(64).NotNullable()
					.WithColumn("subjectid").AsString(192).NotNullable()
					.WithColumn("beforecode").AsString(int.MaxValue).Nullable()
					.WithColumn("aftercode").AsString(int.MaxValue).Nullable()
					.WithColumn("revision").AsInt64().NotNullable();
			if (!Schema.Table("adminassistconfigurationrevisions").Exists())
				Create.Table("adminassistconfigurationrevisions")
					.WithColumn("departmentid").AsInt32().PrimaryKey().NotNullable()
					.WithColumn("revision").AsInt64().NotNullable()
					.WithColumn("modifiedon").AsDateTime().NotNullable();
			if (!Schema.Table("adminassistfindings").Exists())
				Create.Table("adminassistfindings")
					.WithColumn("adminassistfindingid").AsString(36).PrimaryKey().NotNullable()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("ruleid").AsString(128).NotNullable()
					.WithColumn("subjectid").AsString(128).NotNullable()
					.WithColumn("episode").AsInt32().NotNullable()
					.WithColumn("result").AsInt32().NotNullable()
					.WithColumn("severity").AsInt32().NotNullable()
					.WithColumn("reviewstatus").AsInt32().NotNullable()
					.WithColumn("ownerid").AsString(128).Nullable()
					.WithColumn("reviewon").AsDateTime().Nullable()
					.WithColumn("exceptionuntil").AsDateTime().Nullable()
					.WithColumn("content").AsString(int.MaxValue).Nullable()
					.WithColumn("isprotected").AsBoolean().WithDefaultValue(false).NotNullable()
					.WithColumn("protectedcatalogversion").AsInt32().WithDefaultValue(0).NotNullable()
					.WithColumn("snapshotrevision").AsString(128).NotNullable()
					.WithColumn("revision").AsInt64().NotNullable()
					.WithColumn("firstobservedon").AsDateTime().NotNullable()
					.WithColumn("lastobservedon").AsDateTime().NotNullable();
			if (!Schema.Table("adminassistdispatchtraces").Exists())
				Create.Table("adminassistdispatchtraces")
					.WithColumn("adminassistdispatchtraceid").AsString(36).PrimaryKey().NotNullable()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("callid").AsInt32().NotNullable()
					.WithColumn("attemptid").AsString(36).NotNullable()
					.WithColumn("stage").AsString(48).NotNullable()
					.WithColumn("resolverversion").AsString(64).NotNullable()
					.WithColumn("occurredon").AsDateTime().NotNullable()
					.WithColumn("content").AsString(int.MaxValue).Nullable()
					.WithColumn("isprotected").AsBoolean().WithDefaultValue(false).NotNullable()
					.WithColumn("protectedcatalogversion").AsInt32().WithDefaultValue(0).NotNullable();
			if (!Schema.Table("adminassistworkerstates").Exists())
				Create.Table("adminassistworkerstates")
					.WithColumn("departmentid").AsInt32().PrimaryKey().NotNullable()
					.WithColumn("lastevaluatedon").AsDateTime().Nullable()
					.WithColumn("lastattempton").AsDateTime().Nullable()
					.WithColumn("lastdigeston").AsDateTime().Nullable()
					.WithColumn("digestcursor").AsString(128).Nullable()
					.WithColumn("leaseowner").AsString(36).Nullable()
					.WithColumn("leaseexpireson").AsDateTime().Nullable();
			if (!Schema.Table("adminassistdailysummaries").Exists())
				Create.Table("adminassistdailysummaries")
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("dayutc").AsDateTime().NotNullable()
					.WithColumn("failedcount").AsInt32().NotNullable()
					.WithColumn("unknowncount").AsInt32().NotNullable()
					.WithColumn("evaluatedcount").AsInt32().NotNullable()
					.WithColumn("catalogversion").AsString(64).NotNullable();
			if (!Schema.Table("adminassistpreferences").Exists())
				Create.Table("adminassistpreferences")
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("userid").AsString(128).NotNullable()
					.WithColumn("digestenabled").AsBoolean().NotNullable()
					.WithColumn("quietstarthour").AsInt32().NotNullable()
					.WithColumn("quietendhour").AsInt32().NotNullable()
					.WithColumn("locale").AsString(16).NotNullable()
					.WithColumn("revision").AsInt64().NotNullable()
					.WithColumn("lastattemptweek").AsString(10).Nullable()
					.WithColumn("lastattempton").AsDateTime().Nullable()
					.WithColumn("lastattemptoutcome").AsString(32).Nullable();
			if (!Schema.Table("adminassistpreferences").Index("ux_adminassistpreferences_scope").Exists())
				Create.Index("ux_adminassistpreferences_scope").OnTable("adminassistpreferences").OnColumn("departmentid").Ascending().OnColumn("userid").Ascending().WithOptions().Unique();
			if (!Schema.Table("adminassistlearning").Index("ux_adminassistlearning_scope").Exists())
				Create.Index("ux_adminassistlearning_scope").OnTable("adminassistlearning").OnColumn("departmentid").Ascending().OnColumn("userid").Ascending().OnColumn("capabilityid").Ascending().OnColumn("catalogversion").Ascending().WithOptions().Unique();
			if (!Schema.Table("adminassisthistory").Index("ix_adminassisthistory_scope").Exists())
				Create.Index("ix_adminassisthistory_scope").OnTable("adminassisthistory").OnColumn("departmentid").Ascending().OnColumn("occurredonutc").Ascending().OnColumn("adminassisthistoryid").Ascending();
			if (!Schema.Table("adminassistfindings").Index("ux_adminassistfindings_scope").Exists())
				Create.Index("ux_adminassistfindings_scope").OnTable("adminassistfindings").OnColumn("departmentid").Ascending().OnColumn("ruleid").Ascending().OnColumn("subjectid").Ascending().WithOptions().Unique();
			if (!Schema.Table("adminassistdispatchtraces").Index("ix_adminassistdispatchtraces_scope").Exists())
				Create.Index("ix_adminassistdispatchtraces_scope").OnTable("adminassistdispatchtraces").OnColumn("departmentid").Ascending().OnColumn("callid").Ascending().OnColumn("occurredon").Ascending();
			if (!Schema.Table("adminassistdailysummaries").Index("ux_adminassistdailysummaries_scope").Exists())
				Create.Index("ux_adminassistdailysummaries_scope").OnTable("adminassistdailysummaries").OnColumn("departmentid").Ascending().OnColumn("dayutc").Ascending().WithOptions().Unique();
			Execute.Sql("INSERT INTO featureflags (flagkey,name,description,category,isenabledglobally) SELECT 'Admin.Setup','Department Setup','Seeded off; deterministic setup is independent of AI.','Administration',false WHERE NOT EXISTS (SELECT 1 FROM featureflags WHERE flagkey='Admin.Setup');");
			Execute.Sql("INSERT INTO featureflags (flagkey,name,description,category,isenabledglobally) SELECT 'Admin.Assist','Admin Assist','Seeded off; deterministic setup is independent of AI.','Administration',false WHERE NOT EXISTS (SELECT 1 FROM featureflags WHERE flagkey='Admin.Assist');");
			Execute.Sql("INSERT INTO featureflags (flagkey,name,description,category,isenabledglobally) SELECT 'Ai.AdminAssist','Admin Assist Conversation','Seeded off; deterministic setup is independent of AI.','Administration',false WHERE NOT EXISTS (SELECT 1 FROM featureflags WHERE flagkey='Ai.AdminAssist');");
		}

		public override void Down()
		{
			// Keep evidence and operator-owned flags on rollback; disable rollout instead of deleting history.
		}
	}
}
