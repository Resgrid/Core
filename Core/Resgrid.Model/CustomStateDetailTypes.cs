namespace Resgrid.Model
{
	public enum CustomStateDetailTypes
	{
		None = 0,
		Stations = 1,
		Calls = 2,
		CallsAndStations = 3,
		Pois = 4,
		CallsAndPois = 5,
		StationsAndPois = 6,
		CallsStationsAndPois = 7
	}

	public static class CustomStateDetailTypeExtensions
	{
		public static bool SupportsCalls(this int detailType)
		{
			return ((CustomStateDetailTypes)detailType).SupportsCalls();
		}

		public static bool SupportsStations(this int detailType)
		{
			return ((CustomStateDetailTypes)detailType).SupportsStations();
		}

		public static bool SupportsPois(this int detailType)
		{
			return ((CustomStateDetailTypes)detailType).SupportsPois();
		}

		public static bool SupportsCalls(this CustomStateDetailTypes detailType)
		{
			return detailType == CustomStateDetailTypes.Calls
				   || detailType == CustomStateDetailTypes.CallsAndStations
				   || detailType == CustomStateDetailTypes.CallsAndPois
				   || detailType == CustomStateDetailTypes.CallsStationsAndPois;
		}

		public static bool SupportsStations(this CustomStateDetailTypes detailType)
		{
			return detailType == CustomStateDetailTypes.Stations
				   || detailType == CustomStateDetailTypes.CallsAndStations
				   || detailType == CustomStateDetailTypes.StationsAndPois
				   || detailType == CustomStateDetailTypes.CallsStationsAndPois;
		}

		public static bool SupportsPois(this CustomStateDetailTypes detailType)
		{
			return detailType == CustomStateDetailTypes.Pois
				   || detailType == CustomStateDetailTypes.CallsAndPois
				   || detailType == CustomStateDetailTypes.StationsAndPois
				   || detailType == CustomStateDetailTypes.CallsStationsAndPois;
		}
	}
}
