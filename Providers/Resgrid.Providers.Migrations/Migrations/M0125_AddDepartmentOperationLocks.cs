using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Department operation lock — a department-wide mutation freeze (reads continue) held by the ADP
	/// migration worker during an active overnight migration window, designed as a general platform
	/// mechanism. The filtered unique index enforces at most one active lock per department at the
	/// database, closing the acquire race. Runs outside a migration transaction for the ONLINE index
	/// build; every statement is existence-guarded for safe retry.
	/// </summary>
	[Migration(125, TransactionBehavior.None)]
	public class M0125_AddDepartmentOperationLocks : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("DepartmentOperationLocks").Exists())
				Create.Table("DepartmentOperationLocks")
					.WithColumn("DepartmentOperationLockId").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("LockType").AsInt32().NotNullable()
					.WithColumn("Reason").AsString(512).Nullable()
					.WithColumn("CorrelationId").AsString(128).Nullable()
					.WithColumn("AppliedUtc").AsDateTime2().NotNullable()
					.WithColumn("AppliedByIdentity").AsString(256).Nullable()
					.WithColumn("HeartbeatUtc").AsDateTime2().NotNullable()
					.WithColumn("ExpiresUtc").AsDateTime2().NotNullable()
					.WithColumn("ProjectedEndUtc").AsDateTime2().Nullable()
					.WithColumn("ReleasedUtc").AsDateTime2().Nullable()
					.WithColumn("ReleasedBy").AsString(256).Nullable()
					.WithColumn("ReleaseKind").AsInt32().Nullable();

			// At most one active lock per department, enforced at the database.
			Execute.Sql(SqlServerOnlineIndex.Create("UX_DepartmentOperationLocks_Department_Active",
				"DepartmentOperationLocks", new[] { "[DepartmentId] ASC" }, unique: true,
				filter: "[ReleasedUtc] IS NULL"));

			// Liveness sweep: find active locks whose safety valve has passed.
			Execute.Sql(SqlServerOnlineIndex.Create("IX_DepartmentOperationLocks_Released_Expires",
				"DepartmentOperationLocks", new[] { "[ReleasedUtc] ASC", "[ExpiresUtc] ASC" }));
		}

		public override void Down()
		{
			if (Schema.Table("DepartmentOperationLocks").Exists())
				Delete.Table("DepartmentOperationLocks");
		}
	}
}
