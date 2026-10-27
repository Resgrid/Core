using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Incident Back Office LI-C Business, continued (plan §9.14, §9.16, §9.26, §9.27): the claim case ledger, the closeout
	/// gate, disaster declarations, recovery projects and deadlines, and the evidence intake inbox with its files (ADP catalog
	/// 36). PostgreSQL twin: lower-case identifiers and every string column citext (plan §11.2), existence-guarded.
	/// </summary>
	[Migration(296)]
	public class M0296_AddIncidentClaimsRecoveryAndIntakePg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("incidentclaims").Exists())
			{
				Create.Table("incidentclaims")
					.WithColumn("incidentclaimid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("claimnumber").AsCustom("citext").NotNullable()
					.WithColumn("kind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("claimantname").AsCustom("citext").NotNullable()
					.WithColumn("incidentparticipantid").AsCustom("citext").Nullable()
					.WithColumn("claimantcontactid").AsCustom("citext").Nullable()
					.WithColumn("responsibleunit").AsCustom("citext").Nullable()
					.WithColumn("assigneduserid").AsCustom("citext").Nullable()
					.WithColumn("deadlineon").AsDate().Nullable()
					.WithColumn("recordid").AsCustom("citext").Nullable()
					.WithColumn("currency").AsCustom("citext").NotNullable()
					.WithColumn("openedon").AsDateTime2().NotNullable()
					.WithColumn("closedon").AsDateTime2().Nullable()
					.WithColumn("deadlinenotifiedon").AsDateTime2().Nullable()
					.WithColumn("financialsjson").AsCustom("citext").Nullable()
					.WithColumn("casenotes").AsCustom("citext").Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().Nullable()
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentclaims_activation ON incidentclaims (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentclaims_deadline ON incidentclaims (departmentid, incidentactivationid, deadlineon);");
				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_incidentclaims_number ON incidentclaims (departmentid, claimnumber) WHERE isdeleted = FALSE;");
				Execute.Sql("DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_incidentclaims_currency') THEN ALTER TABLE incidentclaims ADD CONSTRAINT ck_incidentclaims_currency CHECK (char_length(currency) <= 3); END IF; END $$;");
			}

			if (!Schema.Table("incidentcloseoutchecklistitems").Exists())
			{
				Create.Table("incidentcloseoutchecklistitems")
					.WithColumn("incidentcloseoutchecklistitemid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
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
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentcloseoutchecklistitems_activation ON incidentcloseoutchecklistitems (departmentid, incidentactivationid);");
			}

			if (!Schema.Table("incidentdisasterdeclarations").Exists())
			{
				Create.Table("incidentdisasterdeclarations")
					.WithColumn("incidentdisasterdeclarationid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("declarationnumber").AsCustom("citext").NotNullable()
					.WithColumn("program").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("state").AsCustom("citext").Nullable()
					.WithColumn("incidentperiodstart").AsDate().Nullable()
					.WithColumn("incidentperiodend").AsDate().Nullable()
					.WithColumn("eligiblecategories").AsCustom("citext").Nullable()
					.WithColumn("applicantid").AsCustom("citext").Nullable()
					.WithColumn("federalsharepercent").AsDecimal(9, 4).Nullable()
					.WithColumn("nonfederalsharepercent").AsDecimal(9, 4).Nullable()
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("eligibilitypolicyversion").AsCustom("citext").Nullable()
					.WithColumn("ratescheduleyear").AsInt32().Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentdisasterdeclarations_activation ON incidentdisasterdeclarations (departmentid, incidentactivationid);");
			}

			if (!Schema.Table("incidentrecoveryprojects").Exists())
			{
				Create.Table("incidentrecoveryprojects")
					.WithColumn("incidentrecoveryprojectid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("incidentdisasterdeclarationid").AsCustom("citext").NotNullable()
					.WithColumn("title").AsCustom("citext").NotNullable()
					.WithColumn("category").AsCustom("citext").NotNullable()
					.WithColumn("incidentfacilityid").AsCustom("citext").Nullable()
					.WithColumn("contactid").AsCustom("citext").Nullable()
					.WithColumn("location").AsCustom("citext").Nullable()
					.WithColumn("externalprojectnumber").AsCustom("citext").Nullable()
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentrecoveryprojects_activation ON incidentrecoveryprojects (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentrecoveryprojects_declaration ON incidentrecoveryprojects (departmentid, incidentdisasterdeclarationid);");
			}

			if (!Schema.Table("incidentrecoverydeadlines").Exists())
			{
				Create.Table("incidentrecoverydeadlines")
					.WithColumn("incidentrecoverydeadlineid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("incidentdisasterdeclarationid").AsCustom("citext").NotNullable()
					.WithColumn("incidentrecoveryprojectid").AsCustom("citext").Nullable()
					.WithColumn("kind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("dueon").AsDate().NotNullable()
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("notifiedon").AsDateTime2().Nullable()
					.WithColumn("notes").AsCustom("citext").Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentrecoverydeadlines_activation ON incidentrecoverydeadlines (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentrecoverydeadlines_due ON incidentrecoverydeadlines (departmentid, dueon);");
			}

			if (!Schema.Table("incidentintakeitems").Exists())
			{
				Create.Table("incidentintakeitems")
					.WithColumn("incidentintakeitemid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("channel").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("senderkind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("senderreferenceid").AsCustom("citext").Nullable()
					.WithColumn("receivedon").AsDateTime2().NotNullable()
					.WithColumn("filecount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("targettype").AsCustom("citext").Nullable()
					.WithColumn("targetid").AsCustom("citext").Nullable()
					.WithColumn("evidenceartifactid").AsCustom("citext").Nullable()
					.WithColumn("filedon").AsDateTime2().Nullable()
					.WithColumn("filedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("providermessageid").AsCustom("citext").Nullable()
					.WithColumn("rejectreason").AsCustom("citext").Nullable()
					.WithColumn("lookslikeshiftticket").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("senderaddress").AsCustom("citext").Nullable()
					.WithColumn("subject").AsCustom("citext").Nullable()
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().Nullable()
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentintakeitems_activation ON incidentintakeitems (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentintakeitems_status ON incidentintakeitems (departmentid, incidentactivationid, status);");
				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_incidentintakeitems_message ON incidentintakeitems (departmentid, providermessageid) WHERE providermessageid IS NOT NULL;");
			}

			if (!Schema.Table("incidentintakefiles").Exists())
			{
				Create.Table("incidentintakefiles")
					.WithColumn("incidentintakefileid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("incidentintakeitemid").AsCustom("citext").NotNullable()
					.WithColumn("filename").AsCustom("citext").NotNullable()
					.WithColumn("contenttype").AsCustom("citext").NotNullable()
					.WithColumn("size").AsInt64().NotNullable().WithDefaultValue(0)
					.WithColumn("data").AsCustom("bytea").NotNullable()
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentintakefiles_activation ON incidentintakefiles (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentintakefiles_item ON incidentintakefiles (departmentid, incidentintakeitemid);");
			}
		}

		public override void Down()
		{
			if (Schema.Table("incidentintakefiles").Exists())
				Delete.Table("incidentintakefiles");
			if (Schema.Table("incidentintakeitems").Exists())
				Delete.Table("incidentintakeitems");
			if (Schema.Table("incidentrecoverydeadlines").Exists())
				Delete.Table("incidentrecoverydeadlines");
			if (Schema.Table("incidentrecoveryprojects").Exists())
				Delete.Table("incidentrecoveryprojects");
			if (Schema.Table("incidentdisasterdeclarations").Exists())
				Delete.Table("incidentdisasterdeclarations");
			if (Schema.Table("incidentcloseoutchecklistitems").Exists())
				Delete.Table("incidentcloseoutchecklistitems");
			if (Schema.Table("incidentclaims").Exists())
				Delete.Table("incidentclaims");
		}
	}
}
