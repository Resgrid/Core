namespace Resgrid.Config
{
	public static class WorkerConfig
	{
		/// <summary>
		/// The underlying database engine for the worker database (Does not support Mongo)
		/// </summary>
		public static DatabaseTypes DatabaseType = DatabaseTypes.SqlServer;

		public static string WorkerDbConnectionString = "Server=rgdevserver;Database=ResgridWorkers;User Id=resgrid_workers;Password=resgrid123;MultipleActiveResultSets=True;";
		public static string PayloadKey = "XsBYpdbdHkhuGsU3tvTMawyV6d3M2F8EQ8wQ2jVLBREECQmwngACk2hm4Ykb7eW7Qsm6za8RdJBY5Z3xvN6erYry47nJ5XmL";
	}
}
