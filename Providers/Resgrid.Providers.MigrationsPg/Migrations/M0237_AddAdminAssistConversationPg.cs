using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(237)]
	public class M0237_AddAdminAssistConversationPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("departmentaiconfigs").Exists())
				Create.Table("departmentaiconfigs")
					.WithColumn("departmentid").AsInt32().PrimaryKey().NotNullable()
					.WithColumn("disabled").AsBoolean().WithDefaultValue(false).NotNullable()
					.WithColumn("monthlytokenlimit").AsInt32().Nullable()
					.WithColumn("revision").AsInt64().NotNullable();
			if (!Schema.Table("aiadmission").Exists())
				Create.Table("aiadmission")
					.WithColumn("id").AsInt32().PrimaryKey().NotNullable();
			if (!Schema.Table("adminassistconversations").Exists())
				Create.Table("adminassistconversations")
					.WithColumn("id").AsString(36).PrimaryKey().NotNullable()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("userid").AsString(128).NotNullable()
					.WithColumn("revision").AsInt64().NotNullable()
					.WithColumn("createdonutc").AsDateTime().NotNullable()
					.WithColumn("modifiedonutc").AsDateTime().NotNullable()
					.WithColumn("deleted").AsBoolean().WithDefaultValue(false).NotNullable();
			if (!Schema.Table("aiusageledger").Exists())
				Create.Table("aiusageledger")
					.WithColumn("id").AsString(36).PrimaryKey().NotNullable()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("userid").AsString(128).NotNullable()
					.WithColumn("month").AsString(7).NotNullable()
					.WithColumn("reservedtokens").AsInt32().NotNullable()
					.WithColumn("usedtokens").AsInt32().Nullable()
					.WithColumn("expiresonutc").AsDateTime().NotNullable()
					.WithColumn("outcome").AsString(40).Nullable();
			if (!Schema.Table("aigenerations").Exists())
				Create.Table("aigenerations")
					.WithColumn("id").AsString(36).PrimaryKey().NotNullable()
					.WithColumn("conversationid").AsString(36).NotNullable()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("userid").AsString(128).NotNullable()
					.WithColumn("revision").AsInt64().NotNullable()
					.WithColumn("createdonutc").AsDateTime().NotNullable()
					.WithColumn("content").AsString(int.MaxValue).NotNullable()
					.WithColumn("isprotected").AsBoolean().WithDefaultValue(false).NotNullable()
					.WithColumn("protectedcatalogversion").AsInt32().Nullable()
					.WithColumn("promptversion").AsString(64).NotNullable()
					.WithColumn("modelrevision").AsString(64).NotNullable()
					.WithColumn("runtimedigest").AsString(80).NotNullable()
					.WithColumn("requestdigest").AsString(64).NotNullable()
					.WithColumn("inputtokens").AsInt32().NotNullable()
					.WithColumn("outputtokens").AsInt32().NotNullable()
					.WithColumn("outcome").AsString(40).NotNullable();
			if (!Schema.Table("aiusageledger").Index("ix_aiusageledger_scope").Exists())
				Create.Index("ix_aiusageledger_scope").OnTable("aiusageledger").OnColumn("month").Ascending().OnColumn("departmentid").Ascending();
			if (!Schema.Table("aigenerations").Index("ix_aigenerations_scope").Exists())
				Create.Index("ix_aigenerations_scope").OnTable("aigenerations").OnColumn("departmentid").Ascending().OnColumn("userid").Ascending().OnColumn("conversationid").Ascending().OnColumn("revision").Ascending();
			if (!Schema.Table("adminassistconversations").Index("ix_adminassistconversations_scope").Exists())
				Create.Index("ix_adminassistconversations_scope").OnTable("adminassistconversations").OnColumn("departmentid").Ascending().OnColumn("userid").Ascending().OnColumn("modifiedonutc").Ascending();
			Execute.Sql("INSERT INTO aiadmission (id) SELECT 1 WHERE NOT EXISTS (SELECT 1 FROM aiadmission WHERE id=1);");
			Execute.Sql("INSERT INTO featureflags (flagkey,name,description,category,isenabledglobally) SELECT 'Ai.Enhanced','Enhanced AI','Operator rollout; entitlement is checked separately.','AI',false WHERE NOT EXISTS (SELECT 1 FROM featureflags WHERE flagkey='Ai.Enhanced');");
			Execute.Sql("INSERT INTO featureflagprerequisites (featureflagid,requiredfeatureflagid) SELECT f.featureflagid,p.featureflagid FROM featureflags f CROSS JOIN featureflags p WHERE f.flagkey='Ai.AdminAssist' AND p.flagkey='Ai.Enhanced' AND NOT EXISTS (SELECT 1 FROM featureflagprerequisites r WHERE r.featureflagid=f.featureflagid AND r.requiredfeatureflagid=p.featureflagid);");
			Execute.Sql("INSERT INTO featureflagprerequisites (featureflagid,requiredfeatureflagid) SELECT f.featureflagid,p.featureflagid FROM featureflags f CROSS JOIN featureflags p WHERE f.flagkey='Ai.AdminAssist' AND p.flagkey='Admin.Assist' AND NOT EXISTS (SELECT 1 FROM featureflagprerequisites r WHERE r.featureflagid=f.featureflagid AND r.requiredfeatureflagid=p.featureflagid);");
		}
		public override void Down()
		{
			// Protected evidence is removed only through its approved lifecycle.
			throw new System.NotSupportedException("Disable AI rollout; conversation schema rollback requires an approved data migration.");
		}
	}
}
