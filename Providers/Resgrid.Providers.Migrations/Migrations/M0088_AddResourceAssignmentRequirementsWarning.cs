using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Advisory lane-requirement warnings: when a resource doesn't satisfy a lane's template requirements
	/// and the lane does not force them, the violation is stamped on the assignment so the IC app can
	/// render a warning indicator on the resource chip (offline sync included).
	/// </summary>
	[Migration(88)]
	public class M0088_AddResourceAssignmentRequirementsWarning : Migration
	{
		public override void Up()
		{
			if (Schema.Table("ResourceAssignments").Exists())
			{
				if (!Schema.Table("ResourceAssignments").Column("RequirementsWarning").Exists())
				{
					Alter.Table("ResourceAssignments")
						.AddColumn("RequirementsWarning").AsBoolean().NotNullable().WithDefaultValue(false);
				}

				if (!Schema.Table("ResourceAssignments").Column("RequirementsWarningMessage").Exists())
				{
					Alter.Table("ResourceAssignments")
						.AddColumn("RequirementsWarningMessage").AsString(500).Nullable();
				}
			}
		}

		public override void Down()
		{
			if (Schema.Table("ResourceAssignments").Exists())
			{
				if (Schema.Table("ResourceAssignments").Column("RequirementsWarningMessage").Exists())
					Delete.Column("RequirementsWarningMessage").FromTable("ResourceAssignments");

				if (Schema.Table("ResourceAssignments").Column("RequirementsWarning").Exists())
					Delete.Column("RequirementsWarning").FromTable("ResourceAssignments");
			}
		}
	}
}
