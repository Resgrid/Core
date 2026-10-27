using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Incident Back Office LI-B Logistics, resources (plan §9.3–§9.5, §12.2): resources, components, status history,
	/// movements, timeout extensions, mission packages, requests with approvals, fills and tracked ETA updates, receipts,
	/// check-ins and demob gates; plus the three additive ResourceOrders columns (IncidentActivationId,
	/// IncidentResourceRequestId, OriginSystem). PostgreSQL twin: lower-case identifiers and every string column citext (plan
	/// §11.2), existence-guarded.
	/// </summary>
	[Migration(292)]
	public class M0292_AddIncidentLogisticsResourcesPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("incidentresources").Exists())
			{
				Create.Table("incidentresources")
					.WithColumn("incidentresourceid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("kind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("name").AsCustom("citext").NotNullable()
					.WithColumn("resourcetype").AsCustom("citext").Nullable()
					.WithColumn("capability").AsCustom("citext").Nullable()
					.WithColumn("typingcode").AsCustom("citext").Nullable()
					.WithColumn("provideragency").AsCustom("citext").Nullable()
					.WithColumn("payingagency").AsCustom("citext").Nullable()
					.WithColumn("orderingauthority").AsCustom("citext").Nullable()
					.WithColumn("ordernumber").AsCustom("citext").Nullable()
					.WithColumn("requestnumber").AsCustom("citext").Nullable()
					.WithColumn("homelocation").AsCustom("citext").Nullable()
					.WithColumn("leadername").AsCustom("citext").Nullable()
					.WithColumn("leaderparticipantid").AsCustom("citext").Nullable()
					.WithColumn("personnelcount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("apparatuscount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("unitid").AsInt32().Nullable()
					.WithColumn("incidentadhocunitid").AsCustom("citext").Nullable()
					.WithColumn("incidentrequestfillid").AsCustom("citext").Nullable()
					.WithColumn("incidentmissionpackageid").AsCustom("citext").Nullable()
					.WithColumn("incidentvendorengagementid").AsCustom("citext").Nullable()
					.WithColumn("supportstatus").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("currentresourceassignmentid").AsCustom("citext").Nullable()
					.WithColumn("paymentclass").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("arrivedon").AsDateTime2().Nullable()
					.WithColumn("checkedinon").AsDateTime2().Nullable()
					.WithColumn("assignmentstarton").AsDate().Nullable()
					.WithColumn("maxassignmentdays").AsInt32().Nullable()
					.WithColumn("timeouton").AsDate().Nullable()
					.WithColumn("timeoutbasis").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("tentativereleaseon").AsDateTime2().Nullable()
					.WithColumn("releasedon").AsDateTime2().Nullable()
					.WithColumn("departedon").AsDateTime2().Nullable()
					.WithColumn("destination").AsCustom("citext").Nullable()
					.WithColumn("notes").AsCustom("citext").Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentresources_activation ON incidentresources (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentresources_status ON incidentresources (departmentid, incidentactivationid, supportstatus);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentresources_timeout ON incidentresources (departmentid, incidentactivationid, timeouton);");
				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_incidentresources_adhoc ON incidentresources (departmentid, incidentactivationid, incidentadhocunitid) WHERE isdeleted = FALSE AND incidentadhocunitid IS NOT NULL;");
			}

			if (!Schema.Table("incidentresourcecomponents").Exists())
			{
				Create.Table("incidentresourcecomponents")
					.WithColumn("incidentresourcecomponentid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("incidentresourceid").AsCustom("citext").NotNullable()
					.WithColumn("componentresourceid").AsCustom("citext").Nullable()
					.WithColumn("incidentparticipantid").AsCustom("citext").Nullable()
					.WithColumn("kind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("name").AsCustom("citext").NotNullable()
					.WithColumn("quantity").AsDecimal(18, 4).NotNullable().WithDefaultValue(0)
					.WithColumn("notes").AsCustom("citext").Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentresourcecomponents_activation ON incidentresourcecomponents (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentresourcecomponents_resource ON incidentresourcecomponents (departmentid, incidentresourceid);");
			}

			if (!Schema.Table("incidentresourcestatushistory").Exists())
			{
				Create.Table("incidentresourcestatushistory")
					.WithColumn("incidentresourcestatushistoryid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("subjecttype").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("subjectid").AsCustom("citext").NotNullable()
					.WithColumn("oldstatus").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("newstatus").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("reason").AsCustom("citext").Nullable()
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentresourcestatushistory_activation ON incidentresourcestatushistory (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentresourcestatushistory_subject ON incidentresourcestatushistory (departmentid, subjectid, addedon);");
			}

			if (!Schema.Table("incidentresourcemovements").Exists())
			{
				Create.Table("incidentresourcemovements")
					.WithColumn("incidentresourcemovementid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("incidentresourceid").AsCustom("citext").NotNullable()
					.WithColumn("kind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("fromlocation").AsCustom("citext").Nullable()
					.WithColumn("tolocation").AsCustom("citext").Nullable()
					.WithColumn("departedon").AsDateTime2().Nullable()
					.WithColumn("arrivedon").AsDateTime2().Nullable()
					.WithColumn("etaon").AsDateTime2().Nullable()
					.WithColumn("notes").AsCustom("citext").Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentresourcemovements_activation ON incidentresourcemovements (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentresourcemovements_resource ON incidentresourcemovements (departmentid, incidentresourceid);");
			}

			if (!Schema.Table("incidentassignmentextensions").Exists())
			{
				Create.Table("incidentassignmentextensions")
					.WithColumn("incidentassignmentextensionid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("subjecttype").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("subjectid").AsCustom("citext").NotNullable()
					.WithColumn("priortimeouton").AsDate().Nullable()
					.WithColumn("newtimeouton").AsDate().NotNullable()
					.WithColumn("approvedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("reason").AsCustom("citext").NotNullable()
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentassignmentextensions_activation ON incidentassignmentextensions (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentassignmentextensions_subject ON incidentassignmentextensions (departmentid, subjectid);");
			}

			if (!Schema.Table("incidentmissionpackages").Exists())
			{
				Create.Table("incidentmissionpackages")
					.WithColumn("incidentmissionpackageid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").Nullable()
					.WithColumn("name").AsCustom("citext").NotNullable()
					.WithColumn("code").AsCustom("citext").Nullable()
					.WithColumn("kind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("description").AsCustom("citext").Nullable()
					.WithColumn("componentsjson").AsCustom("citext").Nullable()
					.WithColumn("istemplate").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("sourcetemplateid").AsCustom("citext").Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentmissionpackages_department ON incidentmissionpackages (departmentid);");
			}

			if (!Schema.Table("incidentmissionpackageestimates").Exists())
			{
				Create.Table("incidentmissionpackageestimates")
					.WithColumn("incidentmissionpackageestimateid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentmissionpackageid").AsCustom("citext").NotNullable()
					.WithColumn("category").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("quantity").AsDecimal(18, 4).NotNullable().WithDefaultValue(0)
					.WithColumn("unit").AsCustom("citext").Nullable()
					.WithColumn("amount").AsDecimal(18, 2).Nullable()
					.WithColumn("currency").AsCustom("citext").Nullable()
					.WithColumn("notes").AsCustom("citext").Nullable()
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentmissionpackageestimates_department ON incidentmissionpackageestimates (departmentid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentmissionpackageestimates_package ON incidentmissionpackageestimates (departmentid, incidentmissionpackageid);");
				Execute.Sql("DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_incidentmissionpackageestimates_currency') THEN ALTER TABLE incidentmissionpackageestimates ADD CONSTRAINT ck_incidentmissionpackageestimates_currency CHECK (char_length(currency) <= 3); END IF; END $$;");
			}

			if (!Schema.Table("incidentresourcerequests").Exists())
			{
				Create.Table("incidentresourcerequests")
					.WithColumn("incidentresourcerequestid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("incidentoperationalperiodid").AsCustom("citext").Nullable()
					.WithColumn("requestnumber").AsCustom("citext").NotNullable()
					.WithColumn("islocallyissued").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("incidentneedid").AsCustom("citext").Nullable()
					.WithColumn("incidentobjectiveid").AsCustom("citext").Nullable()
					.WithColumn("priority").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("substatedetailid").AsInt32().Nullable()
					.WithColumn("kind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("resourcetype").AsCustom("citext").Nullable()
					.WithColumn("capability").AsCustom("citext").Nullable()
					.WithColumn("quantity").AsDecimal(18, 4).NotNullable().WithDefaultValue(0)
					.WithColumn("unitofmeasure").AsCustom("citext").Nullable()
					.WithColumn("substitutions").AsCustom("citext").Nullable()
					.WithColumn("justification").AsCustom("citext").Nullable()
					.WithColumn("needby").AsDateTime2().Nullable()
					.WithColumn("deliverylocation").AsCustom("citext").Nullable()
					.WithColumn("reportinglocation").AsCustom("citext").Nullable()
					.WithColumn("receivingcontact").AsCustom("citext").Nullable()
					.WithColumn("fundingcode").AsCustom("citext").Nullable()
					.WithColumn("incidentbudgetlineid").AsCustom("citext").Nullable()
					.WithColumn("owningsection").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("sourcechannel").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("requestedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("submittedon").AsDateTime2().Nullable()
					.WithColumn("approvedon").AsDateTime2().Nullable()
					.WithColumn("approvedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("orderedon").AsDateTime2().Nullable()
					.WithColumn("orderedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("rejectedreason").AsCustom("citext").Nullable()
					.WithColumn("cancelledreason").AsCustom("citext").Nullable()
					.WithColumn("closedon").AsDateTime2().Nullable()
					.WithColumn("resourceorderid").AsInt32().Nullable()
					.WithColumn("incidentvendorengagementid").AsCustom("citext").Nullable()
					.WithColumn("notes").AsCustom("citext").Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentresourcerequests_activation ON incidentresourcerequests (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentresourcerequests_status ON incidentresourcerequests (departmentid, incidentactivationid, status);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentresourcerequests_need ON incidentresourcerequests (departmentid, incidentneedid);");
				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_incidentresourcerequests_number ON incidentresourcerequests (departmentid, requestnumber) WHERE isdeleted = FALSE;");
			}

			if (!Schema.Table("incidentrequestapprovals").Exists())
			{
				Create.Table("incidentrequestapprovals")
					.WithColumn("incidentrequestapprovalid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("incidentresourcerequestid").AsCustom("citext").NotNullable()
					.WithColumn("step").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("decision").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("comment").AsCustom("citext").Nullable()
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentrequestapprovals_activation ON incidentrequestapprovals (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentrequestapprovals_request ON incidentrequestapprovals (departmentid, incidentresourcerequestid);");
			}

			if (!Schema.Table("incidentrequestfills").Exists())
			{
				Create.Table("incidentrequestfills")
					.WithColumn("incidentrequestfillid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("incidentresourcerequestid").AsCustom("citext").NotNullable()
					.WithColumn("source").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("sourcereference").AsCustom("citext").Nullable()
					.WithColumn("provider").AsCustom("citext").Nullable()
					.WithColumn("resourcekind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("resourcetype").AsCustom("citext").Nullable()
					.WithColumn("apparatuscount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("personnelcount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("quantity").AsDecimal(18, 4).NotNullable().WithDefaultValue(0)
					.WithColumn("etaon").AsDateTime2().Nullable()
					.WithColumn("etaconfidence").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("acceptedon").AsDateTime2().Nullable()
					.WithColumn("arrivedon").AsDateTime2().Nullable()
					.WithColumn("resourceorderfillid").AsInt32().Nullable()
					.WithColumn("notes").AsCustom("citext").Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentrequestfills_activation ON incidentrequestfills (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentrequestfills_request ON incidentrequestfills (departmentid, incidentresourcerequestid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentrequestfills_eta ON incidentrequestfills (departmentid, incidentactivationid, etaon);");
			}

			if (!Schema.Table("incidentfilletaupdates").Exists())
			{
				Create.Table("incidentfilletaupdates")
					.WithColumn("incidentfilletaupdateid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("incidentrequestfillid").AsCustom("citext").NotNullable()
					.WithColumn("prioretaon").AsDateTime2().Nullable()
					.WithColumn("newetaon").AsDateTime2().Nullable()
					.WithColumn("confidence").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("reason").AsCustom("citext").Nullable()
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentfilletaupdates_activation ON incidentfilletaupdates (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentfilletaupdates_fill ON incidentfilletaupdates (departmentid, incidentrequestfillid);");
			}

			if (!Schema.Table("incidentreceipts").Exists())
			{
				Create.Table("incidentreceipts")
					.WithColumn("incidentreceiptid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("incidentresourcerequestid").AsCustom("citext").Nullable()
					.WithColumn("incidentrequestfillid").AsCustom("citext").Nullable()
					.WithColumn("receivinglocation").AsCustom("citext").Nullable()
					.WithColumn("shipmentreference").AsCustom("citext").Nullable()
					.WithColumn("carrier").AsCustom("citext").Nullable()
					.WithColumn("receivedon").AsDateTime2().NotNullable()
					.WithColumn("receivedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("discrepancykind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("notes").AsCustom("citext").Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentreceipts_activation ON incidentreceipts (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentreceipts_request ON incidentreceipts (departmentid, incidentresourcerequestid);");
			}

			if (!Schema.Table("incidentreceiptlines").Exists())
			{
				Create.Table("incidentreceiptlines")
					.WithColumn("incidentreceiptlineid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("incidentreceiptid").AsCustom("citext").NotNullable()
					.WithColumn("itemdescription").AsCustom("citext").NotNullable()
					.WithColumn("inventoryitemid").AsCustom("citext").Nullable()
					.WithColumn("inventorytransactionid").AsCustom("citext").Nullable()
					.WithColumn("quantityexpected").AsDecimal(18, 4).NotNullable().WithDefaultValue(0)
					.WithColumn("quantityaccepted").AsDecimal(18, 4).NotNullable().WithDefaultValue(0)
					.WithColumn("quantityrejected").AsDecimal(18, 4).NotNullable().WithDefaultValue(0)
					.WithColumn("quantitybackordered").AsDecimal(18, 4).NotNullable().WithDefaultValue(0)
					.WithColumn("unitofmeasure").AsCustom("citext").Nullable()
					.WithColumn("damage").AsCustom("citext").Nullable()
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentreceiptlines_activation ON incidentreceiptlines (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentreceiptlines_receipt ON incidentreceiptlines (departmentid, incidentreceiptid);");
			}

			if (!Schema.Table("incidentcheckins").Exists())
			{
				Create.Table("incidentcheckins")
					.WithColumn("incidentcheckinid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("subjecttype").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("subjectid").AsCustom("citext").NotNullable()
					.WithColumn("source").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("checkedinon").AsDateTime2().NotNullable()
					.WithColumn("mobilizationorigin").AsCustom("citext").Nullable()
					.WithColumn("actualpersonnelcount").AsInt32().Nullable()
					.WithColumn("vehicledescription").AsCustom("citext").Nullable()
					.WithColumn("reportingpoint").AsCustom("citext").Nullable()
					.WithColumn("firstworkday").AsDate().Nullable()
					.WithColumn("assignmentlengthdays").AsInt32().Nullable()
					.WithColumn("specialrequirements").AsCustom("citext").Nullable()
					.WithColumn("isduplicatecandidate").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("notes").AsCustom("citext").Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentcheckins_activation ON incidentcheckins (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentcheckins_subject ON incidentcheckins (departmentid, subjectid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentcheckins_day ON incidentcheckins (departmentid, incidentactivationid, checkedinon);");
			}

			if (!Schema.Table("incidentdemobplans").Exists())
			{
				Create.Table("incidentdemobplans")
					.WithColumn("incidentdemobplanid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("subjecttype").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("subjectid").AsCustom("citext").NotNullable()
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("tentativereleaseon").AsDateTime2().Nullable()
					.WithColumn("approvedreleaseon").AsDateTime2().Nullable()
					.WithColumn("approvedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("departurewindowstart").AsDateTime2().Nullable()
					.WithColumn("departurewindowend").AsDateTime2().Nullable()
					.WithColumn("destination").AsCustom("citext").Nullable()
					.WithColumn("travelmode").AsCustom("citext").Nullable()
					.WithColumn("reassignmenteligible").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("departedon").AsDateTime2().Nullable()
					.WithColumn("notes").AsCustom("citext").Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentdemobplans_activation ON incidentdemobplans (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentdemobplans_status ON incidentdemobplans (departmentid, incidentactivationid, status);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentdemobplans_subject ON incidentdemobplans (departmentid, subjectid);");
			}

			if (!Schema.Table("incidentdemobchecklistitems").Exists())
			{
				Create.Table("incidentdemobchecklistitems")
					.WithColumn("incidentdemobchecklistitemid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("incidentdemobplanid").AsCustom("citext").NotNullable()
					.WithColumn("category").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("name").AsCustom("citext").NotNullable()
					.WithColumn("ownerposition").AsCustom("citext").Nullable()
					.WithColumn("isblocking").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("completedon").AsDateTime2().Nullable()
					.WithColumn("completedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("waivedreason").AsCustom("citext").Nullable()
					.WithColumn("waivedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentdemobchecklistitems_activation ON incidentdemobchecklistitems (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentdemobchecklistitems_plan ON incidentdemobchecklistitems (departmentid, incidentdemobplanid);");
			}

			// Plan §12.2: the only change to shipped structure. Nullable / defaulted, so no existing row changes and the Orders UI ignores them.
			if (!Schema.Table("resourceorders").Column("incidentactivationid").Exists())
				Alter.Table("resourceorders").AddColumn("incidentactivationid").AsCustom("citext").Nullable();
			if (!Schema.Table("resourceorders").Column("incidentresourcerequestid").Exists())
				Alter.Table("resourceorders").AddColumn("incidentresourcerequestid").AsCustom("citext").Nullable();
			if (!Schema.Table("resourceorders").Column("originsystem").Exists())
				Alter.Table("resourceorders").AddColumn("originsystem").AsInt32().NotNullable().WithDefaultValue(0);
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_resourceorders_incidentactivation ON resourceorders (departmentid, incidentactivationid) WHERE incidentactivationid IS NOT NULL;");
		}

		public override void Down()
		{
			Execute.Sql("DROP INDEX IF EXISTS ix_resourceorders_incidentactivation;");
			if (Schema.Table("resourceorders").Column("originsystem").Exists())
				Delete.Column("originsystem").FromTable("resourceorders");
			if (Schema.Table("resourceorders").Column("incidentresourcerequestid").Exists())
				Delete.Column("incidentresourcerequestid").FromTable("resourceorders");
			if (Schema.Table("resourceorders").Column("incidentactivationid").Exists())
				Delete.Column("incidentactivationid").FromTable("resourceorders");
			if (Schema.Table("incidentdemobchecklistitems").Exists())
				Delete.Table("incidentdemobchecklistitems");
			if (Schema.Table("incidentdemobplans").Exists())
				Delete.Table("incidentdemobplans");
			if (Schema.Table("incidentcheckins").Exists())
				Delete.Table("incidentcheckins");
			if (Schema.Table("incidentreceiptlines").Exists())
				Delete.Table("incidentreceiptlines");
			if (Schema.Table("incidentreceipts").Exists())
				Delete.Table("incidentreceipts");
			if (Schema.Table("incidentfilletaupdates").Exists())
				Delete.Table("incidentfilletaupdates");
			if (Schema.Table("incidentrequestfills").Exists())
				Delete.Table("incidentrequestfills");
			if (Schema.Table("incidentrequestapprovals").Exists())
				Delete.Table("incidentrequestapprovals");
			if (Schema.Table("incidentresourcerequests").Exists())
				Delete.Table("incidentresourcerequests");
			if (Schema.Table("incidentmissionpackageestimates").Exists())
				Delete.Table("incidentmissionpackageestimates");
			if (Schema.Table("incidentmissionpackages").Exists())
				Delete.Table("incidentmissionpackages");
			if (Schema.Table("incidentassignmentextensions").Exists())
				Delete.Table("incidentassignmentextensions");
			if (Schema.Table("incidentresourcemovements").Exists())
				Delete.Table("incidentresourcemovements");
			if (Schema.Table("incidentresourcestatushistory").Exists())
				Delete.Table("incidentresourcestatushistory");
			if (Schema.Table("incidentresourcecomponents").Exists())
				Delete.Table("incidentresourcecomponents");
			if (Schema.Table("incidentresources").Exists())
				Delete.Table("incidentresources");
		}
	}
}
