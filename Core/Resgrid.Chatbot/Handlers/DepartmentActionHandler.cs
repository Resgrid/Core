using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.Localization;
using Resgrid.Chatbot.Models;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Framework;

namespace Resgrid.Chatbot.Handlers
{
	public class DepartmentActionHandler : IChatbotActionHandler
	{
		private readonly IDepartmentsService _departmentsService;
		private readonly IUserProfileService _userProfileService;
		private readonly IUsersService _usersService;
		private readonly ILimitsService _limitsService;
		private readonly IChatbotDepartmentConfigService _departmentConfigService;

		public DepartmentActionHandler(
			IDepartmentsService departmentsService,
			IUserProfileService userProfileService,
			IUsersService usersService,
			ILimitsService limitsService,
			IChatbotDepartmentConfigService departmentConfigService)
		{
			_departmentsService = departmentsService;
			_userProfileService = userProfileService;
			_usersService = usersService;
			_limitsService = limitsService;
			_departmentConfigService = departmentConfigService;
		}

		/// <summary>
		/// The user's non-deleted memberships restricted to departments the CHATBOT can serve here: on a
		/// plan that supports SMS AND with the chatbot enabled for the current platform (per-department
		/// config; no config row means enabled). Listing/switching to a department whose chatbot is
		/// disabled would strand the user behind the ingress config gate with no in-band way back. The
		/// legacy SMS SWITCH command uses the unfiltered plan-based list — switching is account-wide and
		/// a chatbot-disabled department is still valid for legacy text commands.
		/// </summary>
		private async Task<System.Collections.Generic.List<DepartmentMember>> GetSmsSupportingMembershipsAsync(string userId, ChatbotPlatform platform)
		{
			// Same base list (and order) as the legacy SMS surfaces, then chatbot-eligibility filtering.
			// The ingress restricted mode applies the identical filter, so the numbered options it shows
			// map to the same memberships this handler resolves.
			var supported = await _departmentsService.GetSmsSupportedMembershipsForUserAsync(userId);

			var usable = new System.Collections.Generic.List<DepartmentMember>();
			foreach (var membership in supported)
			{
				if (await _departmentConfigService.IsChatbotUsableForDepartmentAsync(membership.DepartmentId, platform))
					usable.Add(membership);
			}

			return usable;
		}

		public ChatbotIntentType IntentType => ChatbotIntentType.ListDepartments;

		public bool CanHandle(ChatbotIntentType intentType)
		{
			return intentType == ChatbotIntentType.ListDepartments
				|| intentType == ChatbotIntentType.GetActiveDepartment
				|| intentType == ChatbotIntentType.SwitchDepartment;
		}

		public async Task<ChatbotResponse> HandleAsync(ChatbotMessage message, ChatbotIntent intent, ChatbotSession session)
		{
			switch (intent.Type)
			{
				case ChatbotIntentType.ListDepartments:
					return await ListDepartmentsAsync(session);
				case ChatbotIntentType.GetActiveDepartment:
					return await GetActiveDepartmentAsync(session);
				case ChatbotIntentType.SwitchDepartment:
					return await SwitchDepartmentAsync(intent, session);
				default:
					return new ChatbotResponse { Text = ChatbotResources.Get("Dept_UnknownCommand", session.Culture), Processed = false };
			}
		}

		private async Task<ChatbotResponse> ListDepartmentsAsync(ChatbotSession session)
		{
			var culture = session.Culture;
			try
			{
				// Only departments the chatbot can serve here (non-free plan + chatbot enabled for this
				// platform) are switchable, so only list those.
				var activeMemberships = await GetSmsSupportingMembershipsAsync(session.UserId, session.Platform);

				if (activeMemberships.Count == 0)
					return new ChatbotResponse { Text = ChatbotResources.Get("Dept_NoActiveMemberships", culture), Processed = true };

				var sb = new StringBuilder();
				sb.AppendLine(ChatbotResources.Get("Dept_YourDepartments", culture));

				for (int i = 0; i < activeMemberships.Count; i++)
				{
					var membership = activeMemberships[i];
					var dept = await _departmentsService.GetDepartmentByIdAsync(membership.DepartmentId);
					var deptName = dept?.Name ?? ChatbotResources.Get("Dept_DepartmentNum", culture, membership.DepartmentId);
					var activeMarker = membership.IsActive ? ChatbotResources.Get("Dept_ActiveMarker", culture) : "";

					sb.AppendLine(ChatbotResources.Get("Dept_ListItem", culture, i + 1, deptName, activeMarker));
				}

				sb.AppendLine();
				sb.AppendLine(ChatbotResources.Get("Dept_SwitchHint", culture));

				return new ChatbotResponse { Text = sb.ToString().TrimEnd(), Processed = true };
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return new ChatbotResponse { Text = ChatbotResources.Get("Dept_ErrorList", culture), Processed = false };
			}
		}

