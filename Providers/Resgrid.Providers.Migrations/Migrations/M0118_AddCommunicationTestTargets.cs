using System.Data;
using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// CommunicationTestTargets: scopes a communication test to specific groups, personnel roles
	/// or individual users. A test with no rows keeps the original behavior and covers the whole
	/// department. Cascades with the parent test so deleting a test removes its targeting.
	/// </summary>
	[Migration(118)]
	public class M0118_AddCommunicationTestTargets : Migration
	{
		public override void Up()
		{
			Create.Table("CommunicationTestTargets")
				.WithColumn("CommunicationTestTargetId").AsGuid().NotNullable().PrimaryKey().WithDefault(SystemMethods.NewGuid)
				.WithColumn("CommunicationTestId").AsGuid().NotNullable()
				.WithColumn("DepartmentId").AsInt32().NotNullable()
				.WithColumn("TargetType").AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("TargetId").AsString(128).NotNullable();

			Create.ForeignKey("FK_CommunicationTestTargets_CommunicationTests")
				.FromTable("CommunicationTestTargets").ForeignColumn("CommunicationTestId")
				.ToTable("CommunicationTests").PrimaryColumn("CommunicationTestId")
				.OnDelete(Rule.Cascade);

			Create.Index("IX_CommunicationTestTargets_CommunicationTestId")
				.OnTable("CommunicationTestTargets")
				.OnColumn("CommunicationTestId").Ascending();

			Create.Index("IX_CommunicationTestTargets_DepartmentId")
				.OnTable("CommunicationTestTargets")
				.OnColumn("DepartmentId").Ascending();
		}

		public override void Down()
		{
			Delete.Table("CommunicationTestTargets");
		}
	}
}
