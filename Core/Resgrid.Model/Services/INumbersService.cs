using System.Collections.Generic;

namespace Resgrid.Model.Services
{
    public interface INumbersService
    {
		List<TextNumber> GetAvailableNumbers(string country, string areaCode);
        bool ProvisionNumber(int departmentId, string number, string country);
        InboundMessageEvent SaveInboundMessageEvent(InboundMessageEvent messageEvent);
	    bool IsNumberPatternValid(string pattern, string number);
	    bool DoesNumberMatchAnyPattern(List<string> patterns, string number);
    }
}