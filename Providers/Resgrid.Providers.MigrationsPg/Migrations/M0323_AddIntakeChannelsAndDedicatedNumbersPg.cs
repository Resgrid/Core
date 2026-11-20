using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Postgres twin of M0323_AddIntakeChannelsAndDedicatedNumbers: intake channels and messages, dedicated department numbers,
	/// voice intake sessions, AI destination advice and reviews, the Dedicated Number billing accounts, the PlanAddons row
	/// (type 7, USD 15/month, live Stripe price price_0UduJSqJFDZJcnkV8cIpUYFE; the test-mode price is still empty) and five flags
	/// (Intake.Channels, Intake.DedicatedNumber, Ai.DestinationAdvice / Ai.Intake / Ai.VoiceIntake).
	/// </summary>
	[Migration(323)]
	public class M0323_AddIntakeChannelsAndDedicatedNumbersPg : Migration
	{
		private const string DedicatedNumberAddonId = "3b1c7f0e-5d2a-4c86-9a4e-7f2d1e6b8c40";

		/// <summary>Live Stripe price of the add-on ("Private Inbound Dispatch Number", product prod_VfEnZg5YGLGGMp), set 2026-11-20.</summary>
		private const string StripePriceId = "price_0UduJSqJFDZJcnkV8cIpUYFE";

		private static readonly (string Key, string Name, string Description, string Category, bool AiChild)[] Flags =
		{
			("Intake.Channels", "Intake channels", "SMS, email, chat and webhook messages become pending calls. Operator rollout. Seeded off.", "Intake", false),
			("Intake.DedicatedNumber", "Private Inbound Dispatch Number add-on", "Private Inbound Dispatch Number add-on (the department's own phone number): card and checkout (PlanAddonTypes 7). Seeded off.", "Intake", false),
			("Ai.DestinationAdvice", "AI destination advice", "Enhanced AI: destination advice from the call nature, note and call notes, with change suggestions. Requires Ai.Enhanced and the Enhanced AI add-on. Seeded off.", "AI", true),
			("Ai.Intake", "AI intake", "Enhanced AI: intake messages saved as pending calls are filled in by the model. Requires Ai.Enhanced and the Enhanced AI add-on. Seeded off.", "AI", true),
			("Ai.VoiceIntake", "AI voice intake", "Enhanced AI: guided voice intake on the private inbound dispatch number creates pending calls. Requires Ai.Enhanced and the Enhanced AI add-on. Seeded off.", "AI", true)
		};

		public override void Up()
		{
			if (!Schema.Table("intakechannels").Exists())
			{
				Create.Table("intakechannels")
					.WithColumn("intakechannelid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("name").AsCustom("citext").Nullable()
					.WithColumn("channeltype").AsInt32().NotNullable()
					.WithColumn("address").AsCustom("citext").Nullable()
					.WithColumn("processingmode").AsInt32().NotNullable()
					.WithColumn("templateformat").AsInt32().Nullable()
					.WithColumn("allowedsenders").AsCustom("text").Nullable()
					.WithColumn("defaultpriority").AsInt32().Nullable()
					.WithColumn("defaultcalltypeid").AsInt32().Nullable()
					.WithColumn("sendacknowledgement").AsBoolean().NotNullable()
					.WithColumn("webhooksecretprotected").AsCustom("text").Nullable()
					.WithColumn("isenabled").AsBoolean().NotNullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable()
					.WithColumn("createdbyuserid").AsCustom("citext").Nullable()
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("updatedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("updatedon").AsDateTime2().Nullable();
			}

			Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_intakechannels_address ON intakechannels (channeltype, address) WHERE isdeleted = false AND address IS NOT NULL;");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_intakechannels_department ON intakechannels (departmentid);");

			if (!Schema.Table("intakemessages").Exists())
			{
				Create.Table("intakemessages")
					.WithColumn("intakemessageid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("intakechannelid").AsString(36).Nullable()
					.WithColumn("channeltype").AsInt32().NotNullable()
					.WithColumn("providermessageid").AsCustom("citext").Nullable()
					.WithColumn("sender").AsCustom("citext").Nullable()
					.WithColumn("state").AsInt32().NotNullable()
					.WithColumn("callid").AsInt32().Nullable()
					.WithColumn("reason").AsCustom("citext").Nullable()
					.WithColumn("receivedon").AsDateTime2().NotNullable()
					.WithColumn("completedon").AsDateTime2().Nullable();
			}

			Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_intakemessages_provider ON intakemessages (intakechannelid, providermessageid) WHERE intakechannelid IS NOT NULL AND providermessageid IS NOT NULL;");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_intakemessages_usage ON intakemessages (departmentid, channeltype, receivedon);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_intakemessages_received ON intakemessages (receivedon);");

			if (!Schema.Table("departmentphonenumbers").Exists())
			{
				Create.Table("departmentphonenumbers")
					.WithColumn("departmentphonenumberid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("phonenumber").AsCustom("citext").NotNullable()
					.WithColumn("provider").AsCustom("citext").Nullable()
					.WithColumn("providernumberid").AsCustom("citext").Nullable()
					.WithColumn("countrycode").AsCustom("citext").Nullable()
					.WithColumn("smscapable").AsBoolean().NotNullable()
					.WithColumn("voicecapable").AsBoolean().NotNullable()
					.WithColumn("status").AsInt32().NotNullable()
					.WithColumn("smsintakeenabled").AsBoolean().NotNullable()
					.WithColumn("voiceintakeenabled").AsBoolean().NotNullable()
					.WithColumn("provisionedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("provisionedon").AsDateTime2().Nullable()
					.WithColumn("releaseafter").AsDateTime2().Nullable()
					.WithColumn("releasedon").AsDateTime2().Nullable()
					.WithColumn("lasterror").AsCustom("citext").Nullable()
					.WithColumn("addoninactivesince").AsDateTime2().Nullable()
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("updatedon").AsDateTime2().Nullable();
			}

			// Added to the unapplied migration on 2026-11-19 (worker 80 renewal delay); a dev database that already ran it gets the column here.
			Execute.Sql("ALTER TABLE departmentphonenumbers ADD COLUMN IF NOT EXISTS addoninactivesince timestamp NULL;");

			Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_departmentphonenumbers_number ON departmentphonenumbers (phonenumber) WHERE status IN (1, 2, 3);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_departmentphonenumbers_department ON departmentphonenumbers (departmentid, status);");

			if (!Schema.Table("voiceintakesessions").Exists())
			{
				Create.Table("voiceintakesessions")
					.WithColumn("voiceintakesessionid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("providercallid").AsCustom("citext").NotNullable()
					.WithColumn("fromnumber").AsCustom("citext").Nullable()
					.WithColumn("tonumber").AsCustom("citext").Nullable()
					.WithColumn("step").AsInt32().NotNullable()
					.WithColumn("answersjson").AsCustom("text").Nullable()
					.WithColumn("retries").AsInt32().NotNullable()
					.WithColumn("callid").AsInt32().Nullable()
					.WithColumn("startedon").AsDateTime2().NotNullable()
					.WithColumn("completedon").AsDateTime2().Nullable();
			}

			Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_voiceintakesessions_call ON voiceintakesessions (providercallid);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_voiceintakesessions_usage ON voiceintakesessions (departmentid, startedon);");

			if (!Schema.Table("aidestinationadvices").Exists())
			{
				Create.Table("aidestinationadvices")
					.WithColumn("aidestinationadviceid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("callid").AsInt32().NotNullable()
					.WithColumn("currentpoiid").AsInt32().Nullable()
					.WithColumn("recommendedpoiid").AsInt32().Nullable()
					.WithColumn("requiredcapabilityids").AsCustom("text").Nullable()
					.WithColumn("rationale").AsCustom("citext").Nullable()
					.WithColumn("confidence").AsDouble().NotNullable()
					.WithColumn("ischange").AsBoolean().NotNullable()
					.WithColumn("reviewtrigger").AsInt32().NotNullable()
					.WithColumn("state").AsInt32().NotNullable()
					.WithColumn("inputdigest").AsCustom("citext").Nullable()
					.WithColumn("promptversion").AsCustom("citext").Nullable()
					.WithColumn("modelrevision").AsCustom("citext").Nullable()
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("resolvedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("resolvedon").AsDateTime2().Nullable();
			}

			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_aidestinationadvices_call ON aidestinationadvices (callid, state);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_aidestinationadvices_department ON aidestinationadvices (departmentid);");

			if (!Schema.Table("aidestinationreviews").Exists())
			{
				Create.Table("aidestinationreviews")
					.WithColumn("aidestinationreviewid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("callid").AsInt32().NotNullable()
					.WithColumn("reviewtrigger").AsInt32().NotNullable()
					.WithColumn("requestedon").AsDateTime2().NotNullable()
					.WithColumn("claimedon").AsDateTime2().Nullable()
					.WithColumn("processedon").AsDateTime2().Nullable()
					.WithColumn("outcome").AsCustom("citext").Nullable();
			}

			Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_aidestinationreviews_open ON aidestinationreviews (callid) WHERE processedon IS NULL;");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_aidestinationreviews_due ON aidestinationreviews (processedon, requestedon);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_aidestinationreviews_department ON aidestinationreviews (departmentid);");

			if (!Schema.Table("dedicatednumberbillingaccounts").Exists())
			{
				Create.Table("dedicatednumberbillingaccounts")
					.WithColumn("departmentid").AsInt32().NotNullable().PrimaryKey()
					.WithColumn("provider").AsString(20).NotNullable()
					.WithColumn("customerid").AsString(100).NotNullable()
					.WithColumn("planaddonid").AsString(36).NotNullable()
					.WithColumn("priceid").AsString(100).NotNullable()
					.WithColumn("subscriptionid").AsString(100).Nullable()
					.WithColumn("checkoutid").AsString(100).Nullable()
					.WithColumn("checkouturl").AsString(2048).Nullable()
					.WithColumn("checkoutexpireson").AsDateTime2().Nullable()
					.WithColumn("checkoutattempt").AsString(36).Nullable()
					.WithColumn("updatedon").AsDateTime2().NotNullable();
			}

			Execute.Sql(
				"INSERT INTO planaddons (planaddonid, addontype, cost, externalid, testexternalid) " +
				"SELECT '" + DedicatedNumberAddonId + "', 7, 15, '" + StripePriceId + "', '' " +
				"WHERE NOT EXISTS (SELECT 1 FROM planaddons WHERE planaddonid = '" + DedicatedNumberAddonId + "' OR addontype = 7);");

			foreach (var flag in Flags)
			{
				Execute.Sql(
					"INSERT INTO featureflags (flagkey, name, description, category, isenabledglobally) " +
					"SELECT '" + flag.Key + "', '" + flag.Name + "', '" + flag.Description + "', '" + flag.Category + "', false " +
					"WHERE NOT EXISTS (SELECT 1 FROM featureflags WHERE flagkey = '" + flag.Key + "');");

				if (!flag.AiChild)
					continue;

				Execute.Sql(
					"INSERT INTO featureflagprerequisites (featureflagid, requiredfeatureflagid, requiredvalue) " +
					"SELECT f.featureflagid, r.featureflagid, NULL FROM featureflags f CROSS JOIN featureflags r " +
					"WHERE f.flagkey = '" + flag.Key + "' AND r.flagkey = 'Ai.Enhanced' " +
					"AND NOT EXISTS (SELECT 1 FROM featureflagprerequisites p WHERE p.featureflagid = f.featureflagid AND p.requiredfeatureflagid = r.featureflagid);");
			}
		}

		public override void Down()
		{
			foreach (var table in new[] { "dedicatednumberbillingaccounts", "aidestinationreviews", "aidestinationadvices", "voiceintakesessions",
						 "departmentphonenumbers", "intakemessages", "intakechannels" })
				Execute.Sql($"DROP TABLE IF EXISTS {table};");
		}
	}
}
