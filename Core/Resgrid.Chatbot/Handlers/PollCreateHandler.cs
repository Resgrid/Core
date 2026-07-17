using System;
using System.Linq;
using System.Text.RegularExpressions;
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
	/// Creates a yes/no poll message to all department members (intent <see cref="ChatbotIntentType.CreatePoll"/>),
	/// e.g. "poll members to see who's available for a red flag on 7/22". Department-admin only (it texts
	/// the whole roster), with a confirmation pass before sending (security addendum §5): the session parks
	/// in AwaitingConfirmation and the ingress re-dispatches with "__confirmed" on YES. Recipients answer by
	/// replying YES or NO (bare replies resolve against the outstanding poll — see ChatbotIngressService).
	/// </summary>
	public class PollCreateHandler : IChatbotActionHandler
	{
		// "poll members to see who's available ..." — audience/verb filler ahead of the actual question.
		private static readonly Regex LeadingFillerRegex = new Regex(
			@"^((the\s+)?(members|everyone|everybody|all|personnel|department|dept)\s+)?((to\s+)?(see|ask|know|find\s+out)\s+)?(if\s+|whether\s+)?",
			RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(200));

		private readonly IMessageService _messageService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IUserProfileService _userProfileService;

		public PollCreateHandler(
			IMessageService messageService,
			IDepartmentsService departmentsService,
			IUserProfileService userProfileService)
		{
			_messageService = messageService;
			_departmentsService = departmentsService;
			_userProfileService = userProfileService;
		}

		public ChatbotIntentType IntentType => ChatbotIntentType.CreatePoll;

		public async Task<ChatbotResponse> HandleAsync(ChatbotMessage message, ChatbotIntent intent, ChatbotSession session)
		{
			var culture = session.Culture;
			try
			{
				intent.Parameters.TryGetValue("question", out var question);
				question = CleanQuestion(question);

				if (string.IsNullOrWhiteSpace(question))
					return new ChatbotResponse { Text = ChatbotResources.Get("Poll_Usage", culture), Processed = false };

				// Polling texts the entire roster, so only department admins may do it.
				var department = await _departmentsService.GetDepartmentByIdAsync(session.DepartmentId);
				var member = await _departmentsService.GetDepartmentMemberAsync(session.UserId, session.DepartmentId);
				var isAdmin = department?.IsUserAnAdmin(session.UserId) == true || member?.IsAdmin == true;
				if (!isAdmin)
					return new ChatbotResponse { Text = ChatbotResources.Get("Poll_NoPermission", culture), Processed = false };

				var users = await _departmentsService.GetAllUsersForDepartmentAsync(session.DepartmentId);
				var recipients = users?.Where(u => u.UserId != session.UserId).Select(u => u.UserId).Distinct().ToList();
				if (recipients == null || recipients.Count == 0)
					return new ChatbotResponse { Text = ChatbotResources.Get("Poll_NoMembers", culture), Processed = true };

				var confirmed = intent.Parameters.TryGetValue("__confirmed", out var confirmFlag) && confirmFlag == "true";
				if (!confirmed)
				{
					session.State = ChatbotDialogState.AwaitingConfirmation;
					session.PendingIntent = ChatbotIntentType.CreatePoll;
					session.Context["question"] = question;
					return new ChatbotResponse
					{
						Text = ChatbotResources.Get("Poll_Confirm", culture, question, recipients.Count),
						Processed = true
					};
				}

				var profile = await _userProfileService.GetProfileByUserIdAsync(session.UserId);
				var senderName = profile?.FullName?.AsFirstNameLastName ?? "Chatbot";

				var msg = new Message
				{
					// The subject leads the SMS rendering ("{subject} via Resgrid : {body}"), so it carries
					// the poll marker; the body carries the question and the reply instructions.
					Subject = ("Poll: " + question).Truncate(150),
					Body = (question + "\nReply YES or NO.").Truncate(4000),
					SendingUserId = session.UserId,
					SentOn = DateTime.UtcNow,
					IsBroadcast = true,
					Type = (int)MessageTypes.Poll
				};

				foreach (var userId in recipients)
					msg.AddRecipient(userId);

				var saved = await _messageService.SaveMessageAsync(msg);
				await _messageService.SendMessageAsync(saved, senderName, session.DepartmentId);

				return new ChatbotResponse { Text = ChatbotResources.Get("Poll_Sent", culture, recipients.Count), Processed = true };
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
				return new ChatbotResponse { Text = ChatbotResources.Get("Poll_Error", culture), Processed = false };
			}
		}

		private static string CleanQuestion(string question)
		{
			if (string.IsNullOrWhiteSpace(question))
				return null;

			var cleaned = question.Trim();
			try
			{
				cleaned = LeadingFillerRegex.Replace(cleaned, string.Empty, 1).Trim();
			}
			catch (RegexMatchTimeoutException)
			{
				// Keep the raw question when the filler strip times out.
			}

			return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
		}
	}
}
