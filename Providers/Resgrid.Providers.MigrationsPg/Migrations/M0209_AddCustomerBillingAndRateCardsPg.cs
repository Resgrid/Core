using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0209 (Workforce &amp; Business Operations plan, Phase B, B1): customer billing profiles,
	/// rate cards and the department billing identity. Same number, lower-case names, guarded for safe retry.
	/// </summary>
	[Migration(209)]
	public class M0209_AddCustomerBillingAndRateCardsPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("customerbillingprofiles").Exists())
			{
				Create.Table("customerbillingprofiles")
					.WithColumn("customerbillingprofileid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("contactid").AsString(128).NotNullable()
					.WithColumn("billingemail").AsString(500).Nullable()
					.WithColumn("billingaddressid").AsInt32().Nullable()
					.WithColumn("usecontactmailingaddress").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("termsnetdays").AsInt32().NotNullable().WithDefaultValue(30)
					.WithColumn("taxexempt").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("taxrate").AsDecimal(9, 4).Nullable()
					.WithColumn("taxcomponentsjson").AsString(int.MaxValue).Nullable()
					.WithColumn("defaultratecardid").AsString(36).Nullable()
					.WithColumn("defaultdiscountpercent").AsDecimal(9, 4).Nullable()
					.WithColumn("defaultratescheduleid").AsString(36).Nullable()
					.WithColumn("purchaseorderrequired").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("notes").AsString(int.MaxValue).Nullable()
					.WithColumn("active").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsString(128).Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsString(128).Nullable()
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().NotNullable().WithDefaultValue(0);

				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_customerbillingprofiles_department ON customerbillingprofiles (departmentid, isdeleted);");
				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_customerbillingprofiles_contact_live ON customerbillingprofiles (contactid) WHERE isdeleted = FALSE;");
			}

			if (!Schema.Table("ratecards").Exists())
			{
				Create.Table("ratecards")
					.WithColumn("ratecardid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("name").AsString(200).NotNullable()
					.WithColumn("description").AsString(int.MaxValue).Nullable()
					.WithColumn("isdefault").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("active").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsString(128).Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsString(128).Nullable()
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().NotNullable().WithDefaultValue(0);

				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_ratecards_department ON ratecards (departmentid, isdeleted);");
			}

			if (!Schema.Table("ratecarditems").Exists())
			{
				Create.Table("ratecarditems")
					.WithColumn("ratecarditemid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("ratecardid").AsString(36).NotNullable()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("itemtype").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("name").AsString(200).NotNullable()
					.WithColumn("description").AsString(int.MaxValue).Nullable()
					.WithColumn("rate").AsDecimal(18, 4).NotNullable().WithDefaultValue(0)
					.WithColumn("unitlabel").AsString(50).Nullable()
					.WithColumn("minimumminutes").AsInt32().Nullable()
					.WithColumn("roundingminutes").AsInt32().Nullable()
					.WithColumn("minimumcharge").AsDecimal(18, 4).Nullable()
					.WithColumn("unittypefilter").AsString(100).Nullable()
					.WithColumn("personnelroleidfilter").AsInt32().Nullable()
					.WithColumn("taxable").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("sortorder").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("active").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsString(128).Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsString(128).Nullable()
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().NotNullable().WithDefaultValue(0);

				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_ratecarditems_ratecard ON ratecarditems (ratecardid, isdeleted, sortorder);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_ratecarditems_department ON ratecarditems (departmentid);");
			}

			if (!Schema.Table("departmentbillingidentities").Exists())
			{
				Create.Table("departmentbillingidentities")
					.WithColumn("departmentid").AsInt32().NotNullable().PrimaryKey()
					.WithColumn("legalbusinessname").AsString(300).Nullable()
					.WithColumn("remittoaddressid").AsInt32().Nullable()
					.WithColumn("taxregistrationnumber").AsString(100).Nullable()
					.WithColumn("secondarytaxregistrationnumber").AsString(100).Nullable()
					.WithColumn("samuei").AsString(50).Nullable()
					.WithColumn("cagecode").AsString(20).Nullable()
					.WithColumn("workerscompaccountnumber").AsString(100).Nullable()
					.WithColumn("invoicefootertext").AsString(int.MaxValue).Nullable()
					.WithColumn("onlinepaymentsenabled").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("defaultpaymentconnectionid").AsString(36).Nullable()
					.WithColumn("allowedpaymentmethodscsv").AsString(200).Nullable()
					.WithColumn("paylinkexpirydays").AsInt32().NotNullable().WithDefaultValue(30)
					.WithColumn("showpayonlineondocuments").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("updatedon").AsDateTime2().NotNullable()
					.WithColumn("updatedbyuserid").AsString(128).Nullable();
			}
		}

		public override void Down()
		{
			if (Schema.Table("departmentbillingidentities").Exists()) Delete.Table("departmentbillingidentities");
			if (Schema.Table("ratecarditems").Exists()) Delete.Table("ratecarditems");
			if (Schema.Table("ratecards").Exists()) Delete.Table("ratecards");
			if (Schema.Table("customerbillingprofiles").Exists()) Delete.Table("customerbillingprofiles");
		}
	}
}
