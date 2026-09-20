using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0222 (Workforce &amp; Business Operations plan, Phase E). Same number, lower-case identifiers, guarded for safe retry.
	/// </summary>
	[Migration(222)]
	public class M0222_AddResourceAndFieldCostingPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("resourcecostprofiles").Exists())
			{
				Create.Table("resourcecostprofiles")
					.WithColumn("resourcecostprofileid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("subjecttype").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("unitid").AsInt32().Nullable()
					.WithColumn("inventoryassetid").AsString(36).Nullable()
					.WithColumn("externalresourcekey").AsString(100).Nullable()
					.WithColumn("name").AsString(250).Nullable()
					.WithColumn("effectiveon").AsDateTime2().NotNullable()
					.WithColumn("expireson").AsDateTime2().Nullable()
					.WithColumn("currency").AsString(3).Nullable()
					.WithColumn("acquisitioncost").AsDecimal(18,2).Nullable()
					.WithColumn("acquisitiondate").AsDateTime2().Nullable()
					.WithColumn("inservicedate").AsDateTime2().Nullable()
					.WithColumn("salvagevalue").AsDecimal(18,2).Nullable()
					.WithColumn("depreciationmethod").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("allocationbasis").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("usefullifequantity").AsDecimal(18,2).Nullable()
					.WithColumn("usefullifemonths").AsInt32().Nullable()
					.WithColumn("expectedannualutilization").AsDecimal(18,2).Nullable()
					.WithColumn("source").AsString(100).Nullable()
					.WithColumn("isapproved").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("approvedbyuserid").AsString(128).Nullable()
					.WithColumn("approvedon").AsDateTime2().Nullable()
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsString(128).Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsString(128).Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_resourcecostprofiles_department ON resourcecostprofiles (departmentid, isdeleted);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_resourcecostprofiles_unit ON resourcecostprofiles (unitid);");
			}
			if (!Schema.Table("resourcecostcomponents").Exists())
			{
				Create.Table("resourcecostcomponents")
					.WithColumn("resourcecostcomponentid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("resourcecostprofileid").AsString(36).Nullable()
					.WithColumn("effectiveon").AsDateTime2().Nullable()
					.WithColumn("expireson").AsDateTime2().Nullable()
					.WithColumn("category").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("basis").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("rate").AsDecimal(18,4).Nullable()
					.WithColumn("consumptionquantity").AsDecimal(18,4).Nullable()
					.WithColumn("consumptionunit").AsString(20).Nullable()
					.WithColumn("unitprice").AsDecimal(18,4).Nullable()
					.WithColumn("source").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("sourcewindowstart").AsDateTime2().Nullable()
					.WithColumn("sourcewindowend").AsDateTime2().Nullable()
					.WithColumn("sourcemeterstart").AsDecimal(18,2).Nullable()
					.WithColumn("sourcemeterend").AsDecimal(18,2).Nullable()
					.WithColumn("isapproved").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsString(128).Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsString(128).Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_resourcecostcomponents_profile ON resourcecostcomponents (resourcecostprofileid);");
			}
			if (!Schema.Table("resourceusageentries").Exists())
			{
				Create.Table("resourceusageentries")
					.WithColumn("resourceusageentryid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("subjecttype").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("unitid").AsInt32().Nullable()
					.WithColumn("inventoryassetid").AsString(36).Nullable()
					.WithColumn("externalresourcekey").AsString(100).Nullable()
					.WithColumn("callid").AsInt32().Nullable()
					.WithColumn("deploymentid").AsString(36).Nullable()
					.WithColumn("deploymenttimereportid").AsString(36).Nullable()
					.WithColumn("usagedate").AsDateTime2().NotNullable()
					.WithColumn("phase").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("startodometer").AsDecimal(18,2).Nullable()
					.WithColumn("endodometer").AsDecimal(18,2).Nullable()
					.WithColumn("distanceunit").AsString(5).Nullable()
					.WithColumn("originaldistance").AsDecimal(18,2).Nullable()
					.WithColumn("canonicaldistancemiles").AsDecimal(18,2).Nullable()
					.WithColumn("startenginemeter").AsDecimal(18,2).Nullable()
					.WithColumn("endenginemeter").AsDecimal(18,2).Nullable()
					.WithColumn("enginehours").AsDecimal(18,2).Nullable()
					.WithColumn("operatinghours").AsDecimal(18,2).Nullable()
					.WithColumn("idlehours").AsDecimal(18,2).Nullable()
					.WithColumn("deployeddays").AsDecimal(9,2).Nullable()
					.WithColumn("standbydays").AsDecimal(9,2).Nullable()
					.WithColumn("fuelquantity").AsDecimal(18,2).Nullable()
					.WithColumn("fuelunit").AsString(10).Nullable()
					.WithColumn("fuelactualcost").AsDecimal(18,2).Nullable()
					.WithColumn("source").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("externalid").AsString(200).Nullable()
					.WithColumn("isapproved").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("needsreview").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("reviewreason").AsString(500).Nullable()
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsString(128).Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsString(128).Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_resourceusageentries_deployment ON resourceusageentries (deploymentid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_resourceusageentries_call ON resourceusageentries (callid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_resourceusageentries_unit ON resourceusageentries (unitid, usagedate);");
			}
			if (!Schema.Table("fieldcostruns").Exists())
			{
				Create.Table("fieldcostruns")
					.WithColumn("fieldcostrunid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("contexttype").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("bidid").AsString(36).Nullable()
					.WithColumn("callid").AsInt32().Nullable()
					.WithColumn("deploymentid").AsString(36).Nullable()
					.WithColumn("runtype").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("throughdate").AsDateTime2().Nullable()
					.WithColumn("currency").AsString(3).Nullable()
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("inputversions").AsCustom("text").Nullable()
					.WithColumn("revenuesource").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("revenueamount").AsDecimal(18,2).Nullable()
					.WithColumn("revenuesourceid").AsString(36).Nullable()
					.WithColumn("revenuesourceversion").AsString(50).Nullable()
					.WithColumn("personneltotal").AsDecimal(18,2).NotNullable().WithDefaultValue(0)
					.WithColumn("resourcetotal").AsDecimal(18,2).NotNullable().WithDefaultValue(0)
					.WithColumn("consumabletotal").AsDecimal(18,2).NotNullable().WithDefaultValue(0)
					.WithColumn("expensetotal").AsDecimal(18,2).NotNullable().WithDefaultValue(0)
					.WithColumn("overheadtotal").AsDecimal(18,2).NotNullable().WithDefaultValue(0)
					.WithColumn("totalloadedcost").AsDecimal(18,2).NotNullable().WithDefaultValue(0)
					.WithColumn("contributionmargin").AsDecimal(18,2).Nullable()
					.WithColumn("contributionmarginpercent").AsDecimal(9,4).Nullable()
					.WithColumn("breakevenrevenue").AsDecimal(18,2).NotNullable().WithDefaultValue(0)
					.WithColumn("missinginputcount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("supersedesrunid").AsString(36).Nullable()
					.WithColumn("frozenbyuserid").AsString(128).Nullable()
					.WithColumn("frozenon").AsDateTime2().Nullable()
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsString(128).Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsString(128).Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_fieldcostruns_department ON fieldcostruns (departmentid, isdeleted, status);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_fieldcostruns_deployment ON fieldcostruns (deploymentid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_fieldcostruns_bid ON fieldcostruns (bidid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_fieldcostruns_call ON fieldcostruns (callid);");
			}
			if (!Schema.Table("fieldcostlines").Exists())
			{
				Create.Table("fieldcostlines")
					.WithColumn("fieldcostlineid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("fieldcostrunid").AsString(36).Nullable()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("linedate").AsDateTime2().Nullable()
					.WithColumn("category").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("subjecttype").AsString(30).Nullable()
					.WithColumn("subjectid").AsString(128).Nullable()
					.WithColumn("subjectlabel").AsString(250).Nullable()
					.WithColumn("component").AsString(100).Nullable()
					.WithColumn("quantity").AsDecimal(18,4).NotNullable().WithDefaultValue(0)
					.WithColumn("unit").AsString(20).Nullable()
					.WithColumn("rate").AsDecimal(18,4).Nullable()
					.WithColumn("amount").AsDecimal(18,2).NotNullable().WithDefaultValue(0)
					.WithColumn("protecteddetailjson").AsCustom("text").Nullable()
					.WithColumn("sourcetype").AsString(50).Nullable()
					.WithColumn("sourceid").AsString(128).Nullable()
					.WithColumn("isestimated").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("isfallback").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("reviewreason").AsString(250).Nullable()
					.WithColumn("sortorder").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsString(128).Nullable()
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_fieldcostlines_run ON fieldcostlines (fieldcostrunid, sortorder);");
			}
		}

		public override void Down()
		{
			if (Schema.Table("fieldcostlines").Exists()) Delete.Table("fieldcostlines");
			if (Schema.Table("fieldcostruns").Exists()) Delete.Table("fieldcostruns");
			if (Schema.Table("resourceusageentries").Exists()) Delete.Table("resourceusageentries");
			if (Schema.Table("resourcecostcomponents").Exists()) Delete.Table("resourcecostcomponents");
			if (Schema.Table("resourcecostprofiles").Exists()) Delete.Table("resourcecostprofiles");
		}
	}
}
