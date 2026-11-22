using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>Dispatcher desk tables; see the SQL Server M0324_AddDispatcherDesk.</summary>
	[Migration(324)]
	public class M0324_AddDispatcherDeskPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("shiftdispatchsettings").Exists())
			{
				Create.Table("shiftdispatchsettings")
					.WithColumn("shiftdispatchsettingid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("shiftid").AsInt32().NotNullable()
					.WithColumn("isenabled").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("isdispatchonly").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("requireddispatchers").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("minimumondesk").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("allowselfscheduledbreaks").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("breakplanjson").AsCustom("citext").Nullable()
					.WithColumn("updatedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("updatedon").AsDateTime2().NotNullable();
			}

			Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_shiftdispatchsettings_shift ON shiftdispatchsettings (shiftid);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_shiftdispatchsettings_department ON shiftdispatchsettings (departmentid);");

			if (!Schema.Table("shiftdispatchers").Exists())
			{
				Create.Table("shiftdispatchers")
					.WithColumn("shiftdispatcherid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("shiftid").AsInt32().NotNullable()
					.WithColumn("shiftdayid").AsInt32().Nullable()
					.WithColumn("userid").AsCustom("citext").NotNullable()
					.WithColumn("source").AsInt32().NotNullable()
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("issupervisor").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("reviewedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("reviewedon").AsDateTime2().Nullable()
					.WithColumn("note").AsCustom("citext").Nullable();
			}

			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_shiftdispatchers_shift ON shiftdispatchers (shiftid, shiftdayid);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_shiftdispatchers_department_user ON shiftdispatchers (departmentid, userid);");

			if (!Schema.Table("dispatcherbreaks").Exists())
			{
				Create.Table("dispatcherbreaks")
					.WithColumn("dispatcherbreakid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("userid").AsCustom("citext").NotNullable()
					.WithColumn("shiftid").AsInt32().Nullable()
					.WithColumn("shiftdayid").AsInt32().Nullable()
					.WithColumn("breaktype").AsInt32().NotNullable()
					.WithColumn("state").AsInt32().NotNullable()
					.WithColumn("scheduledstarton").AsDateTime2().Nullable()
					.WithColumn("durationminutes").AsInt32().NotNullable()
					.WithColumn("startedon").AsDateTime2().Nullable()
					.WithColumn("expectedreturnon").AsDateTime2().Nullable()
					.WithColumn("endedon").AsDateTime2().Nullable()
					.WithColumn("endreason").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("endedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("note").AsCustom("citext").Nullable()
					.WithColumn("remindersenton").AsDateTime2().Nullable()
					.WithColumn("duealertsenton").AsDateTime2().Nullable()
					.WithColumn("returnalertsenton").AsDateTime2().Nullable()
					.WithColumn("overduealertsenton").AsDateTime2().Nullable()
					.WithColumn("createdbyuserid").AsCustom("citext").Nullable()
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("updatedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("updatedon").AsDateTime2().Nullable();
			}

			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_dispatcherbreaks_department_start ON dispatcherbreaks (departmentid, scheduledstarton);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_dispatcherbreaks_department_user ON dispatcherbreaks (departmentid, userid, state);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_dispatcherbreaks_shiftday ON dispatcherbreaks (shiftdayid) WHERE shiftdayid IS NOT NULL;");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_dispatcherbreaks_open ON dispatcherbreaks (state, scheduledstarton) WHERE state < 2;");

			if (!Schema.Table("dispatcherpresences").Exists())
			{
				Create.Table("dispatcherpresences")
					.WithColumn("dispatcherpresenceid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("userid").AsCustom("citext").NotNullable()
					.WithColumn("state").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("statechangedon").AsDateTime2().NotNullable()
					.WithColumn("signedinon").AsDateTime2().Nullable()
					.WithColumn("lastseenon").AsDateTime2().Nullable()
					.WithColumn("dutymode").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("dutyshiftid").AsInt32().Nullable()
					.WithColumn("dutyshiftdayid").AsInt32().Nullable()
					.WithColumn("dutyendson").AsDateTime2().Nullable()
					.WithColumn("activebreakid").AsCustom("citext").Nullable()
					.WithColumn("callbacknumber").AsCustom("citext").Nullable()
					.WithColumn("devicekind").AsCustom("citext").Nullable()
					.WithColumn("lastofferedon").AsDateTime2().Nullable()
					.WithColumn("missedoffers").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("awayreason").AsCustom("citext").Nullable()
					.WithColumn("capacity").AsInt32().Nullable();
			}

			Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_dispatcherpresences_department_user ON dispatcherpresences (departmentid, userid);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_dispatcherpresences_signedin ON dispatcherpresences (state) WHERE state > 0;");

			if (!Schema.Table("dispatcherqueueitems").Exists())
			{
				Create.Table("dispatcherqueueitems")
					.WithColumn("dispatcherqueueitemid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("kind").AsInt32().NotNullable()
					.WithColumn("state").AsInt32().NotNullable()
					.WithColumn("outcome").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("callid").AsInt32().Nullable()
					.WithColumn("priority").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("sourceaddress").AsCustom("citext").Nullable()
					.WithColumn("sourcename").AsCustom("citext").Nullable()
					.WithColumn("targetaddress").AsCustom("citext").Nullable()
					.WithColumn("subject").AsCustom("citext").Nullable()
					.WithColumn("preview").AsCustom("citext").Nullable()
					.WithColumn("externalreference").AsCustom("citext").Nullable()
					.WithColumn("conversationkey").AsCustom("citext").Nullable()
					.WithColumn("assigneduserid").AsCustom("citext").Nullable()
					.WithColumn("offeredon").AsDateTime2().Nullable()
					.WithColumn("offerexpireson").AsDateTime2().Nullable()
					.WithColumn("offerattempts").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("declineduserids").AsCustom("citext").Nullable()
					.WithColumn("acceptedon").AsDateTime2().Nullable()
					.WithColumn("completedon").AsDateTime2().Nullable()
					.WithColumn("completedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("dueon").AsDateTime2().Nullable()
					.WithColumn("version").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("createdbyuserid").AsCustom("citext").Nullable()
					.WithColumn("lastactivityon").AsDateTime2().NotNullable()
					.WithColumn("overflowedon").AsDateTime2().Nullable()
					.WithColumn("overflownote").AsCustom("citext").Nullable()
					.WithColumn("escalatedon").AsDateTime2().Nullable()
					.WithColumn("note").AsCustom("citext").Nullable();
			}

			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_dispatcherqueueitems_open ON dispatcherqueueitems (departmentid, state) WHERE state < 3;");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_dispatcherqueueitems_offers ON dispatcherqueueitems (state, offerexpireson) WHERE state < 3;");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_dispatcherqueueitems_call ON dispatcherqueueitems (callid) WHERE callid IS NOT NULL;");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_dispatcherqueueitems_conversation ON dispatcherqueueitems (conversationkey, lastactivityon) WHERE conversationkey IS NOT NULL;");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_dispatcherqueueitems_external ON dispatcherqueueitems (externalreference) WHERE externalreference IS NOT NULL;");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_dispatcherqueueitems_department_created ON dispatcherqueueitems (departmentid, createdon);");

			if (!Schema.Table("dispatcherqueueevents").Exists())
			{
				Create.Table("dispatcherqueueevents")
					.WithColumn("dispatcherqueueeventid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("dispatcherqueueitemid").AsCustom("citext").NotNullable()
					.WithColumn("eventtype").AsInt32().NotNullable()
					.WithColumn("userid").AsCustom("citext").Nullable()
					.WithColumn("occurredon").AsDateTime2().NotNullable()
					.WithColumn("detail").AsCustom("citext").Nullable();
			}

			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_dispatcherqueueevents_item ON dispatcherqueueevents (dispatcherqueueitemid, occurredon);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_dispatcherqueueevents_department ON dispatcherqueueevents (departmentid, occurredon);");

			if (!Schema.Table("dispatcherconversationmessages").Exists())
			{
				Create.Table("dispatcherconversationmessages")
					.WithColumn("dispatcherconversationmessageid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("dispatcherqueueitemid").AsCustom("citext").NotNullable()
					.WithColumn("direction").AsInt32().NotNullable()
					.WithColumn("fromaddress").AsCustom("citext").Nullable()
					.WithColumn("toaddress").AsCustom("citext").Nullable()
					.WithColumn("body").AsCustom("citext").Nullable()
					.WithColumn("userid").AsCustom("citext").Nullable()
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("providermessageid").AsCustom("citext").Nullable()
					.WithColumn("deliverystate").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("error").AsCustom("citext").Nullable();
			}

			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_dispatcherconversationmessages_item ON dispatcherconversationmessages (dispatcherqueueitemid, createdon);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_dispatcherconversationmessages_provider ON dispatcherconversationmessages (providermessageid) WHERE providermessageid IS NOT NULL;");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_dispatcherconversationmessages_department ON dispatcherconversationmessages (departmentid);");
		}

		public override void Down()
		{
			Execute.Sql("DROP TABLE IF EXISTS dispatcherconversationmessages;");
			Execute.Sql("DROP TABLE IF EXISTS dispatcherqueueevents;");
			Execute.Sql("DROP TABLE IF EXISTS dispatcherqueueitems;");
			Execute.Sql("DROP TABLE IF EXISTS dispatcherpresences;");
			Execute.Sql("DROP TABLE IF EXISTS dispatcherbreaks;");
			Execute.Sql("DROP TABLE IF EXISTS shiftdispatchers;");
			Execute.Sql("DROP TABLE IF EXISTS shiftdispatchsettings;");
		}
	}
}
