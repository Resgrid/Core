using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Resgrid.Localization
{
	public static class SupportedLocales
	{
		public static Dictionary<string, string> SupportedLanguagesMap = new Dictionary<string, string>()
						{
							{"en", "English (United States)"}, //"en-US"
							{"es", "Spanish (Latin America)"}, //"es-MX"
						};

		public static string[] GetSupportedCultures()
		{
			return SupportedLanguagesMap.Select(x => x.Key).ToArray();
		}

		public static List<CultureInfo> GetSupportedCultureInfos()
		{
			return SupportedLanguagesMap.Select(x => new CultureInfo(x.Key)).ToList();
		}

		public static List<CultureInfo> DefaultENCultureInfos()
		{
			var cultures = new List<CultureInfo>();
			cultures.Add(new CultureInfo("en"));

			return cultures;
		}
	}
}
