using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.Localization;
using Resgrid.Chatbot.Models;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Messages;
using Resgrid.Model.Services;

namespace Resgrid.Chatbot.Services
{
	public class TextResponseResolver : ITextResponseResolver
	{
		private const int MaxMessagesToScan = 25;

		private readonly IMessageService _messageService;
		private readonly ICalendarService _calendarService;
		private readonly IAuthorizationService _authorizationService;

		public TextResponseResolver(IMessageService messageService, ICalendarService calendarService,
			IAuthorizationService authorizationService)
		{
			_messageService = messageService;
			_calendarService = calendarService;
			_authorizationService = authorizationService;
		}

		public async Task<IReadOnlyList<PendingTextResponse>> GetPendingResponsesAsync(string userId,
			int departmentId, DateTime sinceUtc)
		{
			var pending = new List<PendingTextResponse>();
			var inbox = await _messageService.GetInboxMessagesByUserIdAsync(userId);
			var candidates = inbox?
				.Where(m => !m.IsDeleted && m.SentOn >= sinceUtc
					&& (m.ExpireOn == null || m.ExpireOn > DateTime.UtcNow)
					&& (m.Type == (int)MessageTypes.Poll || m.Type == (int)MessageTypes.CalendarRsvp))
				.OrderByDescending(m => m.SentOn)
				.Take(MaxMessagesToScan)
				.ToList();

			if (candidates == null)
				return pending;

			var seenCalendarItems = new HashSet<int>();
			foreach (var message in candidates)
			{
				var recipient = await _messageService.GetMessageRecipientByMessageAndUserAsync(message.MessageId, userId);
				if (recipient == null || recipient.IsDeleted || !string.IsNullOrWhiteSpace(recipient.Response))
					continue;

				if (message.Type == (int)MessageTypes.Poll)
				{
					pending.Add(new PendingTextResponse
					{
						Type = PendingTextResponseType.Poll,
						SourceId = message.MessageId,
						MessageId = message.MessageId,
						Label = CleanLabel(message.Subject, "Poll", "Poll:")
					});
					continue;
				}

				if (!TextResponsePromptMetadata.TryGetCalendarItemId(recipient.Note, out var calendarItemId)
					|| !seenCalendarItems.Add(calendarItemId))
					continue;

				var calendarItem = await _calendarService.GetCalendarItemByIdAsync(calendarItemId);
				if (calendarItem == null || calendarItem.DepartmentId != departmentId
					|| calendarItem.SignupType != (int)CalendarItemSignupTypes.RSVP
					|| await _calendarService.GetCalendarItemAttendeeByUserAsync(calendarItemId, userId) != null
					|| !await _authorizationService.CanUserCheckInToCalendarEventAsync(userId, calendarItemId))
					continue;

				pending.Add(new PendingTextResponse
				{
					Type = PendingTextResponseType.CalendarRsvp,
					SourceId = calendarItemId,
					MessageId = message.MessageId,
					Label = CleanLabel(calendarItem.Title, "Calendar event")
				});
			}

			return pending;
		}

		public async Task<ChatbotResponse> RecordResponseAsync(PendingTextResponse target, string answer,
			ChatbotSession session)
		{
			if (target == null || session == null || string.IsNullOrWhiteSpace(answer))
				return null;

			var recipient = await _messageService.GetMessageRecipientByMessageAndUserAsync(target.MessageId, session.UserId);
			if (recipient == null || recipient.IsDeleted || !string.IsNullOrWhiteSpace(recipient.Response))
				return null;

			if (target.Type == PendingTextResponseType.Poll)
			{
				recipient.Response = answer;
				recipient.ReadOn ??= DateTime.UtcNow;
				await _messageService.SaveMessageRecipientAsync(recipient);

				return new ChatbotResponse
				{
					Text = ChatbotResources.Get("Poll_ResponseRecorded", session.Culture, answer, target.Label),
					Processed = true
				};
			}

			var calendarItem = await _calendarService.GetCalendarItemByIdAsync(target.SourceId);
			if (calendarItem == null || calendarItem.DepartmentId != session.DepartmentId
				|| calendarItem.SignupType != (int)CalendarItemSignupTypes.RSVP
				|| await _calendarService.GetCalendarItemAttendeeByUserAsync(target.SourceId, session.UserId) != null
				|| !await _authorizationService.CanUserCheckInToCalendarEventAsync(session.UserId, target.SourceId))
				return null;

			var isYes = string.Equals(answer, "Yes", StringComparison.OrdinalIgnoreCase);
			var attendeeType = isYes
				? (int)CalendarItemAttendeeTypes.RSVP
				: (int)CalendarItemAttendeeTypes.NotAttending;
			var normalizedAnswer = isYes ? "Yes" : "No";

			await _calendarService.SignupForEvent(calendarItem.CalendarItemId, session.UserId,
				normalizedAnswer, attendeeType);

			recipient.Response = normalizedAnswer;
			recipient.ReadOn ??= DateTime.UtcNow;
			await _messageService.SaveMessageRecipientAsync(recipient);

			return new ChatbotResponse
			{
				Text = ChatbotResources.Get("Cal_RsvpDone", session.Culture, normalizedAnswer, calendarItem.Title),
				Processed = true
			};
		}

		private static string CleanLabel(string value, string fallback, string prefix = null)
		{
			if (string.IsNullOrWhiteSpace(value))
				return fallback;

			var label = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
			if (!string.IsNullOrWhiteSpace(prefix)
				&& label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
				label = label.Substring(prefix.Length).Trim();

			return label.Truncate(100);
		}
	}
}
