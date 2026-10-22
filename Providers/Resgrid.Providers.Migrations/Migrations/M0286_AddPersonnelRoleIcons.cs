using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Personnel role map icons (extends M0285's type icons to roles).
	///  * PersonnelRoles.IconKey mirrors the role's TypeIcons row (TypeKind 4), like UnitTypes / CallTypes / POITypes.
	///  * PersonnelRoles.SortOrder is the department's role order, set on the Roles page; a person's map pin uses the icon
	///    of the first role they hold, in this order, that has one. 0 for every existing role, so they keep listing by name
	///    until a department orders them.
	/// </summary>
	[Migration(286)]
	public class M0286_AddPersonnelRoleIcons : Migration
	{
		public override void Up()
		{
			Execute.Sql("IF COL_LENGTH('PersonnelRoles', 'IconKey') IS NULL ALTER TABLE [PersonnelRoles] ADD [IconKey] nvarchar(64) NULL;");
			Execute.Sql("IF COL_LENGTH('PersonnelRoles', 'SortOrder') IS NULL ALTER TABLE [PersonnelRoles] ADD [SortOrder] int NOT NULL CONSTRAINT [DF_PersonnelRoles_SortOrder] DEFAULT 0;");
		}

		public override void Down()
		{
			if (Schema.Table("PersonnelRoles").Column("SortOrder").Exists())
			{
				Execute.Sql("IF OBJECT_ID('DF_PersonnelRoles_SortOrder', 'D') IS NOT NULL ALTER TABLE [PersonnelRoles] DROP CONSTRAINT [DF_PersonnelRoles_SortOrder];");
				Delete.Column("SortOrder").FromTable("PersonnelRoles");
			}

			if (Schema.Table("PersonnelRoles").Column("IconKey").Exists())
				Delete.Column("IconKey").FromTable("PersonnelRoles");
		}
	}
}
