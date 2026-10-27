using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Incident Back Office LI-C Business (plan §9.12.2–§9.13, §9.23, §11.4): vendor agreements and payment plans, requisitions
	/// and approvals, commitments, purchase receipts, invoice references and payment packages; budgets, lines, funding
	/// authorizations, spending delegations, funding codes and cost groups; time records with the DTR-import unique constraint,
	/// adjustments, equipment use and expenses; rate snapshots, the cost ledger, accrual snapshots, projections, cost-share
	/// agreements, rules, apportionment runs and allocations (ADP catalog 36). PostgreSQL twin: lower-case identifiers and
	/// every string column citext (plan §11.2), existence-guarded.
	/// </summary>
	[Migration(295)]
	public class M0295_AddIncidentBusinessPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("vendoragreements").Exists())
			{
				Create.Table("vendoragreements")
					.WithColumn("vendoragreementid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("vendorid").AsCustom("citext").NotNullable()
					.WithColumn("agreementtype").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("agreementnumber").AsCustom("citext").Nullable()
					.WithColumn("recordid").AsCustom("citext").Nullable()
					.WithColumn("effectiveon").AsDate().Nullable()
					.WithColumn("expireson").AsDate().Nullable()
					.WithColumn("ceilingamount").AsDecimal(18, 2).Nullable()
					.WithColumn("currency").AsCustom("citext").NotNullable()
					.WithColumn("ratescheduleid").AsCustom("citext").Nullable()
					.WithColumn("ratesjson").AsCustom("citext").Nullable()
					.WithColumn("taxesandfees").AsCustom("citext").Nullable()
					.WithColumn("minimums").AsCustom("citext").Nullable()
					.WithColumn("cancellationterms").AsCustom("citext").Nullable()
					.WithColumn("orderinginstructions").AsCustom("citext").Nullable()
					.WithColumn("inspectionrules").AsCustom("citext").Nullable()
					.WithColumn("paymentterms").AsCustom("citext").Nullable()
					.WithColumn("invoiceroute").AsCustom("citext").Nullable()
					.WithColumn("requireddocuments").AsCustom("citext").Nullable()
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_vendoragreements_department ON vendoragreements (departmentid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_vendoragreements_vendor ON vendoragreements (departmentid, vendorid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_vendoragreements_expiry ON vendoragreements (departmentid, expireson);");
				Execute.Sql("DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_vendoragreements_currency') THEN ALTER TABLE vendoragreements ADD CONSTRAINT ck_vendoragreements_currency CHECK (char_length(currency) <= 3); END IF; END $$;");
			}

			if (!Schema.Table("vendorpaymentplans").Exists())
			{
				Create.Table("vendorpaymentplans")
					.WithColumn("vendorpaymentplanid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("vendoragreementid").AsCustom("citext").Nullable()
					.WithColumn("vendorid").AsCustom("citext").NotNullable()
					.WithColumn("payingagency").AsCustom("citext").Nullable()
					.WithColumn("fundingsource").AsCustom("citext").Nullable()
					.WithColumn("method").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("terms").AsCustom("citext").Nullable()
					.WithColumn("paymentoffice").AsCustom("citext").Nullable()
					.WithColumn("remittancereference").AsCustom("citext").Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().Nullable()
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_vendorpaymentplans_department ON vendorpaymentplans (departmentid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_vendorpaymentplans_vendor ON vendorpaymentplans (departmentid, vendorid);");
			}

			if (!Schema.Table("purchaserequisitions").Exists())
			{
				Create.Table("purchaserequisitions")
					.WithColumn("purchaserequisitionid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("requisitionnumber").AsCustom("citext").NotNullable()
					.WithColumn("incidentresourcerequestid").AsCustom("citext").Nullable()
					.WithColumn("vendorid").AsCustom("citext").Nullable()
					.WithColumn("incidentvendorengagementid").AsCustom("citext").Nullable()
					.WithColumn("incidentbudgetlineid").AsCustom("citext").Nullable()
					.WithColumn("method").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("description").AsCustom("citext").NotNullable()
					.WithColumn("estimatedamount").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
					.WithColumn("currency").AsCustom("citext").NotNullable()
					.WithColumn("justification").AsCustom("citext").Nullable()
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("requestedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("submittedon").AsDateTime2().Nullable()
					.WithColumn("approvedon").AsDateTime2().Nullable()
					.WithColumn("approvedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("conflictofinterestattested").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("issplitpurchasewarning").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("overridereason").AsCustom("citext").Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_purchaserequisitions_activation ON purchaserequisitions (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_purchaserequisitions_status ON purchaserequisitions (departmentid, incidentactivationid, status);");
				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_purchaserequisitions_number ON purchaserequisitions (departmentid, requisitionnumber) WHERE isdeleted = FALSE;");
				Execute.Sql("DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_purchaserequisitions_currency') THEN ALTER TABLE purchaserequisitions ADD CONSTRAINT ck_purchaserequisitions_currency CHECK (char_length(currency) <= 3); END IF; END $$;");
			}

			if (!Schema.Table("purchaseapprovals").Exists())
			{
				Create.Table("purchaseapprovals")
					.WithColumn("purchaseapprovalid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("purchaserequisitionid").AsCustom("citext").NotNullable()
					.WithColumn("step").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("decision").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("thresholdamount").AsDecimal(18, 2).Nullable()
					.WithColumn("comment").AsCustom("citext").Nullable()
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_purchaseapprovals_activation ON purchaseapprovals (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_purchaseapprovals_requisition ON purchaseapprovals (departmentid, purchaserequisitionid);");
			}

			if (!Schema.Table("incidentcommitments").Exists())
			{
				Create.Table("incidentcommitments")
					.WithColumn("incidentcommitmentid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("commitmentnumber").AsCustom("citext").NotNullable()
					.WithColumn("purchaserequisitionid").AsCustom("citext").Nullable()
					.WithColumn("vendorid").AsCustom("citext").Nullable()
					.WithColumn("incidentvendorengagementid").AsCustom("citext").Nullable()
					.WithColumn("vendoragreementid").AsCustom("citext").Nullable()
					.WithColumn("incidentbudgetlineid").AsCustom("citext").Nullable()
					.WithColumn("incidentfundingcodeid").AsCustom("citext").Nullable()
					.WithColumn("method").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("amount").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
					.WithColumn("currency").AsCustom("citext").NotNullable()
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("committedon").AsDateTime2().NotNullable()
					.WithColumn("committedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("inventorypurchaseorderid").AsCustom("citext").Nullable()
					.WithColumn("description").AsCustom("citext").Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentcommitments_activation ON incidentcommitments (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentcommitments_status ON incidentcommitments (departmentid, incidentactivationid, status);");
				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_incidentcommitments_number ON incidentcommitments (departmentid, commitmentnumber) WHERE isdeleted = FALSE;");
				Execute.Sql("DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_incidentcommitments_currency') THEN ALTER TABLE incidentcommitments ADD CONSTRAINT ck_incidentcommitments_currency CHECK (char_length(currency) <= 3); END IF; END $$;");
			}

			if (!Schema.Table("purchasereceipts").Exists())
			{
				Create.Table("purchasereceipts")
					.WithColumn("purchasereceiptid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("incidentcommitmentid").AsCustom("citext").NotNullable()
					.WithColumn("incidentreceiptid").AsCustom("citext").Nullable()
					.WithColumn("acceptedon").AsDateTime2().NotNullable()
					.WithColumn("acceptedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("quantity").AsDecimal(18, 4).Nullable()
					.WithColumn("amount").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
					.WithColumn("currency").AsCustom("citext").NotNullable()
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("notes").AsCustom("citext").Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_purchasereceipts_activation ON purchasereceipts (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_purchasereceipts_commitment ON purchasereceipts (departmentid, incidentcommitmentid);");
				Execute.Sql("DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_purchasereceipts_currency') THEN ALTER TABLE purchasereceipts ADD CONSTRAINT ck_purchasereceipts_currency CHECK (char_length(currency) <= 3); END IF; END $$;");
			}

			if (!Schema.Table("vendorinvoicereferences").Exists())
			{
				Create.Table("vendorinvoicereferences")
					.WithColumn("vendorinvoicereferenceid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("vendorid").AsCustom("citext").NotNullable()
					.WithColumn("incidentcommitmentid").AsCustom("citext").Nullable()
					.WithColumn("invoicenumber").AsCustom("citext").NotNullable()
					.WithColumn("invoicedate").AsDate().Nullable()
					.WithColumn("amount").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
					.WithColumn("currency").AsCustom("citext").NotNullable()
					.WithColumn("receivedon").AsDateTime2().NotNullable()
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("matchvariance").AsDecimal(18, 2).Nullable()
					.WithColumn("exceptionkind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("acceptedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("acceptedon").AsDateTime2().Nullable()
					.WithColumn("evidenceartifactid").AsCustom("citext").Nullable()
					.WithColumn("notes").AsCustom("citext").Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_vendorinvoicereferences_activation ON vendorinvoicereferences (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_vendorinvoicereferences_vendor ON vendorinvoicereferences (departmentid, vendorid, invoicenumber);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_vendorinvoicereferences_status ON vendorinvoicereferences (departmentid, incidentactivationid, status);");
				Execute.Sql("DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_vendorinvoicereferences_currency') THEN ALTER TABLE vendorinvoicereferences ADD CONSTRAINT ck_vendorinvoicereferences_currency CHECK (char_length(currency) <= 3); END IF; END $$;");
			}

			if (!Schema.Table("paymentpackages").Exists())
			{
				Create.Table("paymentpackages")
					.WithColumn("paymentpackageid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("packagenumber").AsCustom("citext").NotNullable()
					.WithColumn("vendorid").AsCustom("citext").Nullable()
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("totalamount").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
					.WithColumn("currency").AsCustom("citext").NotNullable()
					.WithColumn("linecount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("submittedon").AsDateTime2().Nullable()
					.WithColumn("externalreference").AsCustom("citext").Nullable()
					.WithColumn("paidreference").AsCustom("citext").Nullable()
					.WithColumn("paidon").AsDateTime2().Nullable()
					.WithColumn("evidenceartifactid").AsCustom("citext").Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_paymentpackages_activation ON paymentpackages (departmentid, incidentactivationid);");
				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_paymentpackages_number ON paymentpackages (departmentid, packagenumber) WHERE isdeleted = FALSE;");
				Execute.Sql("DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_paymentpackages_currency') THEN ALTER TABLE paymentpackages ADD CONSTRAINT ck_paymentpackages_currency CHECK (char_length(currency) <= 3); END IF; END $$;");
			}

			if (!Schema.Table("paymentpackagelines").Exists())
			{
				Create.Table("paymentpackagelines")
					.WithColumn("paymentpackagelineid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("paymentpackageid").AsCustom("citext").NotNullable()
					.WithColumn("vendorinvoicereferenceid").AsCustom("citext").NotNullable()
					.WithColumn("amount").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
					.WithColumn("currency").AsCustom("citext").NotNullable()
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_paymentpackagelines_activation ON paymentpackagelines (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_paymentpackagelines_package ON paymentpackagelines (departmentid, paymentpackageid);");
				Execute.Sql("DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_paymentpackagelines_currency') THEN ALTER TABLE paymentpackagelines ADD CONSTRAINT ck_paymentpackagelines_currency CHECK (char_length(currency) <= 3); END IF; END $$;");
			}

			if (!Schema.Table("incidentbudgets").Exists())
			{
				Create.Table("incidentbudgets")
					.WithColumn("incidentbudgetid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("name").AsCustom("citext").NotNullable()
					.WithColumn("originalamount").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
					.WithColumn("revisedamount").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
					.WithColumn("currency").AsCustom("citext").NotNullable()
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("recordid").AsCustom("citext").Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentbudgets_activation ON incidentbudgets (departmentid, incidentactivationid);");
				Execute.Sql("DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_incidentbudgets_currency') THEN ALTER TABLE incidentbudgets ADD CONSTRAINT ck_incidentbudgets_currency CHECK (char_length(currency) <= 3); END IF; END $$;");
			}

			if (!Schema.Table("incidentbudgetlines").Exists())
			{
				Create.Table("incidentbudgetlines")
					.WithColumn("incidentbudgetlineid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("incidentbudgetid").AsCustom("citext").NotNullable()
					.WithColumn("name").AsCustom("citext").NotNullable()
					.WithColumn("kind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("scopekey").AsCustom("citext").Nullable()
					.WithColumn("incidentfundingcodeid").AsCustom("citext").Nullable()
					.WithColumn("originalamount").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
					.WithColumn("revisedamount").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
					.WithColumn("currency").AsCustom("citext").NotNullable()
					.WithColumn("thresholdpercent").AsDecimal(9, 4).Nullable()
					.WithColumn("flooramount").AsDecimal(18, 2).Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentbudgetlines_activation ON incidentbudgetlines (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentbudgetlines_budget ON incidentbudgetlines (departmentid, incidentbudgetid);");
				Execute.Sql("DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_incidentbudgetlines_currency') THEN ALTER TABLE incidentbudgetlines ADD CONSTRAINT ck_incidentbudgetlines_currency CHECK (char_length(currency) <= 3); END IF; END $$;");
			}

			if (!Schema.Table("incidentfundingauthorizations").Exists())
			{
				Create.Table("incidentfundingauthorizations")
					.WithColumn("incidentfundingauthorizationid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("incidentbudgetlineid").AsCustom("citext").Nullable()
					.WithColumn("frombudgetlineid").AsCustom("citext").Nullable()
					.WithColumn("kind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("amount").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
					.WithColumn("currency").AsCustom("citext").NotNullable()
					.WithColumn("authorizedon").AsDateTime2().NotNullable()
					.WithColumn("recordid").AsCustom("citext").Nullable()
					.WithColumn("reason").AsCustom("citext").Nullable()
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentfundingauthorizations_activation ON incidentfundingauthorizations (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentfundingauthorizations_line ON incidentfundingauthorizations (departmentid, incidentbudgetlineid);");
				Execute.Sql("DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_incidentfundingauthorizations_currency') THEN ALTER TABLE incidentfundingauthorizations ADD CONSTRAINT ck_incidentfundingauthorizations_currency CHECK (char_length(currency) <= 3); END IF; END $$;");
			}

			if (!Schema.Table("incidentspendingdelegations").Exists())
			{
				Create.Table("incidentspendingdelegations")
					.WithColumn("incidentspendingdelegationid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("positioncode").AsCustom("citext").Nullable()
					.WithColumn("userid").AsCustom("citext").Nullable()
					.WithColumn("action").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("thresholdamount").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
					.WithColumn("currency").AsCustom("citext").NotNullable()
					.WithColumn("incidentfundingcodeid").AsCustom("citext").Nullable()
					.WithColumn("effectiveon").AsDateTime2().NotNullable()
					.WithColumn("expireson").AsDateTime2().Nullable()
					.WithColumn("recordid").AsCustom("citext").Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentspendingdelegations_activation ON incidentspendingdelegations (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentspendingdelegations_user ON incidentspendingdelegations (departmentid, incidentactivationid, userid);");
				Execute.Sql("DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_incidentspendingdelegations_currency') THEN ALTER TABLE incidentspendingdelegations ADD CONSTRAINT ck_incidentspendingdelegations_currency CHECK (char_length(currency) <= 3); END IF; END $$;");
			}

			if (!Schema.Table("incidentfundingcodes").Exists())
			{
				Create.Table("incidentfundingcodes")
					.WithColumn("incidentfundingcodeid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("code").AsCustom("citext").NotNullable()
					.WithColumn("kind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("description").AsCustom("citext").Nullable()
					.WithColumn("payingagency").AsCustom("citext").Nullable()
					.WithColumn("effectiveon").AsDate().Nullable()
					.WithColumn("expireson").AsDate().Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentfundingcodes_activation ON incidentfundingcodes (departmentid, incidentactivationid);");
			}

			if (!Schema.Table("incidentcostgroups").Exists())
			{
				Create.Table("incidentcostgroups")
					.WithColumn("incidentcostgroupid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("name").AsCustom("citext").NotNullable()
					.WithColumn("code").AsCustom("citext").Nullable()
					.WithColumn("description").AsCustom("citext").Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentcostgroups_activation ON incidentcostgroups (departmentid, incidentactivationid);");
			}

			if (!Schema.Table("incidenttimerecords").Exists())
			{
				Create.Table("incidenttimerecords")
					.WithColumn("incidenttimerecordid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("incidentoperationalperiodid").AsCustom("citext").Nullable()
					.WithColumn("subjecttype").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("incidentparticipantid").AsCustom("citext").Nullable()
					.WithColumn("incidentresourceid").AsCustom("citext").Nullable()
					.WithColumn("incidentpositionassignmentid").AsCustom("citext").Nullable()
					.WithColumn("incidentrecoveryprojectid").AsCustom("citext").Nullable()
					.WithColumn("memberactivationid").AsCustom("citext").Nullable()
					.WithColumn("entrytype").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("starton").AsDateTime2().NotNullable()
					.WithColumn("endon").AsDateTime2().NotNullable()
					.WithColumn("paidbreakminutes").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("unpaidbreakminutes").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("paymentclass").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ratesnapshotid").AsCustom("citext").Nullable()
					.WithColumn("currency").AsCustom("citext").NotNullable()
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("issystemgenerated").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("certifiedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("certifiedon").AsDateTime2().Nullable()
					.WithColumn("approvedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("approvedon").AsDateTime2().Nullable()
					.WithColumn("adjuststimerecordid").AsCustom("citext").Nullable()
					.WithColumn("sourcetype").AsCustom("citext").Nullable()
					.WithColumn("sourceid").AsCustom("citext").Nullable()
					.WithColumn("remarks").AsCustom("citext").Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().Nullable()
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidenttimerecords_activation ON incidenttimerecords (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidenttimerecords_period ON incidenttimerecords (departmentid, incidentactivationid, incidentoperationalperiodid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidenttimerecords_participant ON incidenttimerecords (departmentid, incidentparticipantid, starton);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidenttimerecords_resource ON incidenttimerecords (departmentid, incidentresourceid, starton);");
				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_incidenttimerecords_source ON incidenttimerecords (departmentid, sourcetype, sourceid) WHERE isdeleted = FALSE AND sourcetype IS NOT NULL AND sourceid IS NOT NULL;");
				Execute.Sql("DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_incidenttimerecords_currency') THEN ALTER TABLE incidenttimerecords ADD CONSTRAINT ck_incidenttimerecords_currency CHECK (char_length(currency) <= 3); END IF; END $$;");
			}

			if (!Schema.Table("incidenttimeadjustments").Exists())
			{
				Create.Table("incidenttimeadjustments")
					.WithColumn("incidenttimeadjustmentid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("incidenttimerecordid").AsCustom("citext").NotNullable()
					.WithColumn("adjustingtimerecordid").AsCustom("citext").NotNullable()
					.WithColumn("reason").AsCustom("citext").NotNullable()
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidenttimeadjustments_activation ON incidenttimeadjustments (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidenttimeadjustments_record ON incidenttimeadjustments (departmentid, incidenttimerecordid);");
			}

			if (!Schema.Table("incidentequipmentuserecords").Exists())
			{
				Create.Table("incidentequipmentuserecords")
					.WithColumn("incidentequipmentuserecordid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("incidentoperationalperiodid").AsCustom("citext").Nullable()
					.WithColumn("incidentresourceid").AsCustom("citext").NotNullable()
					.WithColumn("incidentvendorengagementid").AsCustom("citext").Nullable()
					.WithColumn("vendoragreementid").AsCustom("citext").Nullable()
					.WithColumn("shiftdate").AsDate().NotNullable()
					.WithColumn("starton").AsDateTime2().Nullable()
					.WithColumn("endon").AsDateTime2().Nullable()
					.WithColumn("hours").AsDecimal(18, 4).Nullable()
					.WithColumn("mileage").AsDecimal(18, 4).Nullable()
					.WithColumn("useunit").AsCustom("citext").Nullable()
					.WithColumn("quantity").AsDecimal(18, 4).Nullable()
					.WithColumn("workperformed").AsCustom("citext").Nullable()
					.WithColumn("fuelprovided").AsCustom("citext").Nullable()
					.WithColumn("operatorname").AsCustom("citext").Nullable()
					.WithColumn("shiftticketrecordid").AsCustom("citext").Nullable()
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ratesnapshotid").AsCustom("citext").Nullable()
					.WithColumn("currency").AsCustom("citext").NotNullable()
					.WithColumn("incidentrecoveryprojectid").AsCustom("citext").Nullable()
					.WithColumn("sourcetype").AsCustom("citext").Nullable()
					.WithColumn("sourceid").AsCustom("citext").Nullable()
					.WithColumn("certifiedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("certifiedon").AsDateTime2().Nullable()
					.WithColumn("approvedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("approvedon").AsDateTime2().Nullable()
					.WithColumn("adjustsrecordid").AsCustom("citext").Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentequipmentuserecords_activation ON incidentequipmentuserecords (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentequipmentuserecords_resource ON incidentequipmentuserecords (departmentid, incidentresourceid, shiftdate);");
				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_incidentequipmentuserecords_source ON incidentequipmentuserecords (departmentid, sourcetype, sourceid) WHERE isdeleted = FALSE AND sourcetype IS NOT NULL AND sourceid IS NOT NULL;");
				Execute.Sql("DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_incidentequipmentuserecords_currency') THEN ALTER TABLE incidentequipmentuserecords ADD CONSTRAINT ck_incidentequipmentuserecords_currency CHECK (char_length(currency) <= 3); END IF; END $$;");
			}

			if (!Schema.Table("incidentexpenses").Exists())
			{
				Create.Table("incidentexpenses")
					.WithColumn("incidentexpenseid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("incidentoperationalperiodid").AsCustom("citext").Nullable()
					.WithColumn("subjecttype").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("subjectid").AsCustom("citext").Nullable()
					.WithColumn("category").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("incurredon").AsDate().NotNullable()
					.WithColumn("amount").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
					.WithColumn("currency").AsCustom("citext").NotNullable()
					.WithColumn("isreimbursable").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("isbillable").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("vendorid").AsCustom("citext").Nullable()
					.WithColumn("evidenceartifactid").AsCustom("citext").Nullable()
					.WithColumn("incidentrecoveryprojectid").AsCustom("citext").Nullable()
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("approvedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("approvedon").AsDateTime2().Nullable()
					.WithColumn("notes").AsCustom("citext").Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().Nullable()
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentexpenses_activation ON incidentexpenses (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentexpenses_day ON incidentexpenses (departmentid, incidentactivationid, incurredon);");
				Execute.Sql("DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_incidentexpenses_currency') THEN ALTER TABLE incidentexpenses ADD CONSTRAINT ck_incidentexpenses_currency CHECK (char_length(currency) <= 3); END IF; END $$;");
			}

			if (!Schema.Table("incidentratesnapshots").Exists())
			{
				Create.Table("incidentratesnapshots")
					.WithColumn("incidentratesnapshotid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("subjectkind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("subjectid").AsCustom("citext").Nullable()
					.WithColumn("ratescheduleid").AsCustom("citext").Nullable()
					.WithColumn("ratescheduleentryid").AsCustom("citext").Nullable()
					.WithColumn("vendoragreementid").AsCustom("citext").Nullable()
					.WithColumn("description").AsCustom("citext").Nullable()
					.WithColumn("amount").AsDecimal(18, 4).Nullable()
					.WithColumn("unit").AsCustom("citext").NotNullable()
					.WithColumn("currency").AsCustom("citext").NotNullable()
					.WithColumn("effectiveon").AsDate().Nullable()
					.WithColumn("rulesjson").AsCustom("citext").Nullable()
					.WithColumn("personalamount").AsCustom("citext").Nullable()
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().Nullable()
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentratesnapshots_activation ON incidentratesnapshots (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentratesnapshots_subject ON incidentratesnapshots (departmentid, subjectid);");
				Execute.Sql("DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_incidentratesnapshots_currency') THEN ALTER TABLE incidentratesnapshots ADD CONSTRAINT ck_incidentratesnapshots_currency CHECK (char_length(currency) <= 3); END IF; END $$;");
			}

			if (!Schema.Table("incidentcosttransactions").Exists())
			{
				Create.Table("incidentcosttransactions")
					.WithColumn("incidentcosttransactionid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("incidentoperationalperiodid").AsCustom("citext").Nullable()
					.WithColumn("measure").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("category").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("sourcetype").AsCustom("citext").Nullable()
					.WithColumn("sourceid").AsCustom("citext").Nullable()
					.WithColumn("incidentresourceid").AsCustom("citext").Nullable()
					.WithColumn("incidentparticipantid").AsCustom("citext").Nullable()
					.WithColumn("vendorid").AsCustom("citext").Nullable()
					.WithColumn("incidentbudgetlineid").AsCustom("citext").Nullable()
					.WithColumn("incidentfundingcodeid").AsCustom("citext").Nullable()
					.WithColumn("incidentcostgroupid").AsCustom("citext").Nullable()
					.WithColumn("section").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("incidentfacilityid").AsCustom("citext").Nullable()
					.WithColumn("agency").AsCustom("citext").Nullable()
					.WithColumn("incidentrecoveryprojectid").AsCustom("citext").Nullable()
					.WithColumn("memberactivationid").AsCustom("citext").Nullable()
					.WithColumn("costdate").AsDate().NotNullable()
					.WithColumn("quantity").AsDecimal(18, 4).Nullable()
					.WithColumn("unit").AsCustom("citext").Nullable()
					.WithColumn("amount").AsDecimal(18, 2).Nullable()
					.WithColumn("currency").AsCustom("citext").NotNullable()
					.WithColumn("issystemgenerated").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("adjuststransactionid").AsCustom("citext").Nullable()
					.WithColumn("runid").AsCustom("citext").Nullable()
					.WithColumn("protectedamount").AsCustom("citext").Nullable()
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().Nullable()
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentcosttransactions_activation ON incidentcosttransactions (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentcosttransactions_day ON incidentcosttransactions (departmentid, incidentactivationid, costdate, measure);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentcosttransactions_source ON incidentcosttransactions (departmentid, sourcetype, sourceid);");
				Execute.Sql("DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_incidentcosttransactions_currency') THEN ALTER TABLE incidentcosttransactions ADD CONSTRAINT ck_incidentcosttransactions_currency CHECK (char_length(currency) <= 3); END IF; END $$;");
			}

			if (!Schema.Table("incidentaccrualsnapshots").Exists())
			{
				Create.Table("incidentaccrualsnapshots")
					.WithColumn("incidentaccrualsnapshotid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("asofdate").AsDate().NotNullable()
					.WithColumn("measure").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("category").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("amount").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
					.WithColumn("currency").AsCustom("citext").NotNullable()
					.WithColumn("subjectcount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("runid").AsCustom("citext").Nullable()
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentaccrualsnapshots_activation ON incidentaccrualsnapshots (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentaccrualsnapshots_day ON incidentaccrualsnapshots (departmentid, incidentactivationid, asofdate);");
				Execute.Sql("DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_incidentaccrualsnapshots_currency') THEN ALTER TABLE incidentaccrualsnapshots ADD CONSTRAINT ck_incidentaccrualsnapshots_currency CHECK (char_length(currency) <= 3); END IF; END $$;");
			}

			if (!Schema.Table("incidentcostprojections").Exists())
			{
				Create.Table("incidentcostprojections")
					.WithColumn("incidentcostprojectionid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("scenarioname").AsCustom("citext").NotNullable()
					.WithColumn("projectedendon").AsDate().Nullable()
					.WithColumn("dailyburn").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
					.WithColumn("cumulativetodate").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
					.WithColumn("forecastatcompletion").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
					.WithColumn("currency").AsCustom("citext").NotNullable()
					.WithColumn("assumptions").AsCustom("citext").Nullable()
					.WithColumn("runid").AsCustom("citext").Nullable()
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentcostprojections_activation ON incidentcostprojections (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentcostprojections_activation ON incidentcostprojections (departmentid, incidentactivationid, addedon);");
				Execute.Sql("DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_incidentcostprojections_currency') THEN ALTER TABLE incidentcostprojections ADD CONSTRAINT ck_incidentcostprojections_currency CHECK (char_length(currency) <= 3); END IF; END $$;");
			}

			if (!Schema.Table("incidentcostshareagreements").Exists())
			{
				Create.Table("incidentcostshareagreements")
					.WithColumn("incidentcostshareagreementid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("name").AsCustom("citext").NotNullable()
					.WithColumn("version").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("basis").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("effectivefrom").AsDate().Nullable()
					.WithColumn("effectiveto").AsDate().Nullable()
					.WithColumn("recordid").AsCustom("citext").Nullable()
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("partiesjson").AsCustom("citext").Nullable()
					.WithColumn("supersedesagreementid").AsCustom("citext").Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentcostshareagreements_activation ON incidentcostshareagreements (departmentid, incidentactivationid);");
			}

			if (!Schema.Table("incidentcostsharerules").Exists())
			{
				Create.Table("incidentcostsharerules")
					.WithColumn("incidentcostshareruleid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("incidentcostshareagreementid").AsCustom("citext").NotNullable()
					.WithColumn("ruleorder").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("scopekind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("scopevalue").AsCustom("citext").Nullable()
					.WithColumn("method").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("parametersjson").AsCustom("citext").Nullable()
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentcostsharerules_activation ON incidentcostsharerules (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentcostsharerules_agreement ON incidentcostsharerules (departmentid, incidentcostshareagreementid, ruleorder);");
			}

			if (!Schema.Table("incidentapportionmentruns").Exists())
			{
				Create.Table("incidentapportionmentruns")
					.WithColumn("incidentapportionmentrunid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("incidentcostshareagreementid").AsCustom("citext").NotNullable()
					.WithColumn("agreementversion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("runkind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("throughdate").AsDate().NotNullable()
					.WithColumn("inputchecksum").AsCustom("citext").NotNullable()
					.WithColumn("outputchecksum").AsCustom("citext").NotNullable()
					.WithColumn("totalapportioned").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
					.WithColumn("currency").AsCustom("citext").NotNullable()
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentapportionmentruns_activation ON incidentapportionmentruns (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentapportionmentruns_agreement ON incidentapportionmentruns (departmentid, incidentcostshareagreementid);");
				Execute.Sql("DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_incidentapportionmentruns_currency') THEN ALTER TABLE incidentapportionmentruns ADD CONSTRAINT ck_incidentapportionmentruns_currency CHECK (char_length(currency) <= 3); END IF; END $$;");
			}

			if (!Schema.Table("incidentcostallocations").Exists())
			{
				Create.Table("incidentcostallocations")
					.WithColumn("incidentcostallocationid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("incidentapportionmentrunid").AsCustom("citext").NotNullable()
					.WithColumn("incidentcostshareruleid").AsCustom("citext").Nullable()
					.WithColumn("partykey").AsCustom("citext").NotNullable()
					.WithColumn("partyname").AsCustom("citext").NotNullable()
					.WithColumn("category").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("amount").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
					.WithColumn("currency").AsCustom("citext").NotNullable()
					.WithColumn("method").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("adjustsallocationid").AsCustom("citext").Nullable()
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentcostallocations_activation ON incidentcostallocations (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentcostallocations_run ON incidentcostallocations (departmentid, incidentapportionmentrunid);");
				Execute.Sql("DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_incidentcostallocations_currency') THEN ALTER TABLE incidentcostallocations ADD CONSTRAINT ck_incidentcostallocations_currency CHECK (char_length(currency) <= 3); END IF; END $$;");
			}
		}

		public override void Down()
		{
			if (Schema.Table("incidentcostallocations").Exists())
				Delete.Table("incidentcostallocations");
			if (Schema.Table("incidentapportionmentruns").Exists())
				Delete.Table("incidentapportionmentruns");
			if (Schema.Table("incidentcostsharerules").Exists())
				Delete.Table("incidentcostsharerules");
			if (Schema.Table("incidentcostshareagreements").Exists())
				Delete.Table("incidentcostshareagreements");
			if (Schema.Table("incidentcostprojections").Exists())
				Delete.Table("incidentcostprojections");
			if (Schema.Table("incidentaccrualsnapshots").Exists())
				Delete.Table("incidentaccrualsnapshots");
			if (Schema.Table("incidentcosttransactions").Exists())
				Delete.Table("incidentcosttransactions");
			if (Schema.Table("incidentratesnapshots").Exists())
				Delete.Table("incidentratesnapshots");
			if (Schema.Table("incidentexpenses").Exists())
				Delete.Table("incidentexpenses");
			if (Schema.Table("incidentequipmentuserecords").Exists())
				Delete.Table("incidentequipmentuserecords");
			if (Schema.Table("incidenttimeadjustments").Exists())
				Delete.Table("incidenttimeadjustments");
			if (Schema.Table("incidenttimerecords").Exists())
				Delete.Table("incidenttimerecords");
			if (Schema.Table("incidentcostgroups").Exists())
				Delete.Table("incidentcostgroups");
			if (Schema.Table("incidentfundingcodes").Exists())
				Delete.Table("incidentfundingcodes");
			if (Schema.Table("incidentspendingdelegations").Exists())
				Delete.Table("incidentspendingdelegations");
			if (Schema.Table("incidentfundingauthorizations").Exists())
				Delete.Table("incidentfundingauthorizations");
			if (Schema.Table("incidentbudgetlines").Exists())
				Delete.Table("incidentbudgetlines");
			if (Schema.Table("incidentbudgets").Exists())
				Delete.Table("incidentbudgets");
			if (Schema.Table("paymentpackagelines").Exists())
				Delete.Table("paymentpackagelines");
			if (Schema.Table("paymentpackages").Exists())
				Delete.Table("paymentpackages");
			if (Schema.Table("vendorinvoicereferences").Exists())
				Delete.Table("vendorinvoicereferences");
			if (Schema.Table("purchasereceipts").Exists())
				Delete.Table("purchasereceipts");
			if (Schema.Table("incidentcommitments").Exists())
				Delete.Table("incidentcommitments");
			if (Schema.Table("purchaseapprovals").Exists())
				Delete.Table("purchaseapprovals");
			if (Schema.Table("purchaserequisitions").Exists())
				Delete.Table("purchaserequisitions");
			if (Schema.Table("vendorpaymentplans").Exists())
				Delete.Table("vendorpaymentplans");
			if (Schema.Table("vendoragreements").Exists())
				Delete.Table("vendoragreements");
		}
	}
}
