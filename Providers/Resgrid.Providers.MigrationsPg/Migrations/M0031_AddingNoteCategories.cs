using FluentMigrator;
using System;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(31)]
	public class M0031_AddingNoteCategories : Migration
	{
		public override void Up()
		{
			Create.Table("NoteCategories")
			   .WithColumn("NoteCategoryId").AsCustom("citext").NotNullable().PrimaryKey()
			   .WithColumn("DepartmentId").AsInt32().NotNullable()
			   .WithColumn("Name").AsCustom("citext").NotNullable()
			   .WithColumn("AddedOn").AsDateTime2().NotNullable()
			   .WithColumn("AddedById").AsCustom("citext").NotNullable();

			Create.ForeignKey("FK_NoteCategories_Department")
				.FromTable("NoteCategories").ForeignColumn("DepartmentId")
				.ToTable("Departments").PrimaryColumn("DepartmentId");
		}

		public override void Down()
		{

		}
	}
}
