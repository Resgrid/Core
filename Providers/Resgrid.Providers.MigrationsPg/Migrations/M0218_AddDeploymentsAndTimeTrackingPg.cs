using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0218 (Workforce &amp; Business Operations plan, Phase C). Same number, lower-case identifiers, guarded for safe retry.
	/// </summary>
	[Migration(218)]
	public class M0218_AddDeploymentsAndTimeTrackingPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("deployments").Exists())
			{
				Create.Table("deployments")
					.WithColumn("deploymentid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("callid").AsInt32().Nullable()
					.WithColumn("rmsexternalorderid").AsString(36).Nullable()
					.WithColumn("financemode").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("bidid").AsString(36).Nullable()
					.WithColumn("servicecontractid").AsString(36).Nullable()
					.WithColumn("ratescheduleid").AsString(36).Nullable()
					.WithColumn("contactid").AsString(128).Nullable()
					.WithColumn("name").AsString(250).NotNullable()
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("incidentnumber").AsString(100).Nullable()
					.WithColumn("servicerequestnumber").AsString(100).Nullable()
					.WithColumn("resourceordernumber").AsString(100).Nullable()
					.WithColumn("requestnumber").AsString(100).Nullable()
					.WithColumn("costcode").AsString(100).Nullable()
					.WithColumn("pointofhire").AsString(250).Nullable()
					.WithColumn("starton").AsDateTime2().Nullable()
					.WithColumn("endon").AsDateTime2().Nullable()
					.WithColumn("maxdays").AsInt32().Nullable()
					.WithColumn("outofprovince").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("travelviaair").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("homecountry").AsString(2).Nullable()
					.WithColumn("hostcountry").AsString(2).Nullable()
					.WithColumn("homesubdivision").AsString(10).Nullable()
					.WithColumn("hostsubdivision").AsString(10).Nullable()
					.WithColumn("localtimezoneid").AsString(100).Nullable()
					.WithColumn("locale").AsString(20).Nullable()
					.WithColumn("measurementsystem").AsString(10).Nullable()
					.WithColumn("currency").AsString(3).Nullable()
					.WithColumn("discountpercent").AsDecimal(9,4).Nullable()
					.WithColumn("statuschangedon").AsDateTime2().Nullable()
					.WithColumn("calendaritemid").AsInt32().Nullable()
					.WithColumn("notes").AsCustom("text").Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsString(128).Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsString(128).Nullable()
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_deployments_department ON deployments (departmentid, isdeleted, status);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_deployments_externalorder ON deployments (rmsexternalorderid);");
				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_deployments_call ON deployments (callid) WHERE callid IS NOT NULL AND isdeleted = FALSE;");
			}
			if (!Schema.Table("deploymentunits").Exists())
			{
				Create.Table("deploymentunits")
					.WithColumn("deploymentunitid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("deploymentid").AsString(36).NotNullable()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("unitid").AsInt32().NotNullable()
					.WithColumn("ratescheduleentryid").AsString(36).Nullable()
					.WithColumn("callsign").AsString(100).Nullable()
					.WithColumn("notes").AsCustom("text").Nullable()
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("removedon").AsDateTime2().Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_deploymentunits_deployment ON deploymentunits (deploymentid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_deploymentunits_unit ON deploymentunits (unitid);");
				Create.ForeignKey("fk_deploymentunits_deployment").FromTable("deploymentunits").ForeignColumn("deploymentid").ToTable("deployments").PrimaryColumn("deploymentid");
			}
			if (!Schema.Table("deploymentpersonnel").Exists())
			{
				Create.Table("deploymentpersonnel")
					.WithColumn("deploymentpersonnelid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("deploymentid").AsString(36).NotNullable()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("deploymentunitid").AsString(36).Nullable()
					.WithColumn("userid").AsString(128).NotNullable()
					.WithColumn("unitroleid").AsInt32().Nullable()
					.WithColumn("ratescheduleentryid").AsString(36).Nullable()
					.WithColumn("certificationcode").AsString(50).Nullable()
					.WithColumn("premiumidsjson").AsCustom("text").Nullable()
					.WithColumn("callsign").AsString(100).Nullable()
					.WithColumn("rmsexternalorderfillid").AsString(36).Nullable()
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("removedon").AsDateTime2().Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_deploymentpersonnel_deployment ON deploymentpersonnel (deploymentid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_deploymentpersonnel_user ON deploymentpersonnel (userid, removedon);");
				Create.ForeignKey("fk_deploymentpersonnel_deployment").FromTable("deploymentpersonnel").ForeignColumn("deploymentid").ToTable("deployments").PrimaryColumn("deploymentid");
			}
			if (!Schema.Table("deploymentequipment").Exists())
			{
				Create.Table("deploymentequipment")
					.WithColumn("deploymentequipmentid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("deploymentid").AsString(36).NotNullable()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("deploymentunitid").AsString(36).Nullable()
					.WithColumn("inventoryassetid").AsString(36).Nullable()
					.WithColumn("inventoryitemid").AsString(36).Nullable()
					.WithColumn("freetextname").AsString(250).Nullable()
					.WithColumn("ratescheduleentryid").AsString(36).Nullable()
					.WithColumn("issuedon").AsDateTime2().Nullable()
					.WithColumn("returnedon").AsDateTime2().Nullable()
					.WithColumn("notes").AsCustom("text").Nullable()
					.WithColumn("addedon").AsDateTime2().NotNullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_deploymentequipment_deployment ON deploymentequipment (deploymentid);");
				Create.ForeignKey("fk_deploymentequipment_deployment").FromTable("deploymentequipment").ForeignColumn("deploymentid").ToTable("deployments").PrimaryColumn("deploymentid");
			}
			if (!Schema.Table("deploymenttimereports").Exists())
			{
				Create.Table("deploymenttimereports")
					.WithColumn("deploymenttimereportid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("deploymentid").AsString(36).NotNullable()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("reportnumber").AsInt32().NotNullable()
					.WithColumn("reportdate").AsDate().NotNullable()
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("incidentnumber").AsString(100).Nullable()
					.WithColumn("resourceordernumber").AsString(100).Nullable()
					.WithColumn("requestnumber").AsString(100).Nullable()
					.WithColumn("costcode").AsString(100).Nullable()
					.WithColumn("pointofhire").AsString(250).Nullable()
					.WithColumn("noclear8").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("unsafeconditionsstanddown").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("contractorsignedbyuserid").AsString(128).Nullable()
					.WithColumn("contractorsignedon").AsDateTime2().Nullable()
					.WithColumn("customersignername").AsCustom("text").Nullable()
					.WithColumn("customersignedon").AsDateTime2().Nullable()
					.WithColumn("submittedbyuserid").AsString(128).Nullable()
					.WithColumn("submittedon").AsDateTime2().Nullable()
					.WithColumn("approvedbyuserid").AsString(128).Nullable()
					.WithColumn("approvedon").AsDateTime2().Nullable()
					.WithColumn("invoiceid").AsString(36).Nullable()
					.WithColumn("rmsexternalorderfillid").AsString(36).Nullable()
					.WithColumn("notes").AsCustom("text").Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsString(128).Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsString(128).Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_deploymenttimereports_deployment ON deploymenttimereports (deploymentid, isdeleted, reportdate);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_deploymenttimereports_department ON deploymenttimereports (departmentid, status);");
				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_deploymenttimereports_number ON deploymenttimereports (departmentid, reportnumber);");
				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_deploymenttimereports_date ON deploymenttimereports (deploymentid, reportdate) WHERE isdeleted = FALSE AND status <> 4;");
				Create.ForeignKey("fk_deploymenttimereports_deployment").FromTable("deploymenttimereports").ForeignColumn("deploymentid").ToTable("deployments").PrimaryColumn("deploymentid");
			}
			if (!Schema.Table("deploymenttimeentries").Exists())
			{
				Create.Table("deploymenttimeentries")
					.WithColumn("deploymenttimeentryid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("deploymenttimereportid").AsString(36).NotNullable()
					.WithColumn("deploymentid").AsString(36).NotNullable()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("subjecttype").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("deploymentpersonnelid").AsString(36).Nullable()
					.WithColumn("deploymentunitid").AsString(36).Nullable()
					.WithColumn("deploymentequipmentid").AsString(36).Nullable()
					.WithColumn("entrytype").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("starttime").AsDateTime2().NotNullable()
					.WithColumn("endtime").AsDateTime2().NotNullable()
					.WithColumn("paidbreakminutes").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("unpaidbreakminutes").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("crewsizesnapshot").AsInt32().Nullable()
					.WithColumn("certificationcode").AsString(50).Nullable()
					.WithColumn("mileagekm").AsDecimal(9,2).Nullable()
					.WithColumn("fueldeductionlitres").AsDecimal(9,2).Nullable()
					.WithColumn("agencysuppliedmeals").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("agencysuppliedaccommodation").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("notes").AsCustom("text").Nullable()
					.WithColumn("sortorder").AsInt32().NotNullable().WithDefaultValue(0);
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_deploymenttimeentries_report ON deploymenttimeentries (deploymenttimereportid, sortorder);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_deploymenttimeentries_deployment ON deploymenttimeentries (deploymentid);");
				Create.ForeignKey("fk_deploymenttimeentries_report").FromTable("deploymenttimeentries").ForeignColumn("deploymenttimereportid").ToTable("deploymenttimereports").PrimaryColumn("deploymenttimereportid");
			}
			if (!Schema.Table("deploymentexpenses").Exists())
			{
				Create.Table("deploymentexpenses")
					.WithColumn("deploymentexpenseid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("deploymentid").AsString(36).NotNullable()
					.WithColumn("deploymenttimereportid").AsString(36).Nullable()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("expensedate").AsDateTime2().NotNullable()
					.WithColumn("expensetype").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("mealcode").AsString(10).Nullable()
					.WithColumn("city").AsString(200).Nullable()
					.WithColumn("description").AsCustom("text").Nullable()
					.WithColumn("amount").AsDecimal(18,2).NotNullable().WithDefaultValue(0)
					.WithColumn("currency").AsString(3).Nullable()
					.WithColumn("preapproved").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("billable").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("receiptattachmentid").AsInt32().Nullable()
					.WithColumn("rmsexternalorderfillid").AsString(36).Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsString(128).Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsString(128).Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_deploymentexpenses_deployment ON deploymentexpenses (deploymentid, isdeleted, expensedate);");
				Create.ForeignKey("fk_deploymentexpenses_deployment").FromTable("deploymentexpenses").ForeignColumn("deploymentid").ToTable("deployments").PrimaryColumn("deploymentid");
			}
			if (!Schema.Table("deploymentattachments").Exists())
			{
				Create.Table("deploymentattachments")
					.WithColumn("deploymentattachmentid").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("deploymentid").AsString(36).NotNullable()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("attachmenttype").AsInt32().NotNullable().WithDefaultValue(5)
					.WithColumn("name").AsString(250).Nullable()
					.WithColumn("filename").AsCustom("text").Nullable()
					.WithColumn("filetype").AsString(200).Nullable()
					.WithColumn("filesize").AsInt32().Nullable()
					.WithColumn("data").AsCustom("bytea").Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsString(128).Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_deploymentattachments_deployment ON deploymentattachments (deploymentid, isdeleted);");
				Create.ForeignKey("fk_deploymentattachments_deployment").FromTable("deploymentattachments").ForeignColumn("deploymentid").ToTable("deployments").PrimaryColumn("deploymentid");
			}
			if (!Schema.Table("timereportnumbersequences").Exists())
			{
				Create.Table("timereportnumbersequences")
					.WithColumn("departmentid").AsInt32().NotNullable().PrimaryKey()
					.WithColumn("nextreportnumber").AsInt32().NotNullable().WithDefaultValue(1);
			}
		}

		public override void Down()
		{
			Execute.Sql("DROP TABLE IF EXISTS timereportnumbersequences;");
			Execute.Sql("DROP TABLE IF EXISTS deploymentattachments;");
			Execute.Sql("DROP TABLE IF EXISTS deploymentexpenses;");
			Execute.Sql("DROP TABLE IF EXISTS deploymenttimeentries;");
			Execute.Sql("DROP TABLE IF EXISTS deploymenttimereports;");
			Execute.Sql("DROP TABLE IF EXISTS deploymentequipment;");
			Execute.Sql("DROP TABLE IF EXISTS deploymentpersonnel;");
			Execute.Sql("DROP TABLE IF EXISTS deploymentunits;");
			Execute.Sql("DROP TABLE IF EXISTS deployments;");
		}
	}
}
