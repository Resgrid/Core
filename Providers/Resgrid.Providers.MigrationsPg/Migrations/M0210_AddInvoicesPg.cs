using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0210 (Workforce &amp; Business Operations plan, Phase B, B1): invoices, line items,
	/// payments and the invoice number sequence. Same number, lower-case names, guarded for safe retry.
	/// </summary>
	[Migration(210)]
	public class M0210_AddInvoicesPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("invoices").Exists())
			{
				Create.Table("invoices")
					.WithColumn("invoiceid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("invoicenumber").AsInt32().NotNullable()
					.WithColumn("customerbillingprofileid").AsString(36).NotNullable()
					.WithColumn("contactid").AsString(128).NotNullable()
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("issuedon").AsDateTime2().Nullable()
					.WithColumn("dueon").AsDateTime2().Nullable()
					.WithColumn("currency").AsString(3).NotNullable().WithDefaultValue("USD")
					.WithColumn("subtotal").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
					.WithColumn("discountpercent").AsDecimal(9, 4).Nullable()
					.WithColumn("discountamount").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
					.WithColumn("taxamount").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
					.WithColumn("total").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
					.WithColumn("amountpaid").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
					.WithColumn("taxcomponentsjson").AsString(int.MaxValue).Nullable()
					.WithColumn("notes").AsString(int.MaxValue).Nullable()
					.WithColumn("termstext").AsString(int.MaxValue).Nullable()
					.WithColumn("senton").AsDateTime2().Nullable()
					.WithColumn("senttoemail").AsString(500).Nullable()
					.WithColumn("paidon").AsDateTime2().Nullable()
					.WithColumn("voidedon").AsDateTime2().Nullable()
					.WithColumn("voidreason").AsString(1000).Nullable()
					.WithColumn("platformfeeamount").AsDecimal(18, 2).Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsString(128).Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsString(128).Nullable()
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().NotNullable().WithDefaultValue(0);

				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_invoices_department_number ON invoices (departmentid, invoicenumber);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_invoices_department_status ON invoices (departmentid, status, isdeleted);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_invoices_department_contact ON invoices (departmentid, contactid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_invoices_department_due ON invoices (departmentid, dueon);");
			}

			if (!Schema.Table("invoicelineitems").Exists())
			{
				Create.Table("invoicelineitems")
					.WithColumn("invoicelineitemid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("invoiceid").AsString(36).NotNullable()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("callid").AsInt32().Nullable()
					.WithColumn("ratecarditemid").AsString(36).Nullable()
					.WithColumn("description").AsString(1000).NotNullable()
					.WithColumn("quantity").AsDecimal(18, 4).NotNullable().WithDefaultValue(1)
					.WithColumn("unitrate").AsDecimal(18, 4).NotNullable().WithDefaultValue(0)
					.WithColumn("amount").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
					.WithColumn("taxable").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("sortorder").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().NotNullable().WithDefaultValue(0);

				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_invoicelineitems_invoice ON invoicelineitems (invoiceid, sortorder);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_invoicelineitems_department_call ON invoicelineitems (departmentid, callid);");
			}

			if (!Schema.Table("invoicepayments").Exists())
			{
				Create.Table("invoicepayments")
					.WithColumn("invoicepaymentid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("invoiceid").AsString(36).NotNullable()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("amount").AsDecimal(18, 2).NotNullable()
					.WithColumn("method").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("reference").AsString(200).Nullable()
					.WithColumn("gatewaytransactionid").AsString(200).Nullable()
					.WithColumn("paymentrequestid").AsString(36).Nullable()
					.WithColumn("provider").AsInt32().Nullable()
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("refundedamount").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
					.WithColumn("providerfeeamount").AsDecimal(18, 2).Nullable()
					.WithColumn("netamount").AsDecimal(18, 2).Nullable()
					.WithColumn("payeremail").AsString(500).Nullable()
					.WithColumn("paymentmethodsummary").AsString(100).Nullable()
					.WithColumn("receipturl").AsString(2048).Nullable()
					.WithColumn("notes").AsString(int.MaxValue).Nullable()
					.WithColumn("paidon").AsDateTime2().NotNullable()
					.WithColumn("recordedbyuserid").AsString(128).Nullable()
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().NotNullable().WithDefaultValue(0);

				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_invoicepayments_invoice ON invoicepayments (invoiceid, paidon);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_invoicepayments_department_paid ON invoicepayments (departmentid, paidon);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_invoicepayments_gateway ON invoicepayments (provider, gatewaytransactionid);");
			}

			if (!Schema.Table("invoicenumbersequences").Exists())
			{
				Create.Table("invoicenumbersequences")
					.WithColumn("departmentid").AsInt32().NotNullable().PrimaryKey()
					.WithColumn("nextinvoicenumber").AsInt32().NotNullable().WithDefaultValue(1);
			}
		}

		public override void Down()
		{
			if (Schema.Table("invoicenumbersequences").Exists()) Delete.Table("invoicenumbersequences");
			if (Schema.Table("invoicepayments").Exists()) Delete.Table("invoicepayments");
			if (Schema.Table("invoicelineitems").Exists()) Delete.Table("invoicelineitems");
			if (Schema.Table("invoices").Exists()) Delete.Table("invoices");
		}
	}
}
