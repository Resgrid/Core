namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedSomePropertiesToTrainingTables : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.Trainings", "ToBeCompletedBy", c => c.DateTime());
            AddColumn("dbo.TrainingUsers", "Viewed", c => c.Boolean(nullable: false));
            AddColumn("dbo.TrainingUsers", "ViewedOn", c => c.DateTime());
            AddColumn("dbo.TrainingUsers", "CompletedOn", c => c.DateTime());
        }
        
        public override void Down()
        {
            DropColumn("dbo.TrainingUsers", "CompletedOn");
            DropColumn("dbo.TrainingUsers", "ViewedOn");
            DropColumn("dbo.TrainingUsers", "Viewed");
            DropColumn("dbo.Trainings", "ToBeCompletedBy");
        }
    }
}
