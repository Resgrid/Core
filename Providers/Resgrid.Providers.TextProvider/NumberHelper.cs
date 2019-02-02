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
	}
}
