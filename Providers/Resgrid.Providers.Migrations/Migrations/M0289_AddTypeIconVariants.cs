using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Status and priority map icons (extends M0285's type icons): TypeIcons.Variant names the status or priority an icon
	/// stands in for (p{priority} on a call type, s{built-in status} or o{custom option id} on a unit type or role), NULL for
	/// the type's own icon, so every existing row stays the type's own icon. Only the own icon is mirrored on the type.
	/// </summary>
	[Migration(289)]
	public class M0289_AddTypeIconVariants : Migration
	{
		public override void Up()
		{
			Execute.Sql("IF COL_LENGTH('TypeIcons', 'Variant') IS NULL ALTER TABLE [TypeIcons] ADD [Variant] nvarchar(32) NULL;");
		}

		public override void Down()
		{
			if (Schema.Table("TypeIcons").Column("Variant").Exists())
				Delete.Column("Variant").FromTable("TypeIcons");
		}
	}
}
