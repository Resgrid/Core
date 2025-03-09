using FluentMigrator;
using System;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(31)]
	public class M0031_AddingNoteCategories : Migration
	{
		public override void Up()
		{
			Create.Table("NoteCategories".ToLower())
			   .WithColumn("NoteCategoryId".ToLower()).AsCustom("citext").NotNullable().PrimaryKey()
			   .WithColumn("DepartmentId".ToLower()).AsInt32().NotNullable()
			   .WithColumn("Name".ToLower()).AsCustom("citext").NotNullable()
			   .WithColumn("AddedOn".ToLower()).AsDateTime2().NotNullable()
			   .WithColumn("AddedById".ToLower()).AsCustom("citext").NotNullable();

			Create.ForeignKey("FK_NoteCategories_Department")
				.FromTable("NoteCategories".ToLower()).ForeignColumn("DepartmentId".ToLower())
				.ToTable("Departments".ToLower()).PrimaryColumn("DepartmentId".ToLower());
		}

		public override void Down()
		{

		}
	}
}
