using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Workforce &amp; Business Operations plan, Phase C (C1): bids (quotes) with rate-snapshotting line items, lifecycle status, conversion back-links to the Call and Deployment, and the per-department atomic bid number sequence (the invoice-sequence precedent). Registry M0217. Guarded for safe retry.
	/// </summary>
	[Migration(217)]
	public class M0217_AddBids : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("Bids").Exists())
			{
				Create.Table("Bids")
					.WithColumn("BidId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("BidNumber").AsInt32().NotNullable()
					.WithColumn("ContactId").AsString(128).NotNullable()
					.WithColumn("CustomerBillingProfileId").AsString(36).Nullable()
					.WithColumn("ServiceContractId").AsString(36).Nullable()
					.WithColumn("RateScheduleId").AsString(36).Nullable()
					.WithColumn("Title").AsString(250).NotNullable()
					.WithColumn("Description").AsString(int.MaxValue).Nullable()
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ValidUntil").AsDateTime2().Nullable()
					.WithColumn("RequestedStartOn").AsDateTime2().Nullable()
					.WithColumn("RequestedEndOn").AsDateTime2().Nullable()
					.WithColumn("IncidentNumber").AsString(100).Nullable()
					.WithColumn("DeliveryLocation").AsString(int.MaxValue).Nullable()
					.WithColumn("DiscountPercent").AsDecimal(9,4).Nullable()
					.WithColumn("EstimatedSubTotal").AsDecimal(18,2).NotNullable().WithDefaultValue(0)
					.WithColumn("EstimatedDiscountAmount").AsDecimal(18,2).NotNullable().WithDefaultValue(0)
					.WithColumn("EstimatedTaxAmount").AsDecimal(18,2).NotNullable().WithDefaultValue(0)
					.WithColumn("EstimatedTotal").AsDecimal(18,2).NotNullable().WithDefaultValue(0)
					.WithColumn("Notes").AsString(int.MaxValue).Nullable()
					.WithColumn("TermsText").AsString(int.MaxValue).Nullable()
					.WithColumn("SentOn").AsDateTime2().Nullable()
					.WithColumn("SentToEmail").AsString(500).Nullable()
					.WithColumn("AcceptedOn").AsDateTime2().Nullable()
					.WithColumn("DeclinedOn").AsDateTime2().Nullable()
					.WithColumn("DeclineReason").AsString(int.MaxValue).Nullable()
					.WithColumn("ConvertedCallId").AsInt32().Nullable()
					.WithColumn("ConvertedDeploymentId").AsString(36).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable()
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedCatalogVersion").AsInt32().Nullable();
				Create.Index("IX_Bids_Department").OnTable("Bids").OnColumn("DepartmentId").Ascending().OnColumn("IsDeleted").Ascending().OnColumn("Status").Ascending();
				Create.Index("IX_Bids_Contact").OnTable("Bids").OnColumn("ContactId").Ascending();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_Bids_Number' AND object_id = OBJECT_ID('Bids')) CREATE UNIQUE INDEX [UX_Bids_Number] ON [Bids] ([DepartmentId], [BidNumber]);");
			}
			if (!Schema.Table("BidLineItems").Exists())
			{
				Create.Table("BidLineItems")
					.WithColumn("BidLineItemId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("BidId").AsString(36).NotNullable()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("RateScheduleEntryId").AsString(36).Nullable()
					.WithColumn("LineType").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Description").AsString(int.MaxValue).NotNullable()
					.WithColumn("CrewSize").AsInt32().Nullable()
					.WithColumn("Quantity").AsDecimal(18,4).NotNullable().WithDefaultValue(1)
					.WithColumn("EstimatedHoursPerDay").AsDecimal(9,2).Nullable()
					.WithColumn("EstimatedDays").AsDecimal(9,2).Nullable()
					.WithColumn("UnitRate").AsDecimal(18,4).NotNullable().WithDefaultValue(0)
					.WithColumn("PremiumIdsJson").AsString(int.MaxValue).Nullable()
					.WithColumn("EstimatedAmount").AsDecimal(18,2).NotNullable().WithDefaultValue(0)
					.WithColumn("Taxable").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("SortOrder").AsInt32().NotNullable().WithDefaultValue(0);
				Create.Index("IX_BidLineItems_Bid").OnTable("BidLineItems").OnColumn("BidId").Ascending().OnColumn("SortOrder").Ascending();
				Create.ForeignKey("FK_BidLineItems_Bid").FromTable("BidLineItems").ForeignColumn("BidId").ToTable("Bids").PrimaryColumn("BidId");
			}
			if (!Schema.Table("BidNumberSequences").Exists())
			{
				Create.Table("BidNumberSequences")
					.WithColumn("DepartmentId").AsInt32().NotNullable().PrimaryKey()
					.WithColumn("NextBidNumber").AsInt32().NotNullable().WithDefaultValue(1);
			}
		}

		public override void Down()
		{
			if (Schema.Table("BidNumberSequences").Exists()) Delete.Table("BidNumberSequences");
			if (Schema.Table("BidLineItems").Exists()) Delete.Table("BidLineItems");
			if (Schema.Table("Bids").Exists()) Delete.Table("Bids");
		}
	}
}
