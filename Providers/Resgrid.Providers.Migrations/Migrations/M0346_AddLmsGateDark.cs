using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// LMS add-on gate, built dark (protocols-training-knowledge-plan §17.3, §24, §25.4a item 7): the Training.Lms operator flag,
	/// seeded off (like M0238 seeds the AI flags), and the LMS certificate templates table. No PlanAddons catalog row is
	/// seeded: nothing is offered for sale before wave 3, and ILmsAccessService fails closed without one.
	/// Guarded for safe retry.
	/// </summary>
	[Migration(346)]
	public class M0346_AddLmsGateDark : Migration
	{
		public const string FlagKey = "Training.Lms";

		public override void Up()
		{
			Execute.Sql(
				"IF NOT EXISTS (SELECT 1 FROM [FeatureFlags] WHERE [FlagKey] = '" + FlagKey + "') " +
				"INSERT INTO [FeatureFlags] ([FlagKey], [Name], [Description], [Category], [IsEnabledGlobally]) " +
				"VALUES ('" + FlagKey + "', 'LMS add-on', 'LMS add-on operator rollout and kill switch. Off: the formal ISO FSRS and NREMT NCCP reports, certificate verification codes, the public verify page and custom certificate templates are hidden and their routes return 404. The purchase is PlanAddonTypes.Lms. Seeded off; switched on with wave 3.', 'Training', 0);");

			if (!Schema.Table("LmsCertificateTemplates").Exists())
			{
				Create.Table("LmsCertificateTemplates")
					.WithColumn("LmsCertificateTemplateId").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("Name").AsString(150).NotNullable()
					.WithColumn("Body").AsString(int.MaxValue).Nullable()
					.WithColumn("IsDefault").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("CreatedByUserId").AsString(128).Nullable()
					.WithColumn("UpdatedOn").AsDateTime2().Nullable();

				Create.Index("IX_LmsCertificateTemplates_Department").OnTable("LmsCertificateTemplates").OnColumn("DepartmentId").Ascending();
			}
		}

		public override void Down()
		{
			if (Schema.Table("LmsCertificateTemplates").Exists())
				Delete.Table("LmsCertificateTemplates");

			Execute.Sql("DELETE FROM [FeatureFlags] WHERE [FlagKey] = '" + FlagKey + "';");
		}
	}
}
