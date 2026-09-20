using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Workforce &amp; Business Operations plan, Phase B (B1): customer billing profiles (a Contact becomes billable),
	/// simple per-call rate cards, and the department's own billing identity that prints on every invoice.
	/// Registry M0209, the next physical number under the no-gaps rule. No table carries an ADP row marker: nothing here is under Advanced Data Protection (plan decision 44) — customers read invoices and pay pages without a login
	/// (the marker columns were dropped before release on 2026-09-20). The customer Contact row stays under ADP.
	/// Guarded for safe retry.
	/// </summary>
	[Migration(209)]
	public class M0209_AddCustomerBillingAndRateCards : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("CustomerBillingProfiles").Exists())
			{
				Create.Table("CustomerBillingProfiles")
					.WithColumn("CustomerBillingProfileId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ContactId").AsString(128).NotNullable()
					.WithColumn("BillingEmail").AsString(500).Nullable()
					.WithColumn("BillingAddressId").AsInt32().Nullable()
					.WithColumn("UseContactMailingAddress").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("TermsNetDays").AsInt32().NotNullable().WithDefaultValue(30)
					.WithColumn("TaxExempt").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("TaxRate").AsDecimal(9, 4).Nullable()
					.WithColumn("TaxComponentsJson").AsString(int.MaxValue).Nullable()
					.WithColumn("DefaultRateCardId").AsString(36).Nullable()
					.WithColumn("DefaultDiscountPercent").AsDecimal(9, 4).Nullable()
					.WithColumn("DefaultRateScheduleId").AsString(36).Nullable()
					.WithColumn("PurchaseOrderRequired").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("Notes").AsString(int.MaxValue).Nullable()
					.WithColumn("Active").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();

				Create.Index("IX_CustomerBillingProfiles_Department").OnTable("CustomerBillingProfiles")
					.OnColumn("DepartmentId").Ascending().OnColumn("IsDeleted").Ascending();

				// One live billing profile per contact; a soft-deleted row does not block a replacement.
				Execute.Sql("CREATE UNIQUE INDEX [UX_CustomerBillingProfiles_Contact_Live] ON [CustomerBillingProfiles] ([ContactId]) WHERE [IsDeleted] = 0;");
			}

			if (!Schema.Table("RateCards").Exists())
			{
				Create.Table("RateCards")
					.WithColumn("RateCardId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("Name").AsString(200).NotNullable()
					.WithColumn("Description").AsString(int.MaxValue).Nullable()
					.WithColumn("IsDefault").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("Active").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();

				Create.Index("IX_RateCards_Department").OnTable("RateCards")
					.OnColumn("DepartmentId").Ascending().OnColumn("IsDeleted").Ascending();
			}

			if (!Schema.Table("RateCardItems").Exists())
			{
				Create.Table("RateCardItems")
					.WithColumn("RateCardItemId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("RateCardId").AsString(36).NotNullable()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ItemType").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Name").AsString(200).NotNullable()
					.WithColumn("Description").AsString(int.MaxValue).Nullable()
					.WithColumn("Rate").AsDecimal(18, 4).NotNullable().WithDefaultValue(0)
					.WithColumn("UnitLabel").AsString(50).Nullable()
					.WithColumn("MinimumMinutes").AsInt32().Nullable()
					.WithColumn("RoundingMinutes").AsInt32().Nullable()
					.WithColumn("MinimumCharge").AsDecimal(18, 4).Nullable()
					.WithColumn("UnitTypeFilter").AsString(100).Nullable()
					.WithColumn("PersonnelRoleIdFilter").AsInt32().Nullable()
					.WithColumn("Taxable").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("SortOrder").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Active").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();

				Create.Index("IX_RateCardItems_RateCard").OnTable("RateCardItems")
					.OnColumn("RateCardId").Ascending().OnColumn("IsDeleted").Ascending().OnColumn("SortOrder").Ascending();
				Create.Index("IX_RateCardItems_Department").OnTable("RateCardItems")
					.OnColumn("DepartmentId").Ascending();
			}

			if (!Schema.Table("DepartmentBillingIdentities").Exists())
			{
				Create.Table("DepartmentBillingIdentities")
					.WithColumn("DepartmentId").AsInt32().NotNullable().PrimaryKey()
					.WithColumn("LegalBusinessName").AsString(300).Nullable()
					.WithColumn("RemitToAddressId").AsInt32().Nullable()
					.WithColumn("TaxRegistrationNumber").AsString(100).Nullable()
					.WithColumn("SecondaryTaxRegistrationNumber").AsString(100).Nullable()
					.WithColumn("SamUei").AsString(50).Nullable()
					.WithColumn("CageCode").AsString(20).Nullable()
					.WithColumn("WorkersCompAccountNumber").AsString(100).Nullable()
					.WithColumn("InvoiceFooterText").AsString(int.MaxValue).Nullable()
					.WithColumn("OnlinePaymentsEnabled").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("DefaultPaymentConnectionId").AsString(36).Nullable()
					.WithColumn("AllowedPaymentMethodsCsv").AsString(200).Nullable()
					.WithColumn("PayLinkExpiryDays").AsInt32().NotNullable().WithDefaultValue(30)
					.WithColumn("ShowPayOnlineOnDocuments").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("UpdatedOn").AsDateTime2().NotNullable()
					.WithColumn("UpdatedByUserId").AsString(128).Nullable();
			}
		}

		public override void Down()
		{
			if (Schema.Table("DepartmentBillingIdentities").Exists()) Delete.Table("DepartmentBillingIdentities");
			if (Schema.Table("RateCardItems").Exists()) Delete.Table("RateCardItems");
			if (Schema.Table("RateCards").Exists()) Delete.Table("RateCards");
			if (Schema.Table("CustomerBillingProfiles").Exists()) Delete.Table("CustomerBillingProfiles");
		}
	}
}
