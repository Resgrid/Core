using FluentMigrator;
using System;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(5)]
	public class M0005_AddingForms : Migration
	{
		public override void Up()
		{
			Create.Table("Forms")
			   .WithColumn("FormId").AsString(128).NotNullable().PrimaryKey()
			   .WithColumn("DepartmentId").AsInt32().NotNullable()
			   .WithColumn("Type").AsInt32().NotNullable()
			   .WithColumn("Name").AsString().NotNullable()
			   .WithColumn("IsActive").AsBoolean()
			   .WithColumn("IsDeleted").AsBoolean()
			   .WithColumn("Data").AsString(Int32.MaxValue)
			   .WithColumn("CreatedOn").AsDateTime2()
			   .WithColumn("CreatedBy").AsString(128)
			   .WithColumn("UpdatedOn").AsDateTime2()
			   .WithColumn("UpdatedBy").AsString(128);

			Create.Table("FormAutomations")
			   .WithColumn("FormAutomationId").AsString(128).NotNullable().PrimaryKey()
			   .WithColumn("FormId").AsString(128).NotNullable()
			   .WithColumn("TriggerField").AsString().NotNullable()
			   .WithColumn("TriggerValue").AsString()
			   .WithColumn("OperationType").AsInt32().NotNullable()
			   .WithColumn("OperationValue").AsString();


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
