using System.Collections.Generic;

namespace Resgrid.Config
{
	public static class InfoConfig
	{
		public static int ConfigVersion = 0;

		public static string UnitAppKey = "UnitAppKey";

		public static string ResponderAppKey = "ResponderAppKey";

		public static string DispatchAppKey = "DispatchAppKey";

		public static string BigBoardKey = "BigBoardKey";

		public static string ICAppKey = "ICAppKey";

		public static string ApiKey = "ApiKey";

		public static string WebsiteKey = "WebsiteKey";

		public static string RelayAppKey = "RelayAppKey";

		public static string EmailProcessorKey = "EmailProcessorKey";

		public static List<ResgridSystemLocation> Locations = new List<ResgridSystemLocation>()
		{
			new ResgridSystemLocation()
			{
				Name = "US-West",
				DisplayName = "Resgrid North America",
				LocationInfo =
					"This is the Resgrid system hosted in the Western United States (private datacenter). This system services most Resgrid customers.",
				IsDefault = true,
				ApiUrl = "https://api.resgrid.com",
				AllowsFreeAccounts = true
			},
			new ResgridSystemLocation()
			{
				Name = "EU-Central",
				DisplayName = "Resgrid Europe",
				LocationInfo =
					"This is the Resgrid system hosted in Central Europe (on OVH). This system services Resgrid customers in the European Union to help with data compliance requirements.",
				IsDefault = false,
				ApiUrl = "https://api.eu.resgrid.com",
				AllowsFreeAccounts = false
			}
		};
	}

	public class ResgridSystemLocation
	{
		public string Name { get; set; }
		public string DisplayName { get; set; }
		public string LocationInfo { get; set; }
		public bool IsDefault { get; set; }
		public string ApiUrl { get; set; }
		public bool AllowsFreeAccounts { get; set; }
	}
}
