namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class UpdatedRequiredFieldsForTraining : DbMigration
    {
        public override void Up()
        {
            AlterColumn("dbo.Trainings", "Name", c => c.String(nullable: false));
            AlterColumn("dbo.Trainings", "Description", c => c.String(nullable: false));
            AlterColumn("dbo.Trainings", "TrainingText", c => c.String(nullable: false));
        }
        
        public override void Down()
        {
            AlterColumn("dbo.Trainings", "TrainingText", c => c.String());
            AlterColumn("dbo.Trainings", "Description", c => c.String());
            AlterColumn("dbo.Trainings", "Name", c => c.String());
        }
    }
}
