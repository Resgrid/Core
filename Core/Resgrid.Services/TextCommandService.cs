using System;
using System.Collections.Generic;
using System.Globalization;
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

			// SWITCH [department name/number]: change the user's active department. Checked first — the
			// custom staffing branch below claims any message starting with 's' and would swallow it.
			// Ordinal comparison (culture-sensitive lowercasing breaks "SWITCH" under e.g. the Turkish
			// locale) and any whitespace after the command (spaces or tabs) before the argument.
			var trimmedMessage = message.Trim();
			if (trimmedMessage.StartsWith("switch", StringComparison.OrdinalIgnoreCase)
				&& (trimmedMessage.Length == 6 || char.IsWhiteSpace(trimmedMessage[6])))
			{
				payload.Type = TextCommandTypes.SwitchDepartment;
				payload.Data = trimmedMessage.Length > 6 ? trimmedMessage.Substring(6).Trim() : "";
				return payload;
			}

			var customActionOptions = GetActiveOptions(customActions);
			var customStaffingOptions = GetActiveOptions(customStates);

			if (customActionOptions.Any())
			{
				// The department's own statuses, by the commands the HELP reply lists: the option's name without
				// spaces, dashes or colons, or its 1-based position in the list.
				var option = MatchCustomOption(trimmedMessage, customActionOptions, null);
				if (option != null)
				{
					payload.Type = TextCommandTypes.CustomAction;
					payload.Data = option.CustomStateDetailId.ToString();
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

			if (customStaffingOptions.Any())
			{
				// Staffing positions carry an "S" prefix (S1, S2, ...). A name shared with a status option stays
				// the status: statuses are matched first and change far more often.
				if (payload.Type == TextCommandTypes.None)
				{
					var option = MatchCustomOption(trimmedMessage, customStaffingOptions, "s");
					if (option != null)
					{
						payload.Type = TextCommandTypes.CustomStaffing;
						payload.Data = option.CustomStateDetailId.ToString();
					}
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

			// Wanting to stop receiving text messages. Explicit words only (STOP/UNSUBSCRIBE) —
			// END/QUIT/CANCEL must not trigger the opt-out flow; they carry other meanings.
			if (message.Trim().ToLower() == "stop" || message.Trim().ToLower() == "unsubscribe")
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

		/// <summary>
		/// The text command for a custom status or staffing option, as the HELP reply prints it: the option's name
		/// lower-cased without spaces, dashes or colons.
		/// </summary>
		public static string CompactCommandName(string buttonText)
		{
			if (String.IsNullOrWhiteSpace(buttonText))
				return String.Empty;

			return buttonText.Trim().Replace(" ", "").Replace("-", "").Replace(":", "").ToLowerInvariant();
		}

		private static List<CustomStateDetail> GetActiveOptions(CustomState state)
		{
			if (state == null || state.IsDeleted)
				return new List<CustomStateDetail>();

			return state.GetActiveDetails() ?? new List<CustomStateDetail>();
		}

		private static CustomStateDetail MatchCustomOption(string message, List<CustomStateDetail> options, string positionPrefix)
		{
			var text = message.Trim().ToLowerInvariant();
			var positionText = text;

			if (positionPrefix != null)
				positionText = text.StartsWith(positionPrefix, StringComparison.Ordinal) ? text.Substring(positionPrefix.Length) : null;

			if (positionText != null && int.TryParse(positionText, NumberStyles.None, CultureInfo.InvariantCulture, out var position) &&
				position >= 1 && position <= options.Count)
				return options[position - 1];

			var compact = CompactCommandName(text);
			if (compact.Length == 0)
				return null;

			return options.FirstOrDefault(x => CompactCommandName(x.ButtonText) == compact);
		}
	}
}
