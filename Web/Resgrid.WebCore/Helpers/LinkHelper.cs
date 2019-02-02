namespace Resgrid.Web.Helpers
{
	public static class LinkHelper
	{
		public static string ExtratHref(string url)
		{
			int start = url.IndexOf("href=\"") + 6;
			int end = url.IndexOf("\"", start);

			string fullUrl = url.Substring(start, end - start);

			return fullUrl;
		}
	}
}