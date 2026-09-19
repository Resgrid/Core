using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0214 (Workforce &amp; Business Operations plan, Phase D): personnel certification credit
	/// entries. Same number, lower-case identifiers, guarded for safe retry.
	/// </summary>
	[Migration(214)]
	public class M0214_AddCertificationCreditsPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("personnelcertificationcredits").Exists())
			{
				Create.Table("personnelcertificationcredits")
					.WithColumn("personnelcertificationcreditid").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("personnelcertificationid").AsInt32().NotNullable()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("creditdate").AsDateTime2().NotNullable()
					.WithColumn("hours").AsDecimal(9, 2).NotNullable().WithDefaultValue(0)
					.WithColumn("category").AsString(100).Nullable()
					.WithColumn("description").AsCustom("text").Nullable()
					.WithColumn("filename").AsCustom("text").Nullable()
					.WithColumn("filetype").AsString(200).Nullable()
					.WithColumn("data").AsCustom("bytea").Nullable()
					.WithColumn("addedbyuserid").AsString(128).Nullable()
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().Nullable();

				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_personnelcertificationcredits_certification ON personnelcertificationcredits (personnelcertificationid, creditdate);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_personnelcertificationcredits_department ON personnelcertificationcredits (departmentid);");
				Create.ForeignKey("fk_personnelcertificationcredits_certification").FromTable("personnelcertificationcredits").ForeignColumn("personnelcertificationid").ToTable("personnelcertifications").PrimaryColumn("personnelcertificationid");
			}
		}

		public override void Down()
		{
			Execute.Sql("DROP TABLE IF EXISTS personnelcertificationcredits;");
		}
	}
}
