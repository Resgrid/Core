using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(2)]
	public class M0002_AddingContacts : Migration
	{
		public override void Up()
		{
			Create.Table("Contacts".ToLower())
			   .WithColumn("ContactId".ToLower()).AsInt32().NotNullable().PrimaryKey().Identity()
			   .WithColumn("DepartmentId".ToLower()).AsInt32().NotNullable()
			   .WithColumn("ContactTypeId".ToLower()).AsInt32()
			   .WithColumn("Name".ToLower()).AsCustom("citext").NotNullable()
			   .WithColumn("PhoneNumber".ToLower()).AsCustom("citext")
			   .WithColumn("FaxNumber".ToLower()).AsCustom("citext")
			   .WithColumn("Email".ToLower()).AsCustom("citext")
			   .WithColumn("Twitter".ToLower()).AsCustom("citext")
			   .WithColumn("Facebook".ToLower()).AsCustom("citext")
			   .WithColumn("Notes".ToLower()).AsCustom("citext")
			   .WithColumn("Address".ToLower()).AsCustom("citext")
			   .WithColumn("City".ToLower()).AsCustom("citext")
			   .WithColumn("State".ToLower()).AsCustom("citext")
			   .WithColumn("PostalCode".ToLower()).AsCustom("citext")
			   .WithColumn("Country".ToLower()).AsCustom("citext")
			   .WithColumn("Location".ToLower()).AsCustom("citext");


			Create.ForeignKey("FK_Conacts_Departments")
				.FromTable("Contacts".ToLower()).ForeignColumn("DepartmentId".ToLower())
				.ToTable("Departments".ToLower()).PrimaryColumn("DepartmentId".ToLower());
		}

		public override void Down()
		{
			Delete.Table("Contacts");
		}
	}
}
