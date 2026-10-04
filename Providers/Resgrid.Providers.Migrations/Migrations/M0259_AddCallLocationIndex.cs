using System.Data;
using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Call location history: a lookup row per call holding its parsed address key and coordinates, so previous calls at
	/// a location are found however the address was typed ("110 S Main St" / "110 South Main"). Rows cascade with their
	/// call. A per-department state row drives the backfill (worker 73) and records Advanced Data Protection
	/// suppression, since the keys are derived from protected call fields. Also indexes CallContacts, which had none,
	/// for the contact side of the same history.
	/// </summary>
	[Migration(259)]
	public class M0259_AddCallLocationIndex : Migration
	{
		private const string Keys = "CallLocationKeys";
		private const string States = "CallLocationIndexStates";

		public override void Up()
		{
			if (!Schema.Table(Keys).Exists())
			{
				Create.Table(Keys)
					.WithColumn("CallId").AsInt32().NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("AddressKey").AsString(120).Nullable()
					.WithColumn("AddressCanonical").AsString(500).Nullable()
					.WithColumn("Latitude").AsDecimal(9, 6).Nullable()
					.WithColumn("Longitude").AsDecimal(9, 6).Nullable()
					.WithColumn("LoggedOn").AsDateTime2().NotNullable()
					.WithColumn("KeyVersion").AsInt32().NotNullable()
					.WithColumn("IndexedOn").AsDateTime2().NotNullable();

				Create.ForeignKey("FK_CallLocationKeys_Calls")
					.FromTable(Keys).ForeignColumn("CallId")
					.ToTable("Calls").PrimaryColumn("CallId")
					.OnDelete(Rule.Cascade);
			}

			if (!Schema.Table(Keys).Index("IX_CallLocationKeys_Department_AddressKey").Exists())
			{
				Create.Index("IX_CallLocationKeys_Department_AddressKey")
					.OnTable(Keys)
					.OnColumn("DepartmentId").Ascending()
					.OnColumn("AddressKey").Ascending()
					.OnColumn("LoggedOn").Descending();
			}

			if (!Schema.Table(Keys).Index("IX_CallLocationKeys_Department_Coordinates").Exists())
			{
				Create.Index("IX_CallLocationKeys_Department_Coordinates")
					.OnTable(Keys)
					.OnColumn("DepartmentId").Ascending()
					.OnColumn("Latitude").Ascending()
					.OnColumn("Longitude").Ascending();
			}

			if (!Schema.Table(States).Exists())
			{
				Create.Table(States)
					.WithColumn("DepartmentId").AsInt32().NotNullable().PrimaryKey()
					.WithColumn("KeyVersion").AsInt32().NotNullable()
					.WithColumn("NextCallId").AsInt32().Nullable()
					.WithColumn("CompletedOn").AsDateTime2().Nullable()
					.WithColumn("IsSuppressed").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable();
			}

			if (!Schema.Table("CallContacts").Index("IX_CallContacts_DepartmentId_ContactId").Exists())
			{
				Create.Index("IX_CallContacts_DepartmentId_ContactId")
					.OnTable("CallContacts")
					.OnColumn("DepartmentId").Ascending()
					.OnColumn("ContactId").Ascending();
			}

			if (!Schema.Table("CallContacts").Index("IX_CallContacts_CallId").Exists())
			{
				Create.Index("IX_CallContacts_CallId")
					.OnTable("CallContacts")
					.OnColumn("CallId").Ascending();
			}
		}

		public override void Down()
		{
			if (Schema.Table("CallContacts").Index("IX_CallContacts_CallId").Exists())
				Delete.Index("IX_CallContacts_CallId").OnTable("CallContacts");

			if (Schema.Table("CallContacts").Index("IX_CallContacts_DepartmentId_ContactId").Exists())
				Delete.Index("IX_CallContacts_DepartmentId_ContactId").OnTable("CallContacts");

			if (Schema.Table(States).Exists())
				Delete.Table(States);

			if (Schema.Table(Keys).Exists())
				Delete.Table(Keys);
		}
	}
}
