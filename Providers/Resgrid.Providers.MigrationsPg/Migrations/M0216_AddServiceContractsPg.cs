using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0216 (Workforce &amp; Business Operations plan, Phase C). Same number, lower-case identifiers, guarded for safe retry.
	/// </summary>
	[Migration(216)]
	public class M0216_AddServiceContractsPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("servicecontracts").Exists())
			{
				Create.Table("servicecontracts")
					.WithColumn("servicecontractid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("contactid").AsString(128).NotNullable()
					.WithColumn("customerbillingprofileid").AsString(36).Nullable()
					.WithColumn("contractnumber").AsString(100).Nullable()
					.WithColumn("name").AsString(250).NotNullable()
					.WithColumn("contracttype").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("starton").AsDateTime2().NotNullable()
					.WithColumn("endon").AsDateTime2().Nullable()
					.WithColumn("ratescheduleid").AsString(36).Nullable()
					.WithColumn("discountpercent").AsDecimal(9,4).Nullable()
					.WithColumn("termsnetdays").AsInt32().Nullable()
					.WithColumn("invoicesubmissionemail").AsString(500).Nullable()
					.WithColumn("maxdeploymentdays").AsInt32().Nullable()
					.WithColumn("responsetimeminutes").AsInt32().Nullable()
					.WithColumn("pointofhire").AsString(250).Nullable()
					.WithColumn("documenttemplatekey").AsString(50).Nullable()
					.WithColumn("notes").AsCustom("text").Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsString(128).Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsString(128).Nullable()
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_servicecontracts_department ON servicecontracts (departmentid, isdeleted, status);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_servicecontracts_contact ON servicecontracts (contactid);");
			}
			if (!Schema.Table("servicecontractdocumentrequirements").Exists())
			{
				Create.Table("servicecontractdocumentrequirements")
					.WithColumn("servicecontractdocumentrequirementid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("servicecontractid").AsString(36).NotNullable()
					.WithColumn("name").AsString(250).NotNullable()
					.WithColumn("stage").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("compliancedocumenttype").AsInt32().Nullable()
					.WithColumn("ismandatory").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("sortorder").AsInt32().NotNullable().WithDefaultValue(0);
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_servicecontractdocumentrequirements_contract ON servicecontractdocumentrequirements (servicecontractid, sortorder);");
				Create.ForeignKey("fk_servicecontractdocumentrequirements_contract").FromTable("servicecontractdocumentrequirements").ForeignColumn("servicecontractid").ToTable("servicecontracts").PrimaryColumn("servicecontractid");
			}
			if (!Schema.Table("departmentcompliancedocuments").Exists())
			{
				Create.Table("departmentcompliancedocuments")
					.WithColumn("departmentcompliancedocumentid").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("documenttype").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("name").AsString(250).NotNullable()
					.WithColumn("documentnumber").AsCustom("text").Nullable()
					.WithColumn("issuer").AsString(250).Nullable()
					.WithColumn("effectiveon").AsDateTime2().Nullable()
					.WithColumn("expireson").AsDateTime2().Nullable()
					.WithColumn("alertleaddays").AsInt32().NotNullable().WithDefaultValue(30)
					.WithColumn("filename").AsCustom("text").Nullable()
					.WithColumn("filetype").AsString(200).Nullable()
					.WithColumn("filesize").AsInt32().Nullable()
					.WithColumn("data").AsCustom("bytea").Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsString(128).Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsString(128).Nullable()
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_departmentcompliancedocuments_department ON departmentcompliancedocuments (departmentid, isdeleted, expireson);");
			}
		}

		public override void Down()
		{
			Execute.Sql("DROP TABLE IF EXISTS departmentcompliancedocuments;");
			Execute.Sql("DROP TABLE IF EXISTS servicecontractdocumentrequirements;");
			Execute.Sql("DROP TABLE IF EXISTS servicecontracts;");
		}
	}
}
