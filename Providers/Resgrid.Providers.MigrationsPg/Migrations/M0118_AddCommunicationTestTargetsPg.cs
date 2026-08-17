using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// CommunicationTestTargets: scopes a communication test to specific groups, personnel roles
	/// or individual users. A test with no rows keeps the original behavior and covers the whole
	/// department. Cascades with the parent test so deleting a test removes its targeting.
	/// Guid columns use citext to match the existing communication test tables (M0062).
	/// </summary>
	[Migration(118)]
	public class M0118_AddCommunicationTestTargetsPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("communicationtesttargets").Exists())
			{
				Create.Table("communicationtesttargets")
					.WithColumn("communicationtesttargetid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("communicationtestid").AsCustom("citext").NotNullable()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("targettype").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("targetid").AsCustom("citext").NotNullable();

				Create.ForeignKey("fk_communicationtesttargets_communicationtests")
					.FromTable("communicationtesttargets").ForeignColumn("communicationtestid")
					.ToTable("communicationtests").PrimaryColumn("communicationtestid")
					.OnDelete(System.Data.Rule.Cascade);

				Create.Index("ix_communicationtesttargets_communicationtestid")
					.OnTable("communicationtesttargets")
					.OnColumn("communicationtestid");

				Create.Index("ix_communicationtesttargets_departmentid")
					.OnTable("communicationtesttargets")
					.OnColumn("departmentid");
			}
		}

		public override void Down()
		{
			Delete.Table("communicationtesttargets");
		}
	}
}