		private async Task<ChatbotResponse> GetActiveDepartmentAsync(ChatbotSession session)
		{
			var culture = session.Culture;
			try
			{
				var allMemberRecords = await _departmentsService.GetAllDepartmentsForUserAsync(session.UserId);
				if (allMemberRecords == null || allMemberRecords.Count == 0)
					return new ChatbotResponse { Text = ChatbotResources.Get("Dept_NoMembership", culture), Processed = true };

				var activeMembership = allMemberRecords
					.Where(m => !m.IsDeleted && m.IsActive)
					.OrderByDescending(m => m.IsDefault)
					.FirstOrDefault();

				if (activeMembership == null)
				{
					activeMembership = allMemberRecords
						.Where(m => !m.IsDeleted)
						.OrderByDescending(m => m.IsDefault)
						.FirstOrDefault();
				}

				if (activeMembership == null)
					return new ChatbotResponse { Text = ChatbotResources.Get("Dept_NoActiveSet", culture), Processed = true };

				var dept = await _departmentsService.GetDepartmentByIdAsync(activeMembership.DepartmentId);
				var deptName = dept?.Name ?? ChatbotResources.Get("Dept_DepartmentNum", culture, activeMembership.DepartmentId);

				var defaultMarker = activeMembership.IsDefault ? ChatbotResources.Get("Dept_DefaultMarker", culture) : "";
				var activeWord = activeMembership.IsActive
					? ChatbotResources.Get("Dept_Active", culture)
					: ChatbotResources.Get("Dept_NotActive", culture);

				return new ChatbotResponse
				{
					Text = ChatbotResources.Get("Dept_ActiveIs", culture, activeWord, deptName, defaultMarker),
					Processed = true
				};
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return new ChatbotResponse { Text = ChatbotResources.Get("Dept_ErrorActive", culture), Processed = false };
			}
		}

		private async Task<ChatbotResponse> SwitchDepartmentAsync(ChatbotIntent intent, ChatbotSession session)
		{
			var culture = session.Culture;
			try
			{
				var departmentIdentifier = "";

				if (intent.Parameters != null)
				{
					intent.Parameters.TryGetValue("departmentIdentifier", out departmentIdentifier);
					if (string.IsNullOrWhiteSpace(departmentIdentifier))
						intent.Parameters.TryGetValue("departmentName", out departmentIdentifier);
					if (string.IsNullOrWhiteSpace(departmentIdentifier))
						intent.Parameters.TryGetValue("departmentId", out departmentIdentifier);
				}

				if (string.IsNullOrWhiteSpace(departmentIdentifier) && intent.TargetDepartmentId.HasValue)
					departmentIdentifier = intent.TargetDepartmentId.Value.ToString();

				if (string.IsNullOrWhiteSpace(departmentIdentifier))
					return new ChatbotResponse { Text = ChatbotResources.Get("Dept_SwitchSpecify", culture), Processed = true };

				// Switching is limited to chatbot-usable (non-free plan, chatbot enabled for this platform)
				// departments — the same set, in the same order, that ListDepartments shows, so a numeric
				// pick maps to the displayed list.
				var activeMemberships = await GetSmsSupportingMembershipsAsync(session.UserId, session.Platform);

				if (activeMemberships.Count == 0)
					return new ChatbotResponse { Text = ChatbotResources.Get("Dept_NoActiveMemberships", culture), Processed = true };

				DepartmentMember targetMembership = null;
				// Trailing punctuation is never part of a department identifier ("switch 1?"). An input
				// that normalizes to empty (e.g. "switch ???") must be rejected here — an empty string
				// would pass the name IndexOf check below and silently switch to the first department.
				var trimmedId = departmentIdentifier.Trim().TrimEnd('?', '!', '.', ',');
				if (trimmedId.Length == 0)
					return new ChatbotResponse { Text = ChatbotResources.Get("Dept_SwitchSpecify", culture), Processed = true };

				if (int.TryParse(trimmedId, out var listIndex) && listIndex >= 1 && listIndex <= activeMemberships.Count)
					targetMembership = activeMemberships[listIndex - 1];

				if (targetMembership == null && int.TryParse(trimmedId, out var deptId))
					targetMembership = activeMemberships.FirstOrDefault(m => m.DepartmentId == deptId);

				if (targetMembership == null)
				{
					foreach (var membership in activeMemberships)
					{
						var dept = await _departmentsService.GetDepartmentByIdAsync(membership.DepartmentId);
						if (dept != null)
						{
							if (string.Equals(dept.Name, trimmedId, StringComparison.OrdinalIgnoreCase) ||
								string.Equals(dept.Code, trimmedId, StringComparison.OrdinalIgnoreCase))
							{
								targetMembership = membership;
								break;
							}

							if (dept.Name.IndexOf(trimmedId, StringComparison.OrdinalIgnoreCase) >= 0)
							{
								if (targetMembership == null)
									targetMembership = membership;
							}
						}
					}
				}

				if (targetMembership == null)
					return new ChatbotResponse { Text = ChatbotResources.Get("Dept_SwitchNotFound", culture, departmentIdentifier), Processed = true };

				if (targetMembership.IsActive)
				{
					var dept = await _departmentsService.GetDepartmentByIdAsync(targetMembership.DepartmentId);
					var name = dept?.Name ?? ChatbotResources.Get("Dept_DepartmentNum", culture, targetMembership.DepartmentId);
					return new ChatbotResponse { Text = ChatbotResources.Get("Dept_AlreadyActive", culture, name), Processed = true };
				}

				var identityUser = _usersService.GetUserById(session.UserId);

				var success = await _departmentsService.SetActiveDepartmentForUserAsync(
					session.UserId,
					targetMembership.DepartmentId,
					identityUser);

				if (success)
				{
					session.DepartmentId = targetMembership.DepartmentId;

					var dept = await _departmentsService.GetDepartmentByIdAsync(targetMembership.DepartmentId);
					var name = dept?.Name ?? ChatbotResources.Get("Dept_DepartmentNum", culture, targetMembership.DepartmentId);
					return new ChatbotResponse { Text = ChatbotResources.Get("Dept_Switched", culture, name), Processed = true };
				}

				return new ChatbotResponse { Text = ChatbotResources.Get("Dept_SwitchFailed", culture), Processed = false };
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return new ChatbotResponse { Text = ChatbotResources.Get("Dept_ErrorSwitch", culture), Processed = false };
			}
		}
	}
}
