using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Member wellness WL1 (§13b): NFPA 1582 medical clearances (RmsMedicalClearances) with their restriction status; the evaluation itself is
	/// a certification of the department's clearance type (PersonnelCertificationId, no foreign key). No foreign keys: RmsDepartmentCleanup
	/// purges it on department deletion (a legal hold defers it). RestrictionDetail (ADP catalog 38) is nvarchar(max). Guarded for a safe
	/// retry; Down drops the table.
	/// </summary>
	[Migration(396)]
	public class M0396_AddMedicalClearances : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("RmsMedicalClearances").Exists())
			{
				Create.Table("RmsMedicalClearances")
					.WithColumn("RmsMedicalClearanceId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("UserId").AsString(128).NotNullable()
					.WithColumn("PersonnelCertificationId").AsInt32().Nullable()
					.WithColumn("Status").AsInt32().NotNullable()
					.WithColumn("EvaluatedOn").AsDateTime2().NotNullable()
					.WithColumn("ValidUntil").AsDateTime2().Nullable()
					.WithColumn("RestrictionDetail").AsString(int.MaxValue).Nullable()
					.WithColumn("VoidedOn").AsDateTime2().Nullable()
					.WithColumn("VoidedByUserId").AsString(128).Nullable()
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedCatalogVersion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("CreatedByUserId").AsString(128).Nullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable()
					.WithColumn("ModifiedByUserId").AsString(128).Nullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(1);
			}

			if (!Schema.Table("RmsMedicalClearances").Index("IX_RmsMedicalClearances_User").Exists())
				Create.Index("IX_RmsMedicalClearances_User").OnTable("RmsMedicalClearances")
					.OnColumn("DepartmentId").Ascending().OnColumn("UserId").Ascending().OnColumn("EvaluatedOn").Ascending();
			if (!Schema.Table("RmsMedicalClearances").Index("IX_RmsMedicalClearances_Evaluated").Exists())
				Create.Index("IX_RmsMedicalClearances_Evaluated").OnTable("RmsMedicalClearances")
					.OnColumn("EvaluatedOn").Ascending().OnColumn("DepartmentId").Ascending();
		}

		public override void Down()
		{
			if (Schema.Table("RmsMedicalClearances").Exists())
				Delete.Table("RmsMedicalClearances");
		}
	}
}
