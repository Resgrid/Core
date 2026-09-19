using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0212 (Workforce &amp; Business Operations plan, Phase B2): department payment-provider
	/// connections, hosted payment requests, the received-event ledger, the Payments.StripeConnect cluster switch,
	/// the Invoicing.OnlinePayments flag and their prerequisite edges, plus the unique gateway index on
	/// invoicepayments. Same number, lower-case identifiers, guarded for safe retry.
	/// </summary>
	[Migration(212)]
	public class M0212_AddOnlinePaymentsPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("departmentpaymentconnections").Exists())
			{
				Create.Table("departmentpaymentconnections")
					.WithColumn("departmentpaymentconnectionid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("provider").AsInt32().NotNullable()
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("environment").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("externalaccountid").AsString(200).NotNullable()
					.WithColumn("displayname").AsString(300).Nullable()
					.WithColumn("country").AsString(2).Nullable()
					.WithColumn("defaultcurrency").AsString(3).Nullable()
					.WithColumn("capabilitiesjson").AsCustom("text").Nullable()
					.WithColumn("scopescsv").AsString(200).Nullable()
					.WithColumn("accesstokenciphertext").AsCustom("text").Nullable()
					.WithColumn("refreshtokenciphertext").AsCustom("text").Nullable()
					.WithColumn("tokenexpireson").AsDateTime2().Nullable()
					.WithColumn("connectedon").AsDateTime2().NotNullable()
					.WithColumn("connectedbyuserid").AsString(128).Nullable()
					.WithColumn("disconnectedon").AsDateTime2().Nullable()
					.WithColumn("lastverifiedon").AsDateTime2().Nullable()
					.WithColumn("lasterror").AsString(500).Nullable()
					.WithColumn("isdefault").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsString(128).Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsString(128).Nullable();

				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_departmentpaymentconnections_department ON departmentpaymentconnections (departmentid, isdeleted);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_departmentpaymentconnections_account ON departmentpaymentconnections (provider, externalaccountid);");
				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_departmentpaymentconnections_live ON departmentpaymentconnections (departmentid, provider, environment) WHERE isdeleted = FALSE;");
			}

			if (!Schema.Table("invoicepaymentrequests").Exists())
			{
				Create.Table("invoicepaymentrequests")
					.WithColumn("invoicepaymentrequestid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("invoiceid").AsString(36).NotNullable()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("departmentpaymentconnectionid").AsString(36).Nullable()
					.WithColumn("provider").AsInt32().NotNullable()
					.WithColumn("amount").AsDecimal(18, 2).NotNullable()
					.WithColumn("currency").AsString(3).NotNullable()
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("externalreference").AsString(200).Nullable()
					.WithColumn("paymentintentid").AsString(200).Nullable()
					.WithColumn("hostedurl").AsString(2048).Nullable()
					.WithColumn("expireson").AsDateTime2().NotNullable()
					.WithColumn("completedon").AsDateTime2().Nullable()
					.WithColumn("invoicepaymentid").AsString(36).Nullable()
					.WithColumn("source").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("createdbyuserid").AsString(128).Nullable()
					.WithColumn("lasterror").AsString(1000).Nullable()
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("updatedon").AsDateTime2().NotNullable();

				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_invoicepaymentrequests_invoice ON invoicepaymentrequests (invoiceid, status);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_invoicepaymentrequests_reference ON invoicepaymentrequests (provider, externalreference);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_invoicepaymentrequests_intent ON invoicepaymentrequests (provider, paymentintentid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_invoicepaymentrequests_status_added ON invoicepaymentrequests (status, addedon);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_invoicepaymentrequests_connection ON invoicepaymentrequests (departmentpaymentconnectionid, status);");
				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_invoicepaymentrequests_open ON invoicepaymentrequests (invoiceid) WHERE status IN (0, 1, 2);");
			}

			if (!Schema.Table("paymentproviderevents").Exists())
			{
				Create.Table("paymentproviderevents")
					.WithColumn("paymentconnecteventid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("provider").AsInt32().NotNullable()
					.WithColumn("externaleventid").AsString(200).NotNullable()
					.WithColumn("externalaccountid").AsString(200).Nullable()
					.WithColumn("eventtype").AsString(100).Nullable()
					.WithColumn("departmentid").AsInt32().Nullable()
					.WithColumn("livemode").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("receivedon").AsDateTime2().NotNullable()
					.WithColumn("processedon").AsDateTime2().Nullable()
					.WithColumn("outcome").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("error").AsString(1000).Nullable()
					.WithColumn("payloadjson").AsCustom("text").Nullable();

				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_paymentproviderevents_event ON paymentproviderevents (provider, externaleventid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_paymentproviderevents_account ON paymentproviderevents (provider, externalaccountid, receivedon);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_paymentproviderevents_received ON paymentproviderevents (receivedon);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_paymentproviderevents_outcome ON paymentproviderevents (outcome, receivedon);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_paymentproviderevents_department ON paymentproviderevents (departmentid);");
			}

			// Idempotency of online payments is enforced by the database, not only by the pre-insert lookup: M0210's
			// non-unique gateway index is replaced by a partial unique one (manual payments carry no transaction id).
			Execute.Sql("DROP INDEX IF EXISTS ix_invoicepayments_gateway;");
			Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_invoicepayments_gateway ON invoicepayments (provider, gatewaytransactionid) WHERE gatewaytransactionid IS NOT NULL AND provider IS NOT NULL;");

			Execute.Sql(
				"INSERT INTO featureflags (flagkey, name, description, category, isenabledglobally, ispermanent) " +
				"SELECT 'Payments.StripeConnect', 'Stripe Connect payment collection', 'Operator per-cluster switch for departments connecting their own Stripe account to collect invoice payments (Workforce & Business Operations plan, Phase B2). Global state only; on in the US cluster at launch, off in the EU cluster. Seeded off.', 'Business', false, true " +
				"WHERE NOT EXISTS (SELECT 1 FROM featureflags WHERE flagkey = 'Payments.StripeConnect');");

			Execute.Sql(
				"INSERT INTO featureflags (flagkey, name, description, category, isenabledglobally) " +
				"SELECT 'Invoicing.OnlinePayments', 'Online invoice payments', 'Pay-online links and the Online payments settings tab (Workforce & Business Operations plan, Phase B2). Requires Invoicing.CustomerInvoicing and the Payments.StripeConnect cluster switch. Seeded off.', 'Business', false " +
				"WHERE NOT EXISTS (SELECT 1 FROM featureflags WHERE flagkey = 'Invoicing.OnlinePayments');");

			foreach (var required in new[] { "Invoicing.CustomerInvoicing", "Payments.StripeConnect" })
			{
				Execute.Sql(
					"INSERT INTO featureflagprerequisites (featureflagid, requiredfeatureflagid, requiredvalue) " +
					"SELECT f.featureflagid, r.featureflagid, NULL FROM featureflags f CROSS JOIN featureflags r " +
					"WHERE f.flagkey = 'Invoicing.OnlinePayments' AND r.flagkey = '" + required + "' " +
					"AND NOT EXISTS (SELECT 1 FROM featureflagprerequisites p WHERE p.featureflagid = f.featureflagid AND p.requiredfeatureflagid = r.featureflagid);");
			}
		}

		public override void Down()
		{
			Execute.Sql("DROP INDEX IF EXISTS ux_invoicepayments_gateway;");
			Execute.Sql("DO $guard$ BEGIN IF to_regclass('invoicepayments') IS NOT NULL THEN CREATE INDEX IF NOT EXISTS ix_invoicepayments_gateway ON invoicepayments (provider, gatewaytransactionid); END IF; END $guard$;");
			Execute.Sql("DO $guard$ BEGIN IF to_regclass('paymentproviderevents') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM paymentproviderevents) THEN DROP TABLE paymentproviderevents; END IF; END $guard$;");
			Execute.Sql("DO $guard$ BEGIN IF to_regclass('invoicepaymentrequests') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM invoicepaymentrequests) THEN DROP TABLE invoicepaymentrequests; END IF; END $guard$;");
			Execute.Sql("DO $guard$ BEGIN IF to_regclass('departmentpaymentconnections') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM departmentpaymentconnections) THEN DROP TABLE departmentpaymentconnections; END IF; END $guard$;");
		}
	}
}
