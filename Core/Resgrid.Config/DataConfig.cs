namespace Resgrid.Config
{
	public class DataConfig
	{
		public static string ConnectionString = "Server=rgdevserver;Database=Resgrid;User Id=resgrid_app;Password=resgrid123;MultipleActiveResultSets=True;TrustServerCertificate=True;";

		public static string NoSqlConnectionString = "mongodb://resgrid:resgrid123@rgdevserver:27017";
		public static string NoSqlDatabaseName = "resgrid";
		public static string NoSqlApplicationName = "Resgrid";

		public static string UsersIdentityRoleId = "38b461d7-e848-46ef-8c06-ece5b618d9d1";
		public static string AdminsIdentityRoleId = "1f6a03a8-62f4-4179-80fc-2eb96266cf04";
		public static string AffiliatesIdentityRoleId = "3aba8863-e46d-40cc-ab86-309f9c3e4f97";
	}
}
