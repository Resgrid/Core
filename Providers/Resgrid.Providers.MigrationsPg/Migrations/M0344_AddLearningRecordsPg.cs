using System.Data;
using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0344_AddLearningRecords: trainingcategories, learningrecords (partial unique idempotency key on
	/// records that are not voided, partial unique verification code), learningrecordfiles, categorycreditmappings
	/// (cascading from trainingcategories) and learningrecordbackfills. Member ids are citext.
	/// </summary>
	[Migration(344)]
	public class M0344_AddLearningRecordsPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("trainingcategories").Exists())
			{
				Create.Table("trainingcategories")
					.WithColumn("trainingcategoryid").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("name").AsString(150).NotNullable()
					.WithColumn("code").AsString(32).Nullable()
					.WithColumn("kind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("sortorder").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("description").AsString(1000).Nullable()
					.WithColumn("targethours").AsDecimal(9, 2).Nullable()
					.WithColumn("targetperiod").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("targetappliesto").AsString(200).Nullable()
					.WithColumn("externalcode").AsString(64).Nullable()
					.WithColumn("retentionyears").AsInt32().Nullable()
					.WithColumn("templatekey").AsString(64).Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("createdbyuserid").AsCustom("citext").Nullable()
					.WithColumn("updatedon").AsDateTime2().Nullable()
					.WithColumn("updatedbyuserid").AsCustom("citext").Nullable();
			}

			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_trainingcategories_department ON trainingcategories (departmentid);");

			if (!Schema.Table("learningrecords").Exists())
			{
				Create.Table("learningrecords")
					.WithColumn("learningrecordid").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("userid").AsCustom("citext").NotNullable()
					.WithColumn("sourcekind").AsInt32().NotNullable()
					.WithColumn("sourcerefid").AsString(128).NotNullable()
					.WithColumn("componentkey").AsString(64).NotNullable().WithDefaultValue("")
					.WithColumn("sessionkey").AsString(128).Nullable()
					.WithColumn("title").AsString(250).NotNullable()
					.WithColumn("coursecode").AsString(100).Nullable()
					.WithColumn("completedon").AsDateTime2().NotNullable()
					.WithColumn("startedon").AsDateTime2().Nullable()
					.WithColumn("endedon").AsDateTime2().Nullable()
					.WithColumn("hours").AsDecimal(9, 2).NotNullable().WithDefaultValue(0)
					.WithColumn("categoryid").AsInt32().Nullable()
					.WithColumn("methodoftraining").AsInt32().Nullable()
					.WithColumn("objectives").AsString(2000).Nullable()
					.WithColumn("location").AsString(250).Nullable()
					.WithColumn("ceprovider").AsString(200).Nullable()
					.WithColumn("cecoursenumber").AsString(100).Nullable()
					.WithColumn("cehours").AsDecimal(9, 2).Nullable()
					.WithColumn("instructoruserids").AsString(1000).Nullable()
					.WithColumn("instructornames").AsString(1000).Nullable()
					.WithColumn("status").AsInt32().NotNullable()
					.WithColumn("supersedesid").AsInt32().Nullable()
					.WithColumn("voidreason").AsString(500).Nullable()
					.WithColumn("voidedon").AsDateTime2().Nullable()
					.WithColumn("voidedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("verifiedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("verifiedon").AsDateTime2().Nullable()
					.WithColumn("reviewnote").AsString(1000).Nullable()
					.WithColumn("evidencefileid").AsInt32().Nullable()
					.WithColumn("isbackfill").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("externalid").AsString(100).Nullable()
					.WithColumn("notes").AsString(4000).Nullable()
					.WithColumn("tags").AsString(500).Nullable()
					.WithColumn("possibleduplicateofid").AsInt32().Nullable()
					.WithColumn("duplicateresolvedon").AsDateTime2().Nullable()
					.WithColumn("duplicateresolution").AsInt32().Nullable()
					.WithColumn("verificationcode").AsString(32).Nullable()
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("createdbyuserid").AsCustom("citext").Nullable();
			}

			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_learningrecords_department_user_completed ON learningrecords (departmentid, userid, completedon);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_learningrecords_department_completed ON learningrecords (departmentid, completedon);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_learningrecords_department_session ON learningrecords (departmentid, sessionkey);");
			Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_learningrecords_key ON learningrecords (departmentid, sourcekind, sourcerefid, userid, componentkey) WHERE status <> 3;");
			Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_learningrecords_verificationcode ON learningrecords (verificationcode) WHERE verificationcode IS NOT NULL;");

			if (!Schema.Table("learningrecordfiles").Exists())
			{
				Create.Table("learningrecordfiles")
					.WithColumn("learningrecordfileid").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("userid").AsCustom("citext").NotNullable()
					.WithColumn("filename").AsString(255).NotNullable()
					.WithColumn("contenttype").AsString(100).Nullable()
					.WithColumn("data").AsCustom("bytea").Nullable()
					.WithColumn("uploadedon").AsDateTime2().NotNullable()
					.WithColumn("uploadedbyuserid").AsCustom("citext").Nullable();
			}

			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_learningrecordfiles_department ON learningrecordfiles (departmentid);");

			if (!Schema.Table("categorycreditmappings").Exists())
			{
				Create.Table("categorycreditmappings")
					.WithColumn("categorycreditmappingid").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("departmentcertificationtypeid").AsInt32().NotNullable()
					.WithColumn("categoryid").AsInt32().NotNullable()
					.WithColumn("componentkey").AsString(64).Nullable()
					.WithColumn("isenabled").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("enabledon").AsDateTime2().Nullable()
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("createdbyuserid").AsCustom("citext").Nullable()
					.WithColumn("updatedon").AsDateTime2().Nullable()
					.WithColumn("updatedbyuserid").AsCustom("citext").Nullable();

				Create.ForeignKey("fk_categorycreditmappings_trainingcategories").FromTable("categorycreditmappings").ForeignColumn("categoryid")
					.ToTable("trainingcategories").PrimaryColumn("trainingcategoryid").OnDelete(Rule.Cascade);
			}

			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_categorycreditmappings_department_type ON categorycreditmappings (departmentid, departmentcertificationtypeid);");

			if (!Schema.Table("learningrecordbackfills").Exists())
			{
				Create.Table("learningrecordbackfills")
					.WithColumn("learningrecordbackfillid").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("state").AsInt32().NotNullable()
					.WithColumn("dryrun").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("requestedon").AsDateTime2().NotNullable()
					.WithColumn("requestedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("startedon").AsDateTime2().Nullable()
					.WithColumn("completedon").AsDateTime2().Nullable()
					.WithColumn("resumeafter").AsString(200).Nullable()
					.WithColumn("completionsfound").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("trainingrecordsfound").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("recordswritten").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("recordsskipped").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("attempts").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("error").AsString(1000).Nullable();
			}

			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_learningrecordbackfills_state ON learningrecordbackfills (state);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_learningrecordbackfills_department ON learningrecordbackfills (departmentid);");
		}

		public override void Down()
		{
			Execute.Sql("DROP TABLE IF EXISTS learningrecordbackfills;");
			Execute.Sql("DROP TABLE IF EXISTS categorycreditmappings;");
			Execute.Sql("DROP TABLE IF EXISTS learningrecordfiles;");
			Execute.Sql("DROP TABLE IF EXISTS learningrecords;");
			Execute.Sql("DROP TABLE IF EXISTS trainingcategories;");
		}
	}
}
