using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Contact pre-incident plans (Contacts plan Phase A, A1; registry M0183, taken as the next physical
	/// number under the no-gaps rule): the 1:1 structured NFPA 1620 pre-plan per Contact and its repeating
	/// typed premise hazards. Both tables carry the ADP IsProtected row marker: the text columns are catalog v12 fields
	/// (ProtectedFieldCatalog.ContactPreplansCatalogVersion) and are enveloped in a protected department. Guarded for safe retry.
	/// </summary>
	[Migration(183)]
	public class M0183_AddContactPreplansPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("contactpreplans").Exists())
			{
				Create.Table("contactpreplans")
					.WithColumn("contactpreplanid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("contactid").AsString(128).NotNullable()
					.WithColumn("constructiontype").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("rooftype").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("occupancytype").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("occupancynotes").AsCustom("text").Nullable()
					.WithColumn("occupancyhours").AsString(500).Nullable()
					.WithColumn("occupantload").AsInt32().Nullable()
					.WithColumn("hasoccupantsneedingassistance").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("occupantsneedingassistancenotes").AsCustom("text").Nullable()
					.WithColumn("gasshutofflocation").AsString(1000).Nullable()
					.WithColumn("electricshutofflocation").AsString(1000).Nullable()
					.WithColumn("watershutofflocation").AsString(1000).Nullable()
					.WithColumn("utilitynotes").AsCustom("text").Nullable()
					.WithColumn("knoxboxlocation").AsString(1000).Nullable()
					.WithColumn("gatecode").AsString(1000).Nullable()
					.WithColumn("alarmpanellocation").AsString(1000).Nullable()
					.WithColumn("alarmcompany").AsString(500).Nullable()
					.WithColumn("alarmcompanyphone").AsString(100).Nullable()
					.WithColumn("accessnotes").AsCustom("text").Nullable()
					.WithColumn("nearesthydrantlocation").AsString(1000).Nullable()
					.WithColumn("requiredfireflowgpm").AsInt32().Nullable()
					.WithColumn("watersupplynotes").AsCustom("text").Nullable()
					.WithColumn("emergencycontactname").AsString(500).Nullable()
					.WithColumn("emergencycontactphone").AsString(100).Nullable()
					.WithColumn("secondarycontactname").AsString(500).Nullable()
					.WithColumn("secondarycontactphone").AsString(100).Nullable()
					.WithColumn("hazmatonsite").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("generalhazardnotes").AsCustom("text").Nullable()
					.WithColumn("tacticalsummary").AsCustom("text").Nullable()
					.WithColumn("lastreviewedon").AsDateTime2().Nullable()
					.WithColumn("reviewedbyuserid").AsString(128).Nullable()
					.WithColumn("nextreviewdue").AsDateTime2().Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsString(128).Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsString(128).Nullable()
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false);

				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_contactpreplans_department ON contactpreplans (departmentid, isdeleted);");
				// One live pre-plan per contact; soft-deleted rows do not block a replacement.
				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_contactpreplans_contact_live ON contactpreplans (contactid) WHERE isdeleted = FALSE;");
			}

			if (!Schema.Table("contactpreplanhazards").Exists())
			{
				Create.Table("contactpreplanhazards")
					.WithColumn("contactpreplanhazardid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("contactpreplanid").AsString(36).NotNullable()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("contactid").AsString(128).NotNullable()
					.WithColumn("hazardtype").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("severity").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("title").AsString(500).NotNullable()
					.WithColumn("description").AsCustom("text").Nullable()
					.WithColumn("locationdescription").AsString(1000).Nullable()
					.WithColumn("gpscoordinates").AsString(100).Nullable()
					.WithColumn("shouldalert").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsString(128).Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsString(128).Nullable()
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false);

				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_contactpreplanhazards_preplan ON contactpreplanhazards (contactpreplanid, isdeleted);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_contactpreplanhazards_contact ON contactpreplanhazards (departmentid, contactid, isdeleted);");
			}
		}

		public override void Down()
		{
			if (Schema.Table("contactpreplanhazards").Exists())
				Delete.Table("contactpreplanhazards");
			if (Schema.Table("contactpreplans").Exists())
				Delete.Table("contactpreplans");
		}
	}
}
