using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Web.Helpers
{
	public static class IpAddressHelper
	{
		public static string GetRequestIP(HttpRequest request, bool tryUseXForwardHeader = true)
		{
			string ip = null;

			// todo support new "Forwarded" header (2014) https://en.wikipedia.org/wiki/X-Forwarded-For

			// X-Forwarded-For (csv list):  Using the First entry in the list seems to work
			// for 99% of cases however it has been suggested that a better (although tedious)
			// approach might be to read each IP from right to left and use the first public IP.
			// http://stackoverflow.com/a/43554000/538763
			//
			if (tryUseXForwardHeader)
				ip = GetHeaderValueAs<string>(request, "X-Forwarded-For").SplitCsv().FirstOrDefault();

			// RemoteIpAddress is always null in DNX RC1 Update1 (bug).
			if (ip.IsNullOrWhitespace() && request.HttpContext?.Connection?.RemoteIpAddress != null)
				ip = request.HttpContext.Connection.RemoteIpAddress.ToString();

			if (ip.IsNullOrWhitespace())
				ip = GetHeaderValueAs<string>(request, "REMOTE_ADDR");

			// _httpContextAccessor.HttpContext?.Request?.Host this is the local host.

			if (ip.IsNullOrWhitespace())
				throw new Exception("Unable to determine caller's IP.");

			return ip;
		}

		public static T GetHeaderValueAs<T>(HttpRequest request, string headerName)
		{
			StringValues values;

			if (request.HttpContext?.Request?.Headers?.TryGetValue(headerName, out values) ?? false)
			{
				string rawValues = values.ToString();   // writes out as Csv when there are multiple.

				if (!rawValues.IsNullOrWhitespace())
					return (T)Convert.ChangeType(values.ToString(), typeof(T));
			}
			return default(T);
		}

		public static List<string> SplitCsv(this string csvList, bool nullOrWhitespaceInputReturnsNull = false)
		{
			if (string.IsNullOrWhiteSpace(csvList))
				return nullOrWhitespaceInputReturnsNull ? null : new List<string>();

			return csvList
				.TrimEnd(',')
				.Split(',')
				.AsEnumerable<string>()
				.Select(s => s.Trim())
				.ToList();
		}

		public static bool IsNullOrWhitespace(this string s)
		{
			return String.IsNullOrWhiteSpace(s);
		}
	}
}
