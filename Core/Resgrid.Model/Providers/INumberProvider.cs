using System.Collections.Generic;

namespace Resgrid.Model.Providers
{
	public interface INumberProvider
	{
		List<TextNumber> GetAvailableNumbers(string country, string areaCode);
		bool ProvisionNumber(string country, string number);
		string ConvertCountryToCode(string country);
	}
}