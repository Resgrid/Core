using System;

namespace GoogleMapsApi.Engine
{
	public static class UnixTimeConverter
	{
		private static DateTime epoch = new DateTime(1970, 1, 1);

		/// <summary>
		/// Converts a DateTime to a Unix timestamp
		/// </summary>
		public static int DateTimeToUnixTimestamp(DateTime dateTime)
		{
			return (int)(dateTime - epoch.ToLocalTime()).TotalSeconds;
		}
	}
}