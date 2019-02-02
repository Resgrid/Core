namespace Resgrid.Repositories.DataRepository.Migrations
{
	using System;
	using System.Data.Entity.Migrations;

	public partial class AddedUnitLogsTable : DbMigration
	{
		public override void Up()
		{
			CreateTable(
				"dbo.UnitLogs",
				c => new
					{
						UnitLogId = c.Int(nullable: false, identity: true),
						UnitId = c.Int(nullable: false),
						Timestamp = c.DateTime(nullable: false),
						Narrative = c.String(nullable: false),
					})
				.PrimaryKey(t => t.UnitLogId)
				.ForeignKey("dbo.Units", t => t.UnitId, cascadeDelete: true)
				.Index(t => t.UnitId);

		}

		public override void Down()
		{
			DropIndex("dbo.UnitLogs", new[] { "UnitId" });
			DropForeignKey("dbo.UnitLogs", "UnitId", "dbo.Units");
			DropTable("dbo.UnitLogs");
		}
	}
}
