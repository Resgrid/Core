using System;
using Resgrid.Model;
using Resgrid.Model.Services;
using System.Linq;

namespace Resgrid.Services
{
	public class TextCommandService : ITextCommandService
	{
		public TextCommandPayload DetermineType(string message)
		{
			return DetermineType(message, null, null);
		}

		public TextCommandPayload DetermineType(string message, CustomState customActions, CustomState customStates)
		{
			var payload = new TextCommandPayload();
			payload.Type = TextCommandTypes.None;

			if (String.IsNullOrWhiteSpace(message))
				return payload;

			if (customActions != null && customActions.IsDeleted == false && customActions.GetActiveDetails() != null && customActions.GetActiveDetails().Any())
			{
				int actionId = 0;
				if (int.TryParse(message.Trim(), out actionId))
				{
					payload.Type = TextCommandTypes.CustomAction;
					payload.Data = actionId.ToString();
				}
			}
			else
			{
				if (message.Trim().ToLower() == "responding" || message.Trim().ToLower() == "1")
				{
					payload.Type = TextCommandTypes.Action;
					payload.Data = ((int)ActionTypes.Responding).ToString();
				}

				if (message.Trim().ToLower() == "notresponding" || message.Trim().ToLower() == "not responding" || message.Trim().ToLower() == "2")
				{
					payload.Type = TextCommandTypes.Action;
					payload.Data = ((int)ActionTypes.NotResponding).ToString();
				}

				if (message.Trim().ToLower() == "onscene" || message.Trim().ToLower() == "on scene" || message.Trim().ToLower() == "3")
				{
					payload.Type = TextCommandTypes.Action;
					payload.Data = ((int)ActionTypes.OnScene).ToString();
				}

				if (message.Trim().ToLower() == "standingby" || message.Trim().ToLower() == "4")
				{
					payload.Type = TextCommandTypes.Action;
					payload.Data = ((int)ActionTypes.StandingBy).ToString();
				}
			}

			if (customStates != null && customStates.IsDeleted == false && customStates.GetActiveDetails() != null && customStates.GetActiveDetails().Any())
			{
				if (message.Trim().ToLower()[0] == char.Parse("s"))
				{
					payload.Type = TextCommandTypes.CustomStaffing;
					payload.Data = message.Trim().ToLower().Replace("c", "");
				}
			}
			else
			{
				if (message.Trim().ToLower() == "available" || message.Trim().ToLower() == "s1")
				{
					payload.Type = TextCommandTypes.Staffing;
					payload.Data = ((int)UserStateTypes.Available).ToString();
				}

				if (message.Trim().ToLower() == "delayed" || message.Trim().ToLower() == "s2")
				{
					payload.Type = TextCommandTypes.Staffing;
					payload.Data = ((int)UserStateTypes.Delayed).ToString();
				}

				if (message.Trim().ToLower() == "unavailable" || message.Trim().ToLower() == "s3")
				{
					payload.Type = TextCommandTypes.Staffing;
					payload.Data = ((int)UserStateTypes.Unavailable).ToString();
				}

				if (message.Trim().ToLower() == "committed" || message.Trim().ToLower() == "s4")
				{
					payload.Type = TextCommandTypes.Staffing;
					payload.Data = ((int)UserStateTypes.Committed).ToString();
				}

				if (message.Trim().ToLower() == "onshift" || message.Trim().ToLower() == "on shift" || message.Trim().ToLower() == "s5")
				{
					payload.Type = TextCommandTypes.Staffing;
					payload.Data = ((int)UserStateTypes.OnShift).ToString();
				}
			}

			if (message.Trim().ToLower() == "help" || message.Trim().ToLower() == "info")
				payload.Type = TextCommandTypes.Help;

			// Wanting to stop recieving text messages
			if (message.Trim().ToLower() == "stop" || message.Trim().ToLower() == "end" || message.Trim().ToLower() == "quit" || message.Trim().ToLower() == "cancel" || message.Trim().ToLower() == "unsubscribe")
				payload.Type = TextCommandTypes.Stop;

			if (message.Trim().ToLower() == "calls")
				payload.Type = TextCommandTypes.Calls;

			if (message.Trim().ToLower() == "units")
				payload.Type = TextCommandTypes.Units;

			if (message.Trim().ToLower() == "status")
				payload.Type = TextCommandTypes.MyStatus;

			// Call Detail
			if (string.IsNullOrWhiteSpace(payload.Data) && payload.Type == TextCommandTypes.None)
			{
				if (message.Trim().ToLower()[0] == char.Parse("c"))
				{
					payload.Type = TextCommandTypes.CallDetail;
					payload.Data = message.Trim().ToLower().Replace("c", "");
				}
			}

			return payload;
		}
	}
}
