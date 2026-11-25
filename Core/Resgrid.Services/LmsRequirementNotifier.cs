using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Helpers;
using Resgrid.Model.Lms;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Direct learning-requirement notices (Training T3, EventTypes 61-63): push + email through ICommunicationService with the push event
	/// code "LR:{requirementId}" the apps route on and the web link in the text for the older apps. Members are told when a requirement
	/// applies to them, when it is at risk or overdue, and when enforcement acted or was undone; supervisors (the admins of the member's
	/// group) and named people when a member is overdue or, if the requirement asks, completes it (§13a); the department's active admins
	/// when a sweep refused to act (R-L11). Active members only; nothing goes out where the department cannot transmit. Outbound text is
	/// English, like the rest of Resgrid's outbound messages.
	/// </summary>
	public class LmsRequirementNotifier : ILmsRequirementNotifier
	{
		private readonly Lazy<ICommunicationService> _communication;
		private readonly IDepartmentsService _departments;
		private readonly IDepartmentSettingsService _settings;
		private readonly IDepartmentGroupsService _groups;

		public LmsRequirementNotifier(Lazy<ICommunicationService> communication, IDepartmentsService departments, IDepartmentSettingsService settings, IDepartmentGroupsService groups)
		{
			_communication = communication;
			_departments = departments;
			_settings = settings;
			_groups = groups;
		}

		/// <summary>The member's My Training page, where requirements are listed with their next action.</summary>
		public static string MyTrainingLink() => (Config.SystemBehaviorConfig.ResgridBaseUrl ?? string.Empty).TrimEnd('/') + "/User/Trainings/Index";

		public static string ComplianceLink() => (Config.SystemBehaviorConfig.ResgridBaseUrl ?? string.Empty).TrimEnd('/') + "/User/LmsRequirements/Compliance";

		public static string EventCode(LmsRequirement requirement) => "LR:" + (requirement?.LmsRequirementId ?? 0).ToString(CultureInfo.InvariantCulture);

		public async Task<int> NotifyAssignedAsync(int departmentId, string userId, LmsRequirement requirement, string targetName, DateTime? dueUtc, CancellationToken cancellationToken = default)
		{
			if (requirement == null)
				return 0;
			var due = await LocalDateAsync(departmentId, dueUtc);
			var message = $"New training requirement: {requirement.Name} ({targetName}){(due == null ? string.Empty : ", due " + due)}. {MyTrainingLink()}";
			return await SendAsync(departmentId, new[] { userId }, "Training requirement", message, EventCode(requirement), cancellationToken);
		}

		public async Task<int> NotifyDueAsync(int departmentId, string userId, LmsRequirement requirement, string targetName, LmsRequirementStates state, DateTime? dueUtc,
			CancellationToken cancellationToken = default)
		{
			if (requirement == null || (state != LmsRequirementStates.AtRisk && state != LmsRequirementStates.Overdue))
				return 0;
			var due = await LocalDateAsync(departmentId, dueUtc);
			var title = state == LmsRequirementStates.Overdue ? "Training requirement overdue" : "Training requirement due soon";
			var message = state == LmsRequirementStates.Overdue
				? $"{requirement.Name} ({targetName}) was due{(due == null ? string.Empty : " on " + due)} and is overdue. {MyTrainingLink()}"
				: $"{requirement.Name} ({targetName}) is due{(due == null ? " soon" : " on " + due)}. {MyTrainingLink()}";
			return await SendAsync(departmentId, new[] { userId }, title, message, EventCode(requirement), cancellationToken);
		}

		public async Task<int> NotifySupervisorsAsync(int departmentId, string memberUserId, LmsRequirement requirement, string targetName, bool completed,
			CancellationToken cancellationToken = default)
		{
			if (requirement == null || string.IsNullOrWhiteSpace(memberUserId))
				return 0;

			var recipients = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			if (requirement.AlertSupervisors)
			{
				var group = await _groups.GetGroupForUserAsync(memberUserId, departmentId);
				if (group?.Members != null && group.DepartmentId == departmentId)
					foreach (var admin in group.Members.Where(m => m != null && m.IsAdmin == true && !string.IsNullOrWhiteSpace(m.UserId)))
						recipients.Add(admin.UserId);
			}
			foreach (var id in (requirement.AlertUserIds ?? string.Empty).Split(',').Select(i => i.Trim()).Where(i => i.Length > 0))
				recipients.Add(id);
			// The member is told separately; a supervisor who is the member is not told twice.
			recipients.Remove(memberUserId);
			if (recipients.Count == 0)
				return 0;

			var name = (await _departments.GetAllPersonnelNamesForDepartmentAsync(departmentId) ?? new List<PersonName>())
				.FirstOrDefault(p => string.Equals(p.UserId, memberUserId, StringComparison.OrdinalIgnoreCase))?.Name ?? "A member";
			var message = completed
				? $"{name} completed {requirement.Name} ({targetName}). {ComplianceLink()}"
				: $"{name} is overdue on {requirement.Name} ({targetName}). {ComplianceLink()}";
			return await SendAsync(departmentId, recipients, completed ? "Training requirement completed" : "Training requirement overdue", message, EventCode(requirement), cancellationToken);
		}

		public async Task<int> NotifyEnforcementAsync(int departmentId, string userId, LmsRequirement requirement, string certificationName, bool undone,
			CancellationToken cancellationToken = default)
		{
			if (requirement == null)
				return 0;
			var message = undone
				? $"Your completion of {requirement.Name} restored the certification and roles it had affected. {MyTrainingLink()}"
				: $"{requirement.Name} is overdue, so your {certificationName ?? "certification"} has expired. Complete it to restore the certification. {MyTrainingLink()}";
			return await SendAsync(departmentId, new[] { userId }, undone ? "Training requirement met" : "Certification expired by training requirement", message,
				EventCode(requirement), cancellationToken);
		}

		public async Task<int> NotifyRefusedAsync(int departmentId, IReadOnlyCollection<LmsRoleBreach> breaches, int maxSharePercent, CancellationToken cancellationToken = default)
		{
			if (breaches == null || breaches.Count == 0)
				return 0;
			var admins = (await _departments.GetActiveAdminsForDepartmentAsync(departmentId) ?? new List<Model.Identity.IdentityUser>()).Select(a => a.UserId).ToList();
			var roles = string.Join("; ", breaches.Take(10).Select(b => $"{b.RoleName ?? "role " + b.PersonnelRoleId.ToString(CultureInfo.InvariantCulture)}: {b.Losing} of {b.Holders}"));
			var message = $"Training-backed enforcement did not run today: it would have removed more than {maxSharePercent}% of a role's members ({roles}). " +
				$"Review the requirements and the enforcement preview. {ComplianceLink().Replace("/Compliance", "/Enforcement")}";
			return await SendAsync(departmentId, admins, "Training enforcement refused", message, "LR:0", cancellationToken);
		}

		private async Task<string> LocalDateAsync(int departmentId, DateTime? utc)
		{
			if (!utc.HasValue)
				return null;
			var department = await _departments.GetDepartmentByIdAsync(departmentId, false);
			return department == null ? utc.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
				: DateTime.SpecifyKind(utc.Value, DateTimeKind.Utc).TimeConverter(department).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
		}

		private async Task<int> SendAsync(int departmentId, IEnumerable<string> userIds, string title, string message, string eventCode, CancellationToken cancellationToken)
		{
			var wanted = (userIds ?? Enumerable.Empty<string>()).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			if (wanted.Count == 0 || !ConfigHelper.CanTransmit(departmentId))
				return 0;

			var department = await _departments.GetDepartmentByIdAsync(departmentId, false);
			if (department == null)
				return 0;

			// Inactive-member policy: deleted, disabled and hidden members are never addressed by automation.
			var active = await _departments.GetActiveMemberUserIdsAsync(departmentId) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var number = await _settings.GetTextToCallNumberForDepartmentAsync(departmentId);
			var sent = 0;
			foreach (var userId in wanted.Where(id => active.Contains(id)))
			{
				cancellationToken.ThrowIfCancellationRequested();
				try
				{
					if (await _communication.Value.SendNotificationAsync(userId, departmentId, message, number, department, title, null, false, eventCode))
						sent++;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Learning requirement notice to {userId} in department {departmentId} failed.");
				}
			}

			return sent;
		}
	}
}
