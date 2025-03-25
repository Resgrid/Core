
using System.Diagnostics;
using System.IO;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace System.Web.Mvc
{
	public static class VersionExtension
	{
		public static string ApplicationVersion(this HtmlHelper html)
		{
			string version;

			try
			{
				string path = Path.GetDirectoryName(Reflection.Assembly.GetExecutingAssembly().CodeBase).Replace("file:\\", "");
				version = FileVersionInfo.GetVersionInfo(path + "\\Resgrid.Web.dll").ProductVersion;
			}
			catch (Exception)
			{
				version = "1.0.0.0";
			}

			return version;
		}
	}
}