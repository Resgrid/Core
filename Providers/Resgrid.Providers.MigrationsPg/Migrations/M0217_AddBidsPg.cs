using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0217 (Workforce &amp; Business Operations plan, Phase C). Same number, lower-case identifiers, guarded for safe retry.
	/// </summary>
	[Migration(217)]
	public class M0217_AddBidsPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("bids").Exists())
			{
				Create.Table("bids")
					.WithColumn("bidid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("bidnumber").AsInt32().NotNullable()
					.WithColumn("contactid").AsString(128).NotNullable()
					.WithColumn("customerbillingprofileid").AsString(36).Nullable()
					.WithColumn("servicecontractid").AsString(36).Nullable()
					.WithColumn("ratescheduleid").AsString(36).Nullable()
					.WithColumn("title").AsString(250).NotNullable()
					.WithColumn("description").AsCustom("text").Nullable()
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("validuntil").AsDateTime2().Nullable()
					.WithColumn("requestedstarton").AsDateTime2().Nullable()
					.WithColumn("requestedendon").AsDateTime2().Nullable()
					.WithColumn("incidentnumber").AsString(100).Nullable()
					.WithColumn("deliverylocation").AsCustom("text").Nullable()
					.WithColumn("discountpercent").AsDecimal(9,4).Nullable()
					.WithColumn("estimatedsubtotal").AsDecimal(18,2).NotNullable().WithDefaultValue(0)
					.WithColumn("estimateddiscountamount").AsDecimal(18,2).NotNullable().WithDefaultValue(0)
					.WithColumn("estimatedtaxamount").AsDecimal(18,2).NotNullable().WithDefaultValue(0)
					.WithColumn("estimatedtotal").AsDecimal(18,2).NotNullable().WithDefaultValue(0)
					.WithColumn("notes").AsCustom("text").Nullable()
					.WithColumn("termstext").AsCustom("text").Nullable()
					.WithColumn("senton").AsDateTime2().Nullable()
					.WithColumn("senttoemail").AsString(500).Nullable()
					.WithColumn("acceptedon").AsDateTime2().Nullable()
					.WithColumn("declinedon").AsDateTime2().Nullable()
					.WithColumn("declinereason").AsCustom("text").Nullable()
					.WithColumn("convertedcallid").AsInt32().Nullable()
					.WithColumn("converteddeploymentid").AsString(36).Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsString(128).Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsString(128).Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_bids_department ON bids (departmentid, isdeleted, status);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_bids_contact ON bids (contactid);");
				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_bids_number ON bids (departmentid, bidnumber);");
			}
			if (!Schema.Table("bidlineitems").Exists())
			{
				Create.Table("bidlineitems")
					.WithColumn("bidlineitemid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("bidid").AsString(36).NotNullable()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("ratescheduleentryid").AsString(36).Nullable()
					.WithColumn("linetype").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("description").AsCustom("text").NotNullable()
					.WithColumn("crewsize").AsInt32().Nullable()
					.WithColumn("quantity").AsDecimal(18,4).NotNullable().WithDefaultValue(1)
					.WithColumn("estimatedhoursperday").AsDecimal(9,2).Nullable()
					.WithColumn("estimateddays").AsDecimal(9,2).Nullable()
					.WithColumn("unitrate").AsDecimal(18,4).NotNullable().WithDefaultValue(0)
					.WithColumn("premiumidsjson").AsCustom("text").Nullable()
					.WithColumn("estimatedamount").AsDecimal(18,2).NotNullable().WithDefaultValue(0)
					.WithColumn("taxable").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("sortorder").AsInt32().NotNullable().WithDefaultValue(0);
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_bidlineitems_bid ON bidlineitems (bidid, sortorder);");
				Create.ForeignKey("fk_bidlineitems_bid").FromTable("bidlineitems").ForeignColumn("bidid").ToTable("bids").PrimaryColumn("bidid");
			}
			if (!Schema.Table("bidnumbersequences").Exists())
			{
				Create.Table("bidnumbersequences")
					.WithColumn("departmentid").AsInt32().NotNullable().PrimaryKey()
					.WithColumn("nextbidnumber").AsInt32().NotNullable().WithDefaultValue(1);
			}
		}

		public override void Down()
		{
			Execute.Sql("DROP TABLE IF EXISTS bidnumbersequences;");
			Execute.Sql("DROP TABLE IF EXISTS bidlineitems;");
			Execute.Sql("DROP TABLE IF EXISTS bids;");
		}
	}
}
