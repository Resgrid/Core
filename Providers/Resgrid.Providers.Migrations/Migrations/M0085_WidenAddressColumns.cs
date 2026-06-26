using System;
using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Widens the Addresses columns. Address1 (street address) was nvarchar(200); long addresses saved via Contacts
	/// edit (and Stations/Groups/Home) hit SQL error 8152 "String or binary data would be truncated" in
	/// RepositoryBase.UpdateAsync (AddressService.SaveAddressAsync). Street address is free-text and goes to max; the
	/// shorter fields are padded generously.
	/// </summary>
	[Migration(85)]
	public class M0085_WidenAddressColumns : Migration
	{
		public override void Up()
		{
			if (Schema.Table("Addresses").Exists())
			{
				Alter.Table("Addresses").AlterColumn("Address1").AsString(Int32.MaxValue).NotNullable();
				Alter.Table("Addresses").AlterColumn("City").AsString(200).NotNullable();
				Alter.Table("Addresses").AlterColumn("State").AsString(100).NotNullable();
				Alter.Table("Addresses").AlterColumn("PostalCode").AsString(100).Nullable();
				Alter.Table("Addresses").AlterColumn("Country").AsString(100).NotNullable();
			}
		}

		public override void Down()
		{
			// No-op: narrowing back could truncate existing data.
		}
	}
}
