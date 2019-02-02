using System.Collections.Generic;

namespace Resgrid.Model.Services
{
	public interface ITextCommandService
	{
		TextCommandPayload DetermineType(string message);
		TextCommandPayload DetermineType(string message, CustomState customActions, CustomState customStates);
	}
}