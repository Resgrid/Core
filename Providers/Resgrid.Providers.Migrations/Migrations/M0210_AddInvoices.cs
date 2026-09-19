using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Workforce &amp; Business Operations plan, Phase B (B1): invoices, line items, payments and the per-department
	/// invoice number sequence. Registry M0210. Money is decimal(18,2) for totals and decimal(18,4) for rates.
	/// InvoicePayments already carries the Phase B2 online-payment columns (request id, provider, status, refund,
	/// fee/net, payer e-mail, method summary, receipt URL) so M0212 adds no ALTER; Invoices.PlatformFeeAmount is
	/// reserved and always null in v1. Every table carries the ADP row marker from creation. Guarded for safe retry.
	/// </summary>
	[Migration(210)]
	public class M0210_AddInvoices : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("Invoices").Exists())
			{
				Create.Table("Invoices")
					.WithColumn("InvoiceId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("InvoiceNumber").AsInt32().NotNullable()
					.WithColumn("CustomerBillingProfileId").AsString(36).NotNullable()
					.WithColumn("ContactId").AsString(128).NotNullable()
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("IssuedOn").AsDateTime2().Nullable()
					.WithColumn("DueOn").AsDateTime2().Nullable()
					.WithColumn("Currency").AsString(3).NotNullable().WithDefaultValue("USD")
					.WithColumn("SubTotal").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
					.WithColumn("DiscountPercent").AsDecimal(9, 4).Nullable()
					.WithColumn("DiscountAmount").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
					.WithColumn("TaxAmount").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
					.WithColumn("Total").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
					.WithColumn("AmountPaid").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
					.WithColumn("TaxComponentsJson").AsString(int.MaxValue).Nullable()
					.WithColumn("Notes").AsString(int.MaxValue).Nullable()
					.WithColumn("TermsText").AsString(int.MaxValue).Nullable()
					.WithColumn("SentOn").AsDateTime2().Nullable()
					.WithColumn("SentToEmail").AsString(500).Nullable()
					.WithColumn("PaidOn").AsDateTime2().Nullable()
					.WithColumn("VoidedOn").AsDateTime2().Nullable()
					.WithColumn("VoidReason").AsString(1000).Nullable()
					.WithColumn("PlatformFeeAmount").AsDecimal(18, 2).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable()
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedCatalogVersion").AsInt32().NotNullable().WithDefaultValue(0);

				// The sequence table is the authority; this unique index is the backstop (decision 7).
				Create.Index("UX_Invoices_Department_Number").OnTable("Invoices")
					.OnColumn("DepartmentId").Ascending().OnColumn("InvoiceNumber").Ascending().WithOptions().Unique();
				Create.Index("IX_Invoices_Department_Status").OnTable("Invoices")
					.OnColumn("DepartmentId").Ascending().OnColumn("Status").Ascending().OnColumn("IsDeleted").Ascending();
				Create.Index("IX_Invoices_Department_Contact").OnTable("Invoices")
					.OnColumn("DepartmentId").Ascending().OnColumn("ContactId").Ascending();
				Create.Index("IX_Invoices_Department_Due").OnTable("Invoices")
					.OnColumn("DepartmentId").Ascending().OnColumn("DueOn").Ascending();
			}

			if (!Schema.Table("InvoiceLineItems").Exists())
			{
				Create.Table("InvoiceLineItems")
					.WithColumn("InvoiceLineItemId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("InvoiceId").AsString(36).NotNullable()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("CallId").AsInt32().Nullable()
					.WithColumn("RateCardItemId").AsString(36).Nullable()
					.WithColumn("Description").AsString(1000).NotNullable()
					.WithColumn("Quantity").AsDecimal(18, 4).NotNullable().WithDefaultValue(1)
					.WithColumn("UnitRate").AsDecimal(18, 4).NotNullable().WithDefaultValue(0)
					.WithColumn("Amount").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
					.WithColumn("Taxable").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("SortOrder").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedCatalogVersion").AsInt32().NotNullable().WithDefaultValue(0);

				Create.Index("IX_InvoiceLineItems_Invoice").OnTable("InvoiceLineItems")
					.OnColumn("InvoiceId").Ascending().OnColumn("SortOrder").Ascending();
				Create.Index("IX_InvoiceLineItems_Department_Call").OnTable("InvoiceLineItems")
					.OnColumn("DepartmentId").Ascending().OnColumn("CallId").Ascending();
			}

			if (!Schema.Table("InvoicePayments").Exists())
			{
				Create.Table("InvoicePayments")
					.WithColumn("InvoicePaymentId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("InvoiceId").AsString(36).NotNullable()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("Amount").AsDecimal(18, 2).NotNullable()
					.WithColumn("Method").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Reference").AsString(200).Nullable()
					.WithColumn("GatewayTransactionId").AsString(200).Nullable()
					.WithColumn("PaymentRequestId").AsString(36).Nullable()
					.WithColumn("Provider").AsInt32().Nullable()
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("RefundedAmount").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
					.WithColumn("ProviderFeeAmount").AsDecimal(18, 2).Nullable()
					.WithColumn("NetAmount").AsDecimal(18, 2).Nullable()
					.WithColumn("PayerEmail").AsString(500).Nullable()
					.WithColumn("PaymentMethodSummary").AsString(100).Nullable()
					.WithColumn("ReceiptUrl").AsString(2048).Nullable()
					.WithColumn("Notes").AsString(int.MaxValue).Nullable()
					.WithColumn("PaidOn").AsDateTime2().NotNullable()
					.WithColumn("RecordedByUserId").AsString(128).Nullable()
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedCatalogVersion").AsInt32().NotNullable().WithDefaultValue(0);

				Create.Index("IX_InvoicePayments_Invoice").OnTable("InvoicePayments")
					.OnColumn("InvoiceId").Ascending().OnColumn("PaidOn").Ascending();
				Create.Index("IX_InvoicePayments_Department_Paid").OnTable("InvoicePayments")
					.OnColumn("DepartmentId").Ascending().OnColumn("PaidOn").Ascending();
				Create.Index("IX_InvoicePayments_Gateway").OnTable("InvoicePayments")
					.OnColumn("Provider").Ascending().OnColumn("GatewayTransactionId").Ascending();
			}

			if (!Schema.Table("InvoiceNumberSequences").Exists())
			{
				Create.Table("InvoiceNumberSequences")
					.WithColumn("DepartmentId").AsInt32().NotNullable().PrimaryKey()
					.WithColumn("NextInvoiceNumber").AsInt32().NotNullable().WithDefaultValue(1);
			}
		}

		public override void Down()
		{
			if (Schema.Table("InvoiceNumberSequences").Exists()) Delete.Table("InvoiceNumberSequences");
			if (Schema.Table("InvoicePayments").Exists()) Delete.Table("InvoicePayments");
			if (Schema.Table("InvoiceLineItems").Exists()) Delete.Table("InvoiceLineItems");
			if (Schema.Table("Invoices").Exists()) Delete.Table("Invoices");
		}
	}
}
