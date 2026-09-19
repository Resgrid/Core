using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0219 (Workforce &amp; Business Operations plan, Phase C). Same number, lower-case identifiers, guarded for safe retry.
	/// </summary>
	[Migration(219)]
	public class M0219_ExtendInvoicingAndAddCostRecoveryProfilesPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("invoices").Column("servicecontractid").Exists())
				Alter.Table("invoices").AddColumn("servicecontractid").AsString(36).Nullable();
			if (!Schema.Table("invoices").Column("deploymentid").Exists())
				Alter.Table("invoices").AddColumn("deploymentid").AsString(36).Nullable();
			if (!Schema.Table("invoicelineitems").Column("deploymenttimereportid").Exists())
				Alter.Table("invoicelineitems").AddColumn("deploymenttimereportid").AsString(36).Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_invoices_deployment ON invoices (deploymentid) WHERE deploymentid IS NOT NULL;");

			if (!Schema.Table("caloesmarsagencyprofiles").Exists())
			{
				Create.Table("caloesmarsagencyprofiles")
					.WithColumn("caloesmarsagencyprofileid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("authorityprofilecode").AsString(50).NotNullable()
					.WithColumn("macsdesignator").AsString(20).Nullable()
					.WithColumn("agencyname").AsString(250).Nullable()
					.WithColumn("agencycategory").AsString(50).Nullable()
					.WithColumn("contactname").AsCustom("text").Nullable()
					.WithColumn("contactphone").AsCustom("text").Nullable()
					.WithColumn("contactemail").AsCustom("text").Nullable()
					.WithColumn("address").AsCustom("text").Nullable()
					.WithColumn("feinreference").AsCustom("text").Nullable()
					.WithColumn("ueireference").AsCustom("text").Nullable()
					.WithColumn("samreference").AsCustom("text").Nullable()
					.WithColumn("fiscalsupplierreference").AsCustom("text").Nullable()
					.WithColumn("portalaccountrole").AsString(50).Nullable()
					.WithColumn("portalaccountreference").AsString(200).Nullable()
					.WithColumn("verifiedon").AsDateTime2().Nullable()
					.WithColumn("verifiedbyuserid").AsString(128).Nullable()
					.WithColumn("isactive").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("sourceartifact").AsCustom("text").Nullable()
					.WithColumn("sourcechecksum").AsString(128).Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsString(128).Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsString(128).Nullable()
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().Nullable();
				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_caloesmarsagencyprofiles_department ON caloesmarsagencyprofiles (departmentid) WHERE isdeleted = FALSE;");
			}
			if (!Schema.Table("caloesmarsresourceprofiles").Exists())
			{
				Create.Table("caloesmarsresourceprofiles")
					.WithColumn("caloesmarsresourceprofileid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("subjecttype").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("unitid").AsInt32().Nullable()
					.WithColumn("inventoryassetid").AsString(36).Nullable()
					.WithColumn("externalresourcename").AsString(250).Nullable()
					.WithColumn("marsresourceid").AsString(100).Nullable()
					.WithColumn("resourcetype").AsString(100).Nullable()
					.WithColumn("resourcekind").AsString(100).Nullable()
					.WithColumn("codescheme").AsString(50).Nullable()
					.WithColumn("unitdesignator").AsString(100).Nullable()
					.WithColumn("licenseplate").AsCustom("text").Nullable()
					.WithColumn("vin").AsCustom("text").Nullable()
					.WithColumn("serialnumber").AsCustom("text").Nullable()
					.WithColumn("ownership").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("effectiveon").AsDateTime2().Nullable()
					.WithColumn("expireson").AsDateTime2().Nullable()
					.WithColumn("observedexternalstatus").AsString(50).Nullable()
					.WithColumn("observedon").AsDateTime2().Nullable()
					.WithColumn("reviewstate").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("sourceartifact").AsCustom("text").Nullable()
					.WithColumn("sourcechecksum").AsString(128).Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsString(128).Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsString(128).Nullable()
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_caloesmarsresourceprofiles_department ON caloesmarsresourceprofiles (departmentid, isdeleted);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_caloesmarsresourceprofiles_unit ON caloesmarsresourceprofiles (unitid);");
			}
			if (!Schema.Table("caloesmarsrateprofiles").Exists())
			{
				Create.Table("caloesmarsrateprofiles")
					.WithColumn("caloesmarsrateprofileid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("submissionyear").AsInt32().NotNullable()
					.WithColumn("submissiontype").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("effectiveon").AsDateTime2().Nullable()
					.WithColumn("expireson").AsDateTime2().Nullable()
					.WithColumn("baserateaccepted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("administrativeratemethod").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("administrativeratevalue").AsDecimal(9,4).Nullable()
					.WithColumn("authorityprofilecode").AsString(50).NotNullable()
					.WithColumn("sourceurl").AsCustom("text").Nullable()
					.WithColumn("sourcedate").AsDateTime2().Nullable()
					.WithColumn("signedon").AsDateTime2().Nullable()
					.WithColumn("signedbyname").AsCustom("text").Nullable()
					.WithColumn("observedexternalstatus").AsString(50).Nullable()
					.WithColumn("observedon").AsDateTime2().Nullable()
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("sourceartifact").AsCustom("text").Nullable()
					.WithColumn("sourcechecksum").AsString(128).Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsString(128).Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsString(128).Nullable()
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_caloesmarsrateprofiles_department ON caloesmarsrateprofiles (departmentid, submissionyear, isdeleted);");
			}
			if (!Schema.Table("caloesmarsratelines").Exists())
			{
				Create.Table("caloesmarsratelines")
					.WithColumn("caloesmarsratelineid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("caloesmarsrateprofileid").AsString(36).NotNullable()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("linekind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("classificationcode").AsString(100).Nullable()
					.WithColumn("resourcecode").AsString(100).Nullable()
					.WithColumn("femacode").AsString(50).Nullable()
					.WithColumn("description").AsCustom("text").Nullable()
					.WithColumn("basis").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("straightrate").AsDecimal(18,4).Nullable()
					.WithColumn("overtimerate").AsDecimal(18,4).Nullable()
					.WithColumn("includesworkerscomp").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("includesunemploymentinsurance").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("portaltoportaleligible").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("overtimeeligible").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("authority").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("sourceinputversions").AsCustom("text").Nullable()
					.WithColumn("observedexternalstatus").AsString(50).Nullable()
					.WithColumn("sortorder").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("sourceartifact").AsCustom("text").Nullable()
					.WithColumn("sourcechecksum").AsString(128).Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsString(128).Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsString(128).Nullable()
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_caloesmarsratelines_profile ON caloesmarsratelines (caloesmarsrateprofileid, sortorder);");
				Create.ForeignKey("fk_caloesmarsratelines_profile").FromTable("caloesmarsratelines").ForeignColumn("caloesmarsrateprofileid").ToTable("caloesmarsrateprofiles").PrimaryColumn("caloesmarsrateprofileid");
			}
			if (!Schema.Table("caloesmarsadministrativerateinputs").Exists())
			{
				Create.Table("caloesmarsadministrativerateinputs")
					.WithColumn("caloesmarsadministrativerateinputid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("caloesmarsrateprofileid").AsString(36).NotNullable()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("fiscalyear").AsInt32().NotNullable()
					.WithColumn("functioncode").AsString(50).Nullable()
					.WithColumn("categorycode").AsString(50).Nullable()
					.WithColumn("categoryprofileversion").AsString(50).Nullable()
					.WithColumn("classification").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("actualamount").AsCustom("text").Nullable()
					.WithColumn("sourcesystem").AsString(100).Nullable()
					.WithColumn("sourceline").AsString(200).Nullable()
					.WithColumn("inputversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("incidentdirectexclusion").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("doublecountmarker").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("reviewstatus").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("reviewreason").AsCustom("text").Nullable()
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("sourceartifact").AsCustom("text").Nullable()
					.WithColumn("sourcechecksum").AsString(128).Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsString(128).Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsString(128).Nullable()
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_caloesmarsadministrativerateinputs_profile ON caloesmarsadministrativerateinputs (caloesmarsrateprofileid);");
				Create.ForeignKey("fk_caloesmarsadministrativerateinputs_profile").FromTable("caloesmarsadministrativerateinputs").ForeignColumn("caloesmarsrateprofileid").ToTable("caloesmarsrateprofiles").PrimaryColumn("caloesmarsrateprofileid");
			}
			if (!Schema.Table("caloesmarsagreementsnapshots").Exists())
			{
				Create.Table("caloesmarsagreementsnapshots")
					.WithColumn("caloesmarsagreementsnapshotid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("classificationcode").AsString(100).Nullable()
					.WithColumn("classificationtitle").AsString(250).Nullable()
					.WithColumn("documentkind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("compensationmethod").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("overtimemethod").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("starton").AsDateTime2().Nullable()
					.WithColumn("endon").AsDateTime2().Nullable()
					.WithColumn("externalapprovalstatus").AsString(50).Nullable()
					.WithColumn("observedon").AsDateTime2().Nullable()
					.WithColumn("attachmentid").AsInt32().Nullable()
					.WithColumn("attachmentchecksum").AsString(128).Nullable()
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("sourceartifact").AsCustom("text").Nullable()
					.WithColumn("sourcechecksum").AsString(128).Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsString(128).Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsString(128).Nullable()
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_caloesmarsagreementsnapshots_department ON caloesmarsagreementsnapshots (departmentid, isdeleted, starton);");
			}
			if (!Schema.Table("caloesmarsworkitems").Exists())
			{
				Create.Table("caloesmarsworkitems")
					.WithColumn("caloesmarsworkitemid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("deploymentid").AsString(36).Nullable()
					.WithColumn("rmsexternalorderid").AsString(36).Nullable()
					.WithColumn("rmsexternalorderfillid").AsString(36).Nullable()
					.WithColumn("recordtype").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("localstate").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("marsrecordid").AsString(100).Nullable()
					.WithColumn("marsinvoiceid").AsString(100).Nullable()
					.WithColumn("observedexternalstatus").AsString(50).Nullable()
					.WithColumn("observedon").AsDateTime2().Nullable()
					.WithColumn("observedsource").AsString(50).Nullable()
					.WithColumn("correctioncomment").AsCustom("text").Nullable()
					.WithColumn("authorityprofilecode").AsString(50).Nullable()
					.WithColumn("rateprofileversion").AsString(50).Nullable()
					.WithColumn("agreementsnapshotid").AsString(36).Nullable()
					.WithColumn("snapshotjson").AsCustom("text").Nullable()
					.WithColumn("validationsummaryjson").AsCustom("text").Nullable()
					.WithColumn("submittedbyuserid").AsString(128).Nullable()
					.WithColumn("submittedon").AsDateTime2().Nullable()
					.WithColumn("approvedbyuserid").AsString(128).Nullable()
					.WithColumn("approvedon").AsDateTime2().Nullable()
					.WithColumn("rejectedbyuserid").AsString(128).Nullable()
					.WithColumn("rejectedon").AsDateTime2().Nullable()
					.WithColumn("paidon").AsDateTime2().Nullable()
					.WithColumn("expectedtotal").AsDecimal(18,2).Nullable()
					.WithColumn("approvedtotal").AsDecimal(18,2).Nullable()
					.WithColumn("paidtotal").AsDecimal(18,2).Nullable()
					.WithColumn("paymentreference").AsCustom("text").Nullable()
					.WithColumn("supersedesworkitemid").AsString(36).Nullable()
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("sourceartifact").AsCustom("text").Nullable()
					.WithColumn("sourcechecksum").AsString(128).Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsString(128).Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsString(128).Nullable()
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_caloesmarsworkitems_department ON caloesmarsworkitems (departmentid, isdeleted, localstate);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_caloesmarsworkitems_deployment ON caloesmarsworkitems (deploymentid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_caloesmarsworkitems_external ON caloesmarsworkitems (marsrecordid);");
			}
			if (!Schema.Table("caloesmarsreimbursementlines").Exists())
			{
				Create.Table("caloesmarsreimbursementlines")
					.WithColumn("caloesmarsreimbursementlineid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("caloesmarsworkitemid").AsString(36).NotNullable()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("deploymentid").AsString(36).Nullable()
					.WithColumn("linedate").AsDateTime2().Nullable()
					.WithColumn("linekind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("subjecttype").AsInt32().Nullable()
					.WithColumn("subjectid").AsString(128).Nullable()
					.WithColumn("sourceworkid").AsString(36).Nullable()
					.WithColumn("sourceexpenseid").AsString(36).Nullable()
					.WithColumn("quantity").AsDecimal(18,4).NotNullable().WithDefaultValue(0)
					.WithColumn("unit").AsString(20).Nullable()
					.WithColumn("rate").AsDecimal(18,4).NotNullable().WithDefaultValue(0)
					.WithColumn("ratelineid").AsString(36).Nullable()
					.WithColumn("ratelineversion").AsInt32().Nullable()
					.WithColumn("expectedamount").AsDecimal(18,2).NotNullable().WithDefaultValue(0)
					.WithColumn("approvedamount").AsDecimal(18,2).Nullable()
					.WithColumn("paidamount").AsDecimal(18,2).Nullable()
					.WithColumn("eligibilitystate").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("eligibilityreason").AsCustom("text").Nullable()
					.WithColumn("sourceversions").AsCustom("text").Nullable()
					.WithColumn("sortorder").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsString(128).Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_caloesmarsreimbursementlines_workitem ON caloesmarsreimbursementlines (caloesmarsworkitemid, sortorder);");
				Create.ForeignKey("fk_caloesmarsreimbursementlines_workitem").FromTable("caloesmarsreimbursementlines").ForeignColumn("caloesmarsworkitemid").ToTable("caloesmarsworkitems").PrimaryColumn("caloesmarsworkitemid");
			}

			Execute.Sql("INSERT INTO featureflags (flagkey, name, description, category, isenabledglobally) SELECT 'Operations.Deployments', 'Deployment finance', 'Free deployment finance wrapper around a Call: roster, daily time reports, expenses, attachments, manifests and the RMS external-order link (Workforce & Business Operations plan, Phase C). Independent of the Business Operations add-on. Seeded off.', 'Business', FALSE WHERE NOT EXISTS (SELECT 1 FROM featureflags WHERE flagkey = 'Operations.Deployments');");
			Execute.Sql("INSERT INTO featureflags (flagkey, name, description, category, isenabledglobally) SELECT 'Invoicing.ContractorBilling', 'Contractor billing', 'Rate schedules, service contracts, bids, bid conversion, charge calculation and invoice generation from deployments (Workforce & Business Operations plan, Phase C). Requires Business.Operations. Seeded off.', 'Business', FALSE WHERE NOT EXISTS (SELECT 1 FROM featureflags WHERE flagkey = 'Invoicing.ContractorBilling');");
			Execute.Sql("INSERT INTO featureflagprerequisites (featureflagid, requiredfeatureflagid, requiredvalue) SELECT f.featureflagid, r.featureflagid, NULL FROM featureflags f CROSS JOIN featureflags r WHERE f.flagkey = 'Invoicing.ContractorBilling' AND r.flagkey = 'Business.Operations' AND NOT EXISTS (SELECT 1 FROM featureflagprerequisites p WHERE p.featureflagid = f.featureflagid AND p.requiredfeatureflagid = r.featureflagid);");
			Execute.Sql("INSERT INTO featureflags (flagkey, name, description, category, isenabledglobally) SELECT 'CostRecovery.CalOesMars', 'Cal OES MARS cost recovery', 'California Fire Assistance Agreement cost-recovery preparation and reconciliation for the Cal OES Mutual Aid Reimbursement System (Workforce & Business Operations plan, Phase C11). Manual portal handoff only; requires Business.Operations. Seeded off.', 'Business', FALSE WHERE NOT EXISTS (SELECT 1 FROM featureflags WHERE flagkey = 'CostRecovery.CalOesMars');");
			Execute.Sql("INSERT INTO featureflagprerequisites (featureflagid, requiredfeatureflagid, requiredvalue) SELECT f.featureflagid, r.featureflagid, NULL FROM featureflags f CROSS JOIN featureflags r WHERE f.flagkey = 'CostRecovery.CalOesMars' AND r.flagkey = 'Business.Operations' AND NOT EXISTS (SELECT 1 FROM featureflagprerequisites p WHERE p.featureflagid = f.featureflagid AND p.requiredfeatureflagid = r.featureflagid);");
		}

		public override void Down()
		{
			Execute.Sql("DROP TABLE IF EXISTS caloesmarsreimbursementlines;");
			Execute.Sql("DROP TABLE IF EXISTS caloesmarsworkitems;");
			Execute.Sql("DROP TABLE IF EXISTS caloesmarsagreementsnapshots;");
			Execute.Sql("DROP TABLE IF EXISTS caloesmarsadministrativerateinputs;");
			Execute.Sql("DROP TABLE IF EXISTS caloesmarsratelines;");
			Execute.Sql("DROP TABLE IF EXISTS caloesmarsrateprofiles;");
			Execute.Sql("DROP TABLE IF EXISTS caloesmarsresourceprofiles;");
			Execute.Sql("DROP TABLE IF EXISTS caloesmarsagencyprofiles;");
			Execute.Sql("ALTER TABLE invoicelineitems DROP COLUMN IF EXISTS deploymenttimereportid;");
			Execute.Sql("ALTER TABLE invoices DROP COLUMN IF EXISTS deploymentid;");
			Execute.Sql("ALTER TABLE invoices DROP COLUMN IF EXISTS servicecontractid;");
		}
	}
}
