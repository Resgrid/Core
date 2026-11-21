using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>PostgreSQL twin of M0325_AddIntakeConversations (intake text conversations + dedicated-number release warnings).</summary>
	[Migration(325)]
	public class M0325_AddIntakeConversationsPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("intakeconversations").Exists())
			{
				Create.Table("intakeconversations")
					.WithColumn("intakeconversationid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("intakechannelid").AsString(36).Nullable()
					.WithColumn("callid").AsInt32().Nullable()
					.WithColumn("channeltype").AsInt32().NotNullable()
					.WithColumn("participantaddress").AsCustom("citext").NotNullable()
					.WithColumn("participantname").AsCustom("citext").Nullable()
					.WithColumn("inboundaddress").AsCustom("citext").Nullable()
					.WithColumn("replyfromaddress").AsCustom("citext").Nullable()
					.WithColumn("state").AsInt32().NotNullable()
					.WithColumn("handler").AsInt32().NotNullable()
					.WithColumn("assigneduserid").AsCustom("citext").Nullable()
					.WithColumn("assignedon").AsDateTime2().Nullable()
					.WithColumn("assistantturns").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("optedout").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("deskitemid").AsString(36).Nullable()
					.WithColumn("startedon").AsDateTime2().NotNullable()
					.WithColumn("lastinboundon").AsDateTime2().Nullable()
					.WithColumn("lastactivityon").AsDateTime2().NotNullable()
					.WithColumn("closedon").AsDateTime2().Nullable()
					.WithColumn("closereason").AsCustom("citext").Nullable();
			}

			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_intakeconversations_participant ON intakeconversations (channeltype, participantaddress, state, lastactivityon);");
			Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_intakeconversations_open ON intakeconversations (departmentid, channeltype, participantaddress) WHERE state = 1;");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_intakeconversations_call ON intakeconversations (callid) WHERE callid IS NOT NULL;");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_intakeconversations_activity ON intakeconversations (state, lastactivityon);");

			if (!Schema.Table("intakeconversationmessages").Exists())
			{
				Create.Table("intakeconversationmessages")
					.WithColumn("intakeconversationmessageid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("intakeconversationid").AsString(36).NotNullable()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("direction").AsInt32().NotNullable()
					.WithColumn("author").AsInt32().NotNullable()
					.WithColumn("authoruserid").AsCustom("citext").Nullable()
					.WithColumn("callnoteid").AsInt32().Nullable()
					.WithColumn("providermessageid").AsCustom("citext").Nullable()
					.WithColumn("status").AsInt32().NotNullable()
					.WithColumn("error").AsCustom("citext").Nullable()
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("senton").AsDateTime2().Nullable();
			}

			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_intakeconversationmessages_conversation ON intakeconversationmessages (intakeconversationid, createdon);");
			Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_intakeconversationmessages_provider ON intakeconversationmessages (intakeconversationid, providermessageid) WHERE providermessageid IS NOT NULL AND direction = 1;");

			Execute.Sql("ALTER TABLE departmentphonenumbers ADD COLUMN IF NOT EXISTS releasewarningssent integer NOT NULL DEFAULT 0;");
			Execute.Sql("ALTER TABLE departmentphonenumbers ADD COLUMN IF NOT EXISTS lastreleasewarningon timestamp without time zone NULL;");
		}

		public override void Down()
		{
			Execute.Sql("DROP TABLE IF EXISTS intakeconversationmessages;");
			Execute.Sql("DROP TABLE IF EXISTS intakeconversations;");
			Execute.Sql("ALTER TABLE departmentphonenumbers DROP COLUMN IF EXISTS lastreleasewarningon;");
			Execute.Sql("ALTER TABLE departmentphonenumbers DROP COLUMN IF EXISTS releasewarningssent;");
		}
	}
}
