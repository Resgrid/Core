using System;
using System.Text.RegularExpressions;

namespace Resgrid.Providers.NumberProvider
{
	public static class NumberHelper
	{
		public static bool IsNexmoNumber(string number)
		{
			if (string.IsNullOrWhiteSpace(number))
				return false;

			if (Config.NumberProviderConfig.NexemoNumbers.Contains(number.Trim().ToLower()))
				return true;

			return false;
		}

		public static string TryGetAreaCode(string number)
		{
			if (!String.IsNullOrWhiteSpace(number))
			{
				string digits = Regex.Replace(number, "[^0-9]", x => "");

				// Only the +1 peeps for now
				if (digits.Length == 10)
				{
					return digits.Substring(0, 3);
				}
				else if (digits.Length == 11 && digits[0] == '1')
				{
					digits = digits.Substring(1);
					return digits.Substring(0, 3);
				}
			}

			return String.Empty;
		}
	}
}
