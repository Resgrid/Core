using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Advisory lane-requirement warnings: when a resource doesn't satisfy a lane's template requirements
	/// and the lane does not force them, the violation is stamped on the assignment so the IC app can
	/// render a warning indicator on the resource chip (offline sync included).
	/// </summary>
	[Migration(88)]
	public class M0088_AddResourceAssignmentRequirementsWarningPg : Migration
	{
		public override void Up()
		{
			if (Schema.Table("resourceassignments").Exists())
			{
				if (!Schema.Table("resourceassignments").Column("requirementswarning").Exists())
				{
					Alter.Table("resourceassignments")
						.AddColumn("requirementswarning").AsBoolean().NotNullable().WithDefaultValue(false);
				}

				if (!Schema.Table("resourceassignments").Column("requirementswarningmessage").Exists())
				{
					Alter.Table("resourceassignments")
						.AddColumn("requirementswarningmessage").AsString(500).Nullable();
				}
			}
		}

		public override void Down()
		{
			if (Schema.Table("resourceassignments").Exists())
			{
				if (Schema.Table("resourceassignments").Column("requirementswarningmessage").Exists())
					Delete.Column("requirementswarningmessage").FromTable("resourceassignments");

				if (Schema.Table("resourceassignments").Column("requirementswarning").Exists())
					Delete.Column("requirementswarning").FromTable("resourceassignments");
			}
		}
	}
}
