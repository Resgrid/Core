using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Workforce &amp; Business Operations plan, Phase D (D2): continuing-education / CEU credit entries against a
	/// personnel certification (D1.4). Hours roll up against the type's RenewalCreditHoursRequired. Description and
	/// Data are ADP catalog 27 protected fields; the table carries IsProtected/ProtectedCatalogVersion from creation.
	/// Registry M0214. Guarded for safe retry.
	/// </summary>
	[Migration(214)]
	public class M0214_AddCertificationCredits : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("PersonnelCertificationCredits").Exists())
			{
				Create.Table("PersonnelCertificationCredits")
					.WithColumn("PersonnelCertificationCreditId").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("PersonnelCertificationId").AsInt32().NotNullable()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("CreditDate").AsDateTime2().NotNullable()
					.WithColumn("Hours").AsDecimal(9, 2).NotNullable().WithDefaultValue(0)
					.WithColumn("Category").AsString(100).Nullable()
					.WithColumn("Description").AsString(int.MaxValue).Nullable()
					.WithColumn("FileName").AsString(int.MaxValue).Nullable()
					.WithColumn("FileType").AsString(200).Nullable()
					.WithColumn("Data").AsBinary(int.MaxValue).Nullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedCatalogVersion").AsInt32().Nullable();

				Create.Index("IX_PersonnelCertificationCredits_Certification").OnTable("PersonnelCertificationCredits")
					.OnColumn("PersonnelCertificationId").Ascending().OnColumn("CreditDate").Ascending();
				Create.Index("IX_PersonnelCertificationCredits_Department").OnTable("PersonnelCertificationCredits")
					.OnColumn("DepartmentId").Ascending();
				Create.ForeignKey("FK_PersonnelCertificationCredits_Certification").FromTable("PersonnelCertificationCredits").ForeignColumn("PersonnelCertificationId")
					.ToTable("PersonnelCertifications").PrimaryColumn("PersonnelCertificationId");
			}
		}

		public override void Down()
		{
			if (Schema.Table("PersonnelCertificationCredits").Exists())
				Delete.Table("PersonnelCertificationCredits");
		}
	}
}
