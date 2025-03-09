using FluentMigrator;
using System;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(5)]
	public class M0005_AddingForms : Migration
	{
		public override void Up()
		{
			Create.Table("Forms".ToLower())
			   .WithColumn("FormId".ToLower()).AsCustom("citext").NotNullable().PrimaryKey()
			   .WithColumn("DepartmentId".ToLower()).AsInt32().NotNullable()
			   .WithColumn("Type".ToLower()).AsInt32().NotNullable()
			   .WithColumn("Name".ToLower()).AsCustom("citext").NotNullable()
			   .WithColumn("IsActive".ToLower()).AsBoolean()
			   .WithColumn("IsDeleted".ToLower()).AsBoolean()
			   .WithColumn("Data".ToLower()).AsCustom("citext")
			   .WithColumn("CreatedOn".ToLower()).AsDateTime2()
			   .WithColumn("CreatedBy".ToLower()).AsCustom("citext")
			   .WithColumn("UpdatedOn".ToLower()).AsDateTime2()
			   .WithColumn("UpdatedBy".ToLower()).AsCustom("citext");

			Create.Table("FormAutomations".ToLower())
			   .WithColumn("FormAutomationId".ToLower()).AsCustom("citext").NotNullable().PrimaryKey()
			   .WithColumn("FormId".ToLower()).AsCustom("citext").NotNullable()
			   .WithColumn("TriggerField".ToLower()).AsCustom("citext").NotNullable()
			   .WithColumn("TriggerValue".ToLower()).AsCustom("citext")
			   .WithColumn("OperationType".ToLower()).AsInt32().NotNullable()
			   .WithColumn("OperationValue".ToLower()).AsCustom("citext");


			Create.ForeignKey("FK_Forms_Departments")
				.FromTable("Forms".ToLower()).ForeignColumn("DepartmentId".ToLower())
				.ToTable("Departments".ToLower()).PrimaryColumn("DepartmentId".ToLower());

			Create.ForeignKey("FK_FormAutomations_Forms")
				.FromTable("FormAutomations".ToLower()).ForeignColumn("FormId".ToLower())
				.ToTable("Forms".ToLower()).PrimaryColumn("FormId".ToLower());
		}

		public override void Down()
		{
			Delete.Table("Forms");
			Delete.Table("FormAutomations");
		}
	}
}
