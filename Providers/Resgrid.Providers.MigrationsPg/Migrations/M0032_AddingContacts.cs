using FluentMigrator;
using System;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(32)]
	public class M0032_AddingContacts : Migration
	{
		public override void Up()
		{
			//Delete.ForeignKey("FK_Conacts_Departments");
			Delete.Table("Contacts".ToLower());

			Create.Table("Contacts".ToLower())
			   .WithColumn("ContactId".ToLower()).AsCustom("citext").NotNullable().PrimaryKey()
			   .WithColumn("DepartmentId".ToLower()).AsInt32().NotNullable()
			   .WithColumn("ContactType".ToLower()).AsInt32()
			   .WithColumn("ContactCategoryId".ToLower()).AsCustom("citext").Nullable()
			   .WithColumn("FirstName".ToLower()).AsCustom("citext").Nullable()
			   .WithColumn("MiddleName".ToLower()).AsCustom("citext").Nullable()
			   .WithColumn("LastName".ToLower()).AsCustom("citext").Nullable()
			   .WithColumn("OtherName".ToLower()).AsCustom("citext").Nullable()
			   .WithColumn("CompanyName".ToLower()).AsCustom("citext").Nullable()
			   .WithColumn("Email".ToLower()).AsCustom("citext").Nullable()
			   .WithColumn("PhysicalAddressId".ToLower()).AsInt32().Nullable()
			   .WithColumn("MailingAddressId".ToLower()).AsInt32().Nullable()
			   .WithColumn("Website".ToLower()).AsCustom("citext").Nullable()
			   .WithColumn("Twitter".ToLower()).AsCustom("citext").Nullable()
			   .WithColumn("Facebook".ToLower()).AsCustom("citext").Nullable()
			   .WithColumn("LinkedIn".ToLower()).AsCustom("citext").Nullable()
			   .WithColumn("Instagram".ToLower()).AsCustom("citext").Nullable()
			   .WithColumn("Threads".ToLower()).AsCustom("citext").Nullable()
			   .WithColumn("Bluesky".ToLower()).AsCustom("citext").Nullable()
			   .WithColumn("Mastodon".ToLower()).AsCustom("citext").Nullable()
			   .WithColumn("LocationGpsCoordinates".ToLower()).AsCustom("citext").Nullable()
			   .WithColumn("EntranceGpsCoordinates".ToLower()).AsCustom("citext").Nullable()
			   .WithColumn("ExitGpsCoordinates".ToLower()).AsCustom("citext").Nullable()
			   .WithColumn("LocationGeofence".ToLower()).AsCustom("citext").Nullable()
			   .WithColumn("CountryIssuedIdNumber".ToLower()).AsCustom("citext").Nullable()
			   .WithColumn("CountryIdName".ToLower()).AsCustom("citext").Nullable()
			   .WithColumn("StateIdNumber".ToLower()).AsCustom("citext").Nullable()
			   .WithColumn("StateIdName".ToLower()).AsCustom("citext").Nullable()
			   .WithColumn("StateIdCountryName".ToLower()).AsCustom("citext").Nullable()
			   .WithColumn("Description".ToLower()).AsCustom("citext").Nullable()
			   .WithColumn("OtherInfo".ToLower()).AsCustom("citext").Nullable()
			   .WithColumn("HomePhoneNumber".ToLower()).AsCustom("citext").Nullable()
			   .WithColumn("CellPhoneNumber".ToLower()).AsCustom("citext").Nullable()
			   .WithColumn("FaxPhoneNumber".ToLower()).AsCustom("citext").Nullable()
			   .WithColumn("OfficePhoneNumber".ToLower()).AsCustom("citext").Nullable()
			   .WithColumn("Image".ToLower()).AsBinary().Nullable()
			   .WithColumn("IsDeleted".ToLower()).AsBoolean()
			   .WithColumn("AddedOn".ToLower()).AsDateTime2().NotNullable()
			   .WithColumn("AddedByUserId".ToLower()).AsCustom("citext").NotNullable()
			   .WithColumn("EditedOn".ToLower()).AsDateTime2().Nullable()
			   .WithColumn("EditedByUserId".ToLower()).AsCustom("citext").Nullable();

			Create.ForeignKey("FK_Contacts_Department")
				.FromTable("Contacts".ToLower()).ForeignColumn("DepartmentId".ToLower())
				.ToTable("Departments".ToLower()).PrimaryColumn("DepartmentId".ToLower());

			Create.ForeignKey("FK_Contacts_PhysicalAddress")
				.FromTable("Contacts".ToLower()).ForeignColumn("PhysicalAddressId".ToLower())
				.ToTable("Addresses".ToLower()).PrimaryColumn("AddressId".ToLower());

			Create.ForeignKey("FK_Contacts_MailingAddress")
				.FromTable("Contacts".ToLower()).ForeignColumn("MailingAddressId".ToLower())
				.ToTable("Addresses".ToLower()).PrimaryColumn("AddressId".ToLower());

			Create.Table("ContactNoteTypes".ToLower())
			   .WithColumn("ContactNoteTypeId".ToLower()).AsCustom("citext").NotNullable().PrimaryKey()
			   .WithColumn("DepartmentId".ToLower()).AsInt32().NotNullable()
			   .WithColumn("Name".ToLower()).AsCustom("citext").NotNullable()
			   .WithColumn("Color".ToLower()).AsCustom("citext").Nullable()
			   .WithColumn("DefaultShouldAlert".ToLower()).AsBoolean().NotNullable()
			   .WithColumn("DefaultVisibility".ToLower()).AsInt32().NotNullable()
			   .WithColumn("AddedOn".ToLower()).AsDateTime2().NotNullable()
			   .WithColumn("AddedByUserId".ToLower()).AsCustom("citext").NotNullable()
			   .WithColumn("EditedOn".ToLower()).AsDateTime2().Nullable()
			   .WithColumn("EditedByUserId".ToLower()).AsCustom("citext").Nullable();

			Create.ForeignKey("FK_ContactNoteTypes_Department")
				.FromTable("ContactNoteTypes".ToLower()).ForeignColumn("DepartmentId".ToLower())
				.ToTable("Departments".ToLower()).PrimaryColumn("DepartmentId".ToLower());

			Create.Table("ContactNotes".ToLower())
			   .WithColumn("ContactNoteId".ToLower()).AsCustom("citext").NotNullable().PrimaryKey()
			   .WithColumn("DepartmentId".ToLower()).AsInt32().NotNullable()
			   .WithColumn("ContactId".ToLower()).AsCustom("citext").NotNullable()
			   .WithColumn("ContactNoteTypeId".ToLower()).AsCustom("citext").Nullable()
			   .WithColumn("Note".ToLower()).AsCustom("citext").NotNullable()
			   .WithColumn("Color".ToLower()).AsCustom("citext").Nullable()
			   .WithColumn("ShouldAlert".ToLower()).AsBoolean().NotNullable()
			   .WithColumn("Visibility".ToLower()).AsInt32().NotNullable()
			   .WithColumn("ExpiresOn".ToLower()).AsDateTime2().Nullable()
			   .WithColumn("IsDeleted".ToLower()).AsBoolean().NotNullable()
			   .WithColumn("AddedOn".ToLower()).AsDateTime2().NotNullable()
			   .WithColumn("AddedByUserId".ToLower()).AsCustom("citext").NotNullable()
			   .WithColumn("EditedOn".ToLower()).AsDateTime2().Nullable()
			   .WithColumn("EditedByUserId".ToLower()).AsCustom("citext").Nullable();

			Create.ForeignKey("FK_ContactNotes_Department")
				.FromTable("ContactNotes".ToLower()).ForeignColumn("DepartmentId".ToLower())
				.ToTable("Departments".ToLower()).PrimaryColumn("DepartmentId".ToLower());

			Create.ForeignKey("FK_ContactNotes_Contacts")
				.FromTable("ContactNotes".ToLower()).ForeignColumn("ContactId".ToLower())
				.ToTable("Contacts".ToLower()).PrimaryColumn("ContactId".ToLower());

			Create.Table("ContactAssociations".ToLower())
			   .WithColumn("ContactAssociationId".ToLower()).AsCustom("citext").NotNullable().PrimaryKey()
			   .WithColumn("DepartmentId".ToLower()).AsInt32().NotNullable()
			   .WithColumn("SourceContactId".ToLower()).AsCustom("citext").NotNullable()
			   .WithColumn("TargetContactId".ToLower()).AsCustom("citext").NotNullable()
			   .WithColumn("Type".ToLower()).AsInt32().NotNullable()
			   .WithColumn("Note".ToLower()).AsCustom("citext").Nullable()
			   .WithColumn("Twitter".ToLower()).AsCustom("citext").Nullable()
			   .WithColumn("AddedOn".ToLower()).AsDateTime2().NotNullable()
			   .WithColumn("AddedByUserId".ToLower()).AsCustom("citext").NotNullable();

			Create.ForeignKey("FK_ContactAssociations_Department")
				.FromTable("ContactAssociations".ToLower()).ForeignColumn("DepartmentId".ToLower())
				.ToTable("Departments".ToLower()).PrimaryColumn("DepartmentId".ToLower());

			Create.ForeignKey("FK_ContactAssociations_SourceContactId")
				.FromTable("ContactAssociations".ToLower()).ForeignColumn("SourceContactId".ToLower())
				.ToTable("Contacts".ToLower()).PrimaryColumn("ContactId".ToLower());

			Create.ForeignKey("FK_ContactAssociations_TargetContactId")
				.FromTable("ContactAssociations".ToLower()).ForeignColumn("TargetContactId".ToLower())
				.ToTable("Contacts".ToLower()).PrimaryColumn("ContactId".ToLower());

			Create.Table("CallContacts".ToLower())
			   .WithColumn("CallContactId".ToLower()).AsCustom("citext").NotNullable().PrimaryKey()
			   .WithColumn("DepartmentId".ToLower()).AsInt32().NotNullable()
			   .WithColumn("CallId".ToLower()).AsInt32().NotNullable()
			   .WithColumn("ContactId".ToLower()).AsCustom("citext").NotNullable()
			   .WithColumn("CallContactType".ToLower()).AsInt32().NotNullable();

			Create.ForeignKey("FK_CallContacts_Department")
				.FromTable("CallContacts".ToLower()).ForeignColumn("DepartmentId".ToLower())
				.ToTable("Departments".ToLower()).PrimaryColumn("DepartmentId".ToLower());

			Create.ForeignKey("FK_CallContacts_Contacts")
				.FromTable("Contacts".ToLower()).ForeignColumn("ContactId".ToLower())
				.ToTable("Contacts".ToLower()).PrimaryColumn("ContactId".ToLower());

			Create.Table("ContactCategories".ToLower())
			   .WithColumn("ContactCategoryId".ToLower()).AsCustom("citext").NotNullable().PrimaryKey()
			   .WithColumn("DepartmentId".ToLower()).AsInt32().NotNullable()
			   .WithColumn("Name".ToLower()).AsCustom("citext").NotNullable()
			   .WithColumn("Description".ToLower()).AsCustom("citext").Nullable()
			   .WithColumn("Color".ToLower()).AsCustom("citext").Nullable()
			   .WithColumn("DisplayOnMap".ToLower()).AsBoolean().NotNullable()
			   .WithColumn("MapIcon".ToLower()).AsInt32().Nullable()
			   .WithColumn("AddedOn".ToLower()).AsDateTime2().NotNullable()
			   .WithColumn("AddedByUserId".ToLower()).AsCustom("citext").NotNullable()
			   .WithColumn("EditedOn".ToLower()).AsDateTime2().Nullable()
			   .WithColumn("EditedByUserId".ToLower()).AsCustom("citext").Nullable();

			Create.ForeignKey("FK_ContactCategories_Department")
				.FromTable("ContactCategories".ToLower()).ForeignColumn("DepartmentId".ToLower())
				.ToTable("Departments".ToLower()).PrimaryColumn("DepartmentId".ToLower());
		}

		public override void Down()
		{

		}
	}
}
