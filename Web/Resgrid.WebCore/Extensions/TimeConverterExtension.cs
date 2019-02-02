using System;
using Resgrid.Model;
using Resgrid.Model.Helpers;

namespace Microsoft.AspNetCore.Mvc.ViewFeatures
{
	public static class TimeConverterExtension
	{
		public static DateTime TimeConverter(this HtmlHelper html, DateTime timestamp, Department department)
		{
			return TimeConverterHelper.TimeConverter(timestamp, department);
		}

		public static string TimeConverterToString(this HtmlHelper html, DateTime timestamp, Department department)
		{
			return TimeConverterHelper.TimeConverterToString(timestamp, department);
		}
	}
}