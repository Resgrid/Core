using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

namespace Resgrid.Framework
{
	public static class TypeExtensions
	{
		public static string ProductVersion(this Type self)
		{
			var fileVersionInfo = FileVersionInfo.GetVersionInfo(self.Assembly.Location);
			return fileVersionInfo.ProductVersion;
		}

		public static T GetAttribute<T>(this ICustomAttributeProvider type, bool inherit = true) where T : class
		{
			if (type == null)
				return null;

			return type.GetCustomAttributes(typeof (T), inherit).FirstOrDefault() as T;
		}

		public static bool HasAttribute<T>(this ICustomAttributeProvider type, bool inherit = true) where T : class
		{
			if (type == null)
				return false;

			return type.GetAttribute<T>(inherit) != null;
		}
	}
}
