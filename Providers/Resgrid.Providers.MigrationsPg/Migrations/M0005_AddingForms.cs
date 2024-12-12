using FluentMigrator;
using System;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(5)]
	public class M0005_AddingForms : Migration
	{
		public override void Up()
		{
			Create.Table("Forms")
			   .WithColumn("FormId").AsCustom("citext").NotNullable().PrimaryKey()
			   .WithColumn("DepartmentId").AsInt32().NotNullable()
			   .WithColumn("Type").AsInt32().NotNullable()
			   .WithColumn("Name").AsCustom("citext").NotNullable()
			   .WithColumn("IsActive").AsBoolean()
			   .WithColumn("IsDeleted").AsBoolean()
			   .WithColumn("Data").AsCustom("citext")
			   .WithColumn("CreatedOn").AsDateTime2()
			   .WithColumn("CreatedBy").AsCustom("citext")
			   .WithColumn("UpdatedOn").AsDateTime2()
			   .WithColumn("UpdatedBy").AsCustom("citext");

			Create.Table("FormAutomations")
			   .WithColumn("FormAutomationId").AsCustom("citext").NotNullable().PrimaryKey()
			   .WithColumn("FormId").AsCustom("citext").NotNullable()
			   .WithColumn("TriggerField").AsCustom("citext").NotNullable()
			   .WithColumn("TriggerValue").AsCustom("citext")
			   .WithColumn("OperationType").AsInt32().NotNullable()
			   .WithColumn("OperationValue").AsCustom("citext");


			Create.ForeignKey("FK_Forms_Departments")
				.FromTable("Forms").ForeignColumn("DepartmentId")
				.ToTable("Departments").PrimaryColumn("DepartmentId");

			Create.ForeignKey("FK_FormAutomations_Forms")
				.FromTable("FormAutomations").ForeignColumn("FormId")
				.ToTable("Forms").PrimaryColumn("FormId");
		}

		public override void Down()
		{
			Delete.Table("Forms");
			Delete.Table("FormAutomations");
		}
	}
}
