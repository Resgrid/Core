using FluentMigrator;
using System;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(33)]
	public class M0033_UpdatingContactsTable : Migration
	{
		public override void Up()
		{
			Alter.Table("Contacts").AlterColumn("ContactCategoryId").AsString(128).Nullable();
			Alter.Table("Contacts").AlterColumn("FirstName").AsString().Nullable();
			Alter.Table("Contacts").AlterColumn("MiddleName").AsString().Nullable();
			Alter.Table("Contacts").AlterColumn("LastName").AsString().Nullable();
			Alter.Table("Contacts").AlterColumn("OtherName").AsString().Nullable();
			Alter.Table("Contacts").AlterColumn("CompanyName").AsString().Nullable();
			Alter.Table("Contacts").AlterColumn("Email").AsString().Nullable();
			Alter.Table("Contacts").AlterColumn("PhysicalAddressId").AsInt32().Nullable();
			Alter.Table("Contacts").AlterColumn("MailingAddressId").AsInt32().Nullable();
			Alter.Table("Contacts").AlterColumn("Website").AsString().Nullable();
			Alter.Table("Contacts").AlterColumn("Twitter").AsString().Nullable();
			Alter.Table("Contacts").AlterColumn("Facebook").AsString().Nullable();
			Alter.Table("Contacts").AlterColumn("LinkedIn").AsString().Nullable();
			Alter.Table("Contacts").AlterColumn("Instagram").AsString().Nullable();
			Alter.Table("Contacts").AlterColumn("Threads").AsString().Nullable();
			Alter.Table("Contacts").AlterColumn("Bluesky").AsString().Nullable();
			Alter.Table("Contacts").AlterColumn("Mastodon").AsString().Nullable();
			Alter.Table("Contacts").AlterColumn("LocationGpsCoordinates").AsString().Nullable();
			Alter.Table("Contacts").AlterColumn("EntranceGpsCoordinates").AsString().Nullable();
			Alter.Table("Contacts").AlterColumn("ExitGpsCoordinates").AsString().Nullable();
			Alter.Table("Contacts").AlterColumn("LocationGeofence").AsString().Nullable();
			Alter.Table("Contacts").AlterColumn("CountryIssuedIdNumber").AsString().Nullable();
			Alter.Table("Contacts").AlterColumn("CountryIdName").AsString().Nullable();
			Alter.Table("Contacts").AlterColumn("StateIdNumber").AsString().Nullable();
			Alter.Table("Contacts").AlterColumn("StateIdName").AsString().Nullable();
			Alter.Table("Contacts").AlterColumn("StateIdCountryName").AsString().Nullable();
			Alter.Table("Contacts").AlterColumn("Description").AsString().Nullable();
			Alter.Table("Contacts").AlterColumn("OtherInfo").AsString().Nullable();
			Alter.Table("Contacts").AlterColumn("HomePhoneNumber").AsString().Nullable();
			Alter.Table("Contacts").AlterColumn("CellPhoneNumber").AsString().Nullable();
			Alter.Table("Contacts").AlterColumn("FaxPhoneNumber").AsString().Nullable();
			Alter.Table("Contacts").AlterColumn("OfficePhoneNumber").AsString().Nullable();
			Alter.Table("Contacts").AlterColumn("Image").AsBinary().Nullable();
			Alter.Table("Contacts").AlterColumn("EditedOn").AsDateTime2().Nullable();
			Alter.Table("Contacts").AlterColumn("EditedByUserId").AsString(128).Nullable();

			Alter.Table("ContactNoteTypes").AlterColumn("Color").AsString().Nullable();
			Alter.Table("ContactNoteTypes").AlterColumn("EditedOn").AsDateTime2().Nullable();
			Alter.Table("ContactNoteTypes").AlterColumn("EditedByUserId").AsString(128).Nullable();

			Alter.Table("ContactNotes").AlterColumn("ContactNoteTypeId").AsString(128).Nullable();
			Alter.Table("ContactNotes").AlterColumn("Color").AsString().Nullable();
			Alter.Table("ContactNotes").AlterColumn("EditedOn").AsDateTime2().Nullable();
			Alter.Table("ContactNotes").AlterColumn("EditedByUserId").AsString(128).Nullable();

			Alter.Table("ContactCategories").AlterColumn("Color").AsString().Nullable();
			Alter.Table("ContactCategories").AlterColumn("EditedOn").AsDateTime2().Nullable();
			Alter.Table("ContactCategories").AlterColumn("EditedByUserId").AsString(128).Nullable();
		}

		public override void Down()
		{

		}
	}
}
