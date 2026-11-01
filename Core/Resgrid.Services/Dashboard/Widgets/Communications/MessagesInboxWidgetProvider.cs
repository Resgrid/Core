using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.Communications;
using Resgrid.Model.Services;

namespace Resgrid.Services.Dashboard.Widgets.Communications
{
	/// <summary>
	/// messages.inbox (plan §7.5, §9A): the navbar unread count and the newest unread messages. Subjects resolve per owning
	/// department through ResolveMessagesForReadAsync, exactly as MessagesController.ResolveMessagesByOwningDepartmentAsync
	/// does for the Inbox page (the inbox spans every department the member belongs to); only the rows shown are resolved.
	/// Bodies are never selected into the DTO.
	/// </summary>
	[DashboardWidget("messages.inbox")]
	public class MessagesInboxWidgetProvider : IDashboardWidgetDataProvider
	{
		private readonly IMessageService _messageService;
		private readonly IProtectedReadService _protectedReadService;
		private readonly IUserProfileService _userProfileService;

		public MessagesInboxWidgetProvider(IMessageService messageService, IProtectedReadService protectedReadService, IUserProfileService userProfileService)
		{
			_messageService = messageService;
			_protectedReadService = protectedReadService;
			_userProfileService = userProfileService;
		}

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			var maxItems = context.GetInt("maxItems", 5);
			var unread = await _messageService.GetUnreadInboxMessagesByUserIdAsync(context.UserId) ?? new List<Message>();
			var data = new MessagesInboxWidgetData { UnreadCount = await _messageService.GetUnreadMessagesCountByUserIdAsync(context.UserId) };

			var shown = unread.Where(m => m != null && !m.IsDeleted).OrderByDescending(m => m.SentOn).Take(maxItems).ToList();
			if (shown.Count == 0)
				return data;

			// One resolve per owning department: a grant (and a department key) is per department. The grant on the request
			// belongs to the active department, so other departments' rows resolve without one (placeholders).
			foreach (var group in shown.Where(m => m.DepartmentId.HasValue && m.DepartmentId.Value > 0).GroupBy(m => m.DepartmentId.Value))
			{
				var grant = group.Key == context.DepartmentId ? context.ProtectedGrantToken : null;
				await _protectedReadService.ResolveMessagesForReadAsync(group.Key, group.ToList(), grant, context.UserId, cancellationToken);
			}

			var senderIds = shown.Where(m => !m.SystemGenerated && !string.IsNullOrWhiteSpace(m.SendingUserId))
				.Select(m => m.SendingUserId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			if (senderIds.Count > 0)
			{
				foreach (var profile in await _userProfileService.GetSelectedUserProfilesAsync(senderIds) ?? new List<UserProfile>())
				{
					if (profile?.UserId != null && !names.ContainsKey(profile.UserId))
						names.Add(profile.UserId, profile.FullName?.AsFirstNameLastName);
				}
			}

			var canReveal = context.Viewer?.CanRevealAdp(PermissionTypes.ViewProtectedOperationalData) ?? false;
			foreach (var message in shown)
			{
				data.Items.Add(new MessagesInboxItem
				{
					MessageId = message.MessageId,
					SenderName = !message.SystemGenerated && message.SendingUserId != null && names.TryGetValue(message.SendingUserId, out var name) ? name ?? string.Empty : string.Empty,
					SystemGenerated = message.SystemGenerated,
					// Reveal is offered only for the active department: a grant from this page cannot open another department's rows.
					Subject = WidgetProtectedText.From(message.Subject, canReveal && message.DepartmentId == context.DepartmentId),
					SentOnUtc = message.SentOn,
					Type = message.Type,
					Url = "/User/Messages/ViewMessage?messageId=" + message.MessageId.ToString(CultureInfo.InvariantCulture)
				});
			}

			return data;
		}
	}
}
