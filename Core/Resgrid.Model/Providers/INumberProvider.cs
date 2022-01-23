using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Providers
{
	public interface INumberProvider
	{
		Task<List<TextNumber>> GetAvailableNumbers(string country, string areaCode);
		Task<bool> ProvisionNumber(string country, string number);
		string ConvertCountryToCode(string country);
	}
}
