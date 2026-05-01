using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Resgrid.Chatbot.Interfaces;
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

		public DepartmentActionHandler(
			IDepartmentsService departmentsService,
			IUserProfileService userProfileService,
			IUsersService usersService)
		{
			_departmentsService = departmentsService;
			_userProfileService = userProfileService;
			_usersService = usersService;
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
					return new ChatbotResponse { Text = "Unknown department command.", Processed = false };
			}
		}

		private async Task<ChatbotResponse> ListDepartmentsAsync(ChatbotSession session)
		{
			try
			{
				var allMemberRecords = await _departmentsService.GetAllDepartmentsForUserAsync(session.UserId);
				if (allMemberRecords == null || allMemberRecords.Count == 0)
				{
					return new ChatbotResponse
					{
						Text = "You are not a member of any department.",
						Processed = true
					};
				}

				// Filter out deleted memberships
				var activeMemberships = allMemberRecords
					.Where(m => !m.IsDeleted)
					.OrderByDescending(m => m.IsActive)
					.ThenBy(m => m.DepartmentId)
					.ToList();

				if (activeMemberships.Count == 0)
				{
					return new ChatbotResponse
					{
						Text = "You have no active department memberships.",
						Processed = true
					};
				}

				// Resolve department names
				var sb = new StringBuilder();
				sb.AppendLine("Your departments:");

				for (int i = 0; i < activeMemberships.Count; i++)
				{
					var membership = activeMemberships[i];
					var dept = await _departmentsService.GetDepartmentByIdAsync(membership.DepartmentId);
					var deptName = dept?.Name ?? $"Department #{membership.DepartmentId}";
					var isActive = membership.IsActive ? " [ACTIVE]" : "";

					sb.AppendLine($"{i + 1}. {deptName}{isActive}");
				}

				sb.AppendLine();
				sb.AppendLine("To switch departments, type: switch to department [name or number]");

				return new ChatbotResponse { Text = sb.ToString().TrimEnd(), Processed = true };
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return new ChatbotResponse { Text = "Error retrieving your departments.", Processed = false };
			}
		}

		private async Task<ChatbotResponse> GetActiveDepartmentAsync(ChatbotSession session)
		{
			try
			{
				var allMemberRecords = await _departmentsService.GetAllDepartmentsForUserAsync(session.UserId);
				if (allMemberRecords == null || allMemberRecords.Count == 0)
				{
					return new ChatbotResponse
					{
						Text = "You are not a member of any department.",
						Processed = true
					};
				}

				var activeMembership = allMemberRecords
					.Where(m => !m.IsDeleted && m.IsActive)
					.OrderByDescending(m => m.IsDefault)
					.FirstOrDefault();

				if (activeMembership == null)
				{
					// Fall back to IsDefault
					activeMembership = allMemberRecords
						.Where(m => !m.IsDeleted)
						.OrderByDescending(m => m.IsDefault)
						.FirstOrDefault();
				}

				if (activeMembership == null)
				{
					return new ChatbotResponse
					{
						Text = "You don't have an active department set. Type 'departments' to see your options.",
						Processed = true
					};
				}

				var dept = await _departmentsService.GetDepartmentByIdAsync(activeMembership.DepartmentId);
				var deptName = dept?.Name ?? $"Department #{activeMembership.DepartmentId}";

				var isDefault = activeMembership.IsDefault ? " (your default)" : "";
				var isActive = activeMembership.IsActive ? "active" : "not active";

				return new ChatbotResponse
				{
					Text = $"Your {isActive} department is: {deptName}{isDefault}.",
					Processed = true
				};
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return new ChatbotResponse { Text = "Error retrieving your active department.", Processed = false };
			}
		}

		private async Task<ChatbotResponse> SwitchDepartmentAsync(ChatbotIntent intent, ChatbotSession session)
		{
			try
			{
				var departmentIdentifier = "";

				// Check for department identifier in parameters
				if (intent.Parameters != null)
				{
					intent.Parameters.TryGetValue("departmentIdentifier", out departmentIdentifier);
					if (string.IsNullOrWhiteSpace(departmentIdentifier))
						intent.Parameters.TryGetValue("departmentName", out departmentIdentifier);
					if (string.IsNullOrWhiteSpace(departmentIdentifier))
						intent.Parameters.TryGetValue("departmentId", out departmentIdentifier);
				}

				// Try TargetDepartmentId override
				if (string.IsNullOrWhiteSpace(departmentIdentifier) && intent.TargetDepartmentId.HasValue)
				{
					departmentIdentifier = intent.TargetDepartmentId.Value.ToString();
				}

				if (string.IsNullOrWhiteSpace(departmentIdentifier))
				{
					return new ChatbotResponse
					{
						Text = "Please specify the department name or number to switch to.\nType 'departments' to see your list.",
						Processed = true
					};
				}

				var allMemberRecords = await _departmentsService.GetAllDepartmentsForUserAsync(session.UserId);
				var activeMemberships = allMemberRecords
					.Where(m => !m.IsDeleted)
					.ToList();

				if (activeMemberships.Count == 0)
				{
					return new ChatbotResponse
					{
						Text = "You are not a member of any department.",
						Processed = true
					};
				}

				// Find the target membership: by number (1-based list index), by department id, or by name
				DepartmentMember targetMembership = null;
				var trimmedId = departmentIdentifier.Trim();

				// Try as list index (1-based)
				if (int.TryParse(trimmedId, out var listIndex) && listIndex >= 1 && listIndex <= activeMemberships.Count)
				{
					targetMembership = activeMemberships[listIndex - 1];
				}

				// Try as department ID
				if (targetMembership == null && int.TryParse(trimmedId, out var deptId))
				{
					targetMembership = activeMemberships.FirstOrDefault(m => m.DepartmentId == deptId);
				}

				// Try as department name or code
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

							// Partial match on name
							if (dept.Name.IndexOf(trimmedId, StringComparison.OrdinalIgnoreCase) >= 0)
							{
								if (targetMembership == null)
									targetMembership = membership;
							}
						}
					}
				}

				if (targetMembership == null)
				{
					return new ChatbotResponse
					{
						Text = $"Couldn't find a department matching \"{departmentIdentifier}\".\nType 'departments' to see your list.",
						Processed = true
					};
				}

				if (targetMembership.IsActive)
				{
					var dept = await _departmentsService.GetDepartmentByIdAsync(targetMembership.DepartmentId);
					return new ChatbotResponse
					{
						Text = $"Your active department is already {dept?.Name ?? $"#{targetMembership.DepartmentId}"}.",
						Processed = true
					};
				}

				// Get the IdentityUser for the SetActiveDepartmentForUserAsync call
				var identityUser = _usersService.GetUserById(session.UserId);

				// Switch the active department
				var success = await _departmentsService.SetActiveDepartmentForUserAsync(
					session.UserId,
					targetMembership.DepartmentId,
					identityUser);

				if (success)
				{
					// Update session's DepartmentId so subsequent operations target the correct department
					session.DepartmentId = targetMembership.DepartmentId;

					var dept = await _departmentsService.GetDepartmentByIdAsync(targetMembership.DepartmentId);
					return new ChatbotResponse
					{
						Text = $"Switched to {dept?.Name ?? $"Department #{targetMembership.DepartmentId}"}.",
						Processed = true
					};
				}

				return new ChatbotResponse
				{
					Text = "Failed to switch departments. Please try again or contact your administrator.",
					Processed = false
				};
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return new ChatbotResponse { Text = "Error switching departments.", Processed = false };
			}
		}
	}
}
