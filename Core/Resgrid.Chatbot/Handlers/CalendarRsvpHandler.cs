using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.Localization;
using Resgrid.Chatbot.Models;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Chatbot.Handlers
{
	/// <summary>
	/// RSVPs the user to a calendar event (intent <see cref="ChatbotIntentType.RsvpCalendar"/>). Event
	/// resolved by number/name within the user's department (anti-IDOR §3) + authorization (§2). Responses
	/// are localized to the user's culture.
	/// </summary>
	public class CalendarRsvpHandler : IChatbotActionHandler
	{
		private readonly ICalendarService _calendarService;
		private readonly IAuthorizationService _authorizationService;

		public CalendarRsvpHandler(ICalendarService calendarService, IAuthorizationService authorizationService)
		{
			_calendarService = calendarService;
			_authorizationService = authorizationService;
		}

		public ChatbotIntentType IntentType => ChatbotIntentType.RsvpCalendar;

		public async Task<ChatbotResponse> HandleAsync(ChatbotMessage message, ChatbotIntent intent, ChatbotSession session)
		{
			var culture = session.Culture;
			try
			{
				intent.Parameters.TryGetValue("response", out var response);
				intent.Parameters.TryGetValue("eventId", out var eventRef);

				if (string.IsNullOrWhiteSpace(response) || string.IsNullOrWhiteSpace(eventRef))
					return new ChatbotResponse { Text = ChatbotResources.Get("Cal_RsvpUsage", culture), Processed = false };

				CalendarItem calendarItem = null;
				if (int.TryParse(eventRef.Trim().TrimStart('#'), out var eventId))
				{
					var byId = await _calendarService.GetCalendarItemByIdAsync(eventId);
					if (byId != null && byId.DepartmentId == session.DepartmentId)
						calendarItem = byId;
				}
				else
				{
					var upcoming = await _calendarService.GetUpcomingCalendarItemsAsync(session.DepartmentId, DateTime.UtcNow);
					var q = eventRef.Trim().ToLowerInvariant();
					var matches = (upcoming ?? new List<CalendarItem>())
						.Where(i => i.Title != null && i.Title.ToLowerInvariant().Contains(q))
						.ToList();

					if (matches.Count == 1)
						calendarItem = matches[0];
					else if (matches.Count > 1)
						return new ChatbotResponse { Text = ChatbotResources.Get("Cal_MultipleMatch", culture, eventRef), Processed = true };
				}

				if (calendarItem == null)
					return new ChatbotResponse { Text = ChatbotResources.Get("Cal_EventNotFound", culture, eventRef), Processed = true };

				if (!await _authorizationService.CanUserCheckInToCalendarEventAsync(session.UserId, calendarItem.CalendarItemId))
					return new ChatbotResponse { Text = ChatbotResources.Get("Cal_NotAuthorized", culture), Processed = false };

				var (attendeeType, note) = response.Trim().ToLowerInvariant() switch
				{
					"yes" => ((int)CalendarItemAttendeeTypes.RSVP, "Yes"),
					"no" => ((int)CalendarItemAttendeeTypes.NotAttending, "No"),
					"maybe" => ((int)CalendarItemAttendeeTypes.Optional, "Maybe"),
					_ => ((int)CalendarItemAttendeeTypes.RSVP, response)
				};

				await _calendarService.SignupForEvent(calendarItem.CalendarItemId, session.UserId, note, attendeeType);

				return new ChatbotResponse { Text = ChatbotResources.Get("Cal_RsvpDone", culture, note, calendarItem.Title), Processed = true };
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
				return new ChatbotResponse { Text = ChatbotResources.Get("Cal_ErrorRsvp", culture), Processed = false };
			}
		}
	}
}
