using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Custom unit, call and POI type icons (customer request: tell an ambulance from a MUG or PIT at a glance in unit
	/// lists; extended to call and POI types).
	///  * TypeIcons holds the department's uploaded icon for a type (TypeKind 1 unit, 2 call, 3 POI; TypeId the type's id),
	///    re-encoded server side to a square, transparent PNG. One row per type; IconKey is the opaque random key the
	///    anonymous icon endpoint serves it under, regenerated on every upload so the URL is immutable.
	///  * UnitTypes, CallTypes and POITypes gain IconKey, mirroring the current key so lists build the icon URL without
	///    reading bytes.
	/// No foreign keys: the type deletes and the department purge (UnlinkedDepartmentCleanup) remove the rows.
	/// </summary>
	[Migration(285)]
	public class M0285_AddTypeIcons : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("TypeIcons").Exists())
			{
				Create.Table("TypeIcons")
					.WithColumn("TypeIconId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("TypeKind").AsInt32().NotNullable()
					.WithColumn("TypeId").AsInt32().NotNullable()
					.WithColumn("IconKey").AsString(64).NotNullable()
					.WithColumn("ContentType").AsString(100).Nullable()
					.WithColumn("Width").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Height").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ByteSize").AsInt64().NotNullable().WithDefaultValue(0L)
					.WithColumn("Checksum").AsString(128).Nullable()
					.WithColumn("Data").AsBinary(int.MaxValue).Nullable()
					.WithColumn("UploadedByUserId").AsString(128).Nullable()
					.WithColumn("UploadedOn").AsDateTime2().NotNullable();

				Create.Index("UX_TypeIcons_IconKey").OnTable("TypeIcons").OnColumn("IconKey").Ascending().WithOptions().Unique();
				Create.Index("IX_TypeIcons_Department_Kind_Type").OnTable("TypeIcons")
					.OnColumn("DepartmentId").Ascending().OnColumn("TypeKind").Ascending().OnColumn("TypeId").Ascending();
			}

			Execute.Sql("IF COL_LENGTH('UnitTypes', 'IconKey') IS NULL ALTER TABLE [UnitTypes] ADD [IconKey] nvarchar(64) NULL;");
			Execute.Sql("IF COL_LENGTH('CallTypes', 'IconKey') IS NULL ALTER TABLE [CallTypes] ADD [IconKey] nvarchar(64) NULL;");
			Execute.Sql("IF COL_LENGTH('POITypes', 'IconKey') IS NULL ALTER TABLE [POITypes] ADD [IconKey] nvarchar(64) NULL;");
		}

		public override void Down()
		{
			foreach (var table in new[] { "UnitTypes", "CallTypes", "POITypes" })
			{
				if (Schema.Table(table).Column("IconKey").Exists())
					Delete.Column("IconKey").FromTable(table);
			}

			if (Schema.Table("TypeIcons").Exists())
				Delete.Table("TypeIcons");
		}
	}
}
