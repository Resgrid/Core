using System.Data.Entity;
using System.Data.Entity.Migrations;
using Resgrid.Repositories.DataRepository.Migrations;

namespace Resgrid.Repositories.DataRepository.Initialization
{
    public class InitalizeDatabase
    {
        public static void Initialize()
        {
            Database.SetInitializer(new MigrateDatabaseToLatestVersion<Contexts.DataContext, Configuration>());
						//var dbMigrator = new DbMigrator(new Configuration());
						//dbMigrator.Update();
        }
    }
}
