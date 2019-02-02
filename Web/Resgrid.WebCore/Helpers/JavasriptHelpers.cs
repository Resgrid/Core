namespace Resgrid.Web.Helpers
{
	public static class JavasriptHelpers
	{
		public static long GetJavascriptTimestamp(System.DateTime input)
		{
			System.TimeSpan span = new System.TimeSpan(System.DateTime.Parse("1/1/1970").Ticks);
			System.DateTime time = input.Subtract(span);
			return (long) (time.Ticks/10000);
		}
	}
}