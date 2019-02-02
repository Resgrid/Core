using System.Data.Entity;
using Resgrid.Repositories.DataRepository.Contexts;

namespace Resgrid.Repositories.DataRepository.Initialization
{
	public class ResgridTestDbInitializer : IDatabaseInitializer<DataContext>
	{
		public void InitializeDatabase(DataContext context)
		{
			//if (context.Database.Exists())
			//	context.Database.Delete();

			//context.Database.CreateIfNotExists();
		}
	}
}
