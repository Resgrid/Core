using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Localization.Areas.User.ContentNotices;
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
	/// when a sweep refused to act (R-L11). Active members only; nothing goes out where the department cannot transmit. The text comes from
	/// ContentNotices in each recipient's language, dates in the department's zone (round 9C).
	/// </summary>
	public class LmsRequirementNotifier : ILmsRequirementNotifier
	{
		private readonly Lazy<ICommunicationService> _communication;
		private readonly IDepartmentsService _departments;
		private readonly IDepartmentSettingsService _settings;
		private readonly IDepartmentGroupsService _groups;
		private readonly IUserProfileService _profiles;

		public LmsRequirementNotifier(Lazy<ICommunicationService> communication, IDepartmentsService departments, IDepartmentSettingsService settings, IDepartmentGroupsService groups,
			IUserProfileService profiles)
		{
			_communication = communication;
			_departments = departments;
			_settings = settings;
			_groups = groups;
			_profiles = profiles;
		}

		/// <summary>The member's My Training page, where requirements are listed with their next action.</summary>
		public static string MyTrainingLink() => (Config.SystemBehaviorConfig.ResgridBaseUrl ?? string.Empty).TrimEnd('/') + "/User/Trainings/Index";

		public static string ComplianceLink() => (Config.SystemBehaviorConfig.ResgridBaseUrl ?? string.Empty).TrimEnd('/') + "/User/LmsRequirements/Compliance";

		public static string EventCode(LmsRequirement requirement) => "LR:" + (requirement?.LmsRequirementId ?? 0).ToString(CultureInfo.InvariantCulture);

		public async Task<int> NotifyAssignedAsync(int departmentId, string userId, LmsRequirement requirement, string targetName, DateTime? dueUtc, CancellationToken cancellationToken = default)
		{
			if (requirement == null)
				return 0;
			return await SendAsync(departmentId, new[] { userId }, (culture, department) => AssignedNotice(requirement, targetName, dueUtc, department, culture),
				EventCode(requirement), cancellationToken);
		}

		/// <summary>"New training requirement" in <paramref name="culture"/>, the due date in the department's zone.</summary>
		public static ContentNoticeDelivery.Notice AssignedNotice(LmsRequirement requirement, string targetName, DateTime? dueUtc, Department department, CultureInfo culture)
		{
			var due = ContentNoticeCultures.LocalDate(dueUtc, department, culture);
			return new ContentNoticeDelivery.Notice(ContentNoticeText.Get("Requirement.Assigned.Title", culture), due == null
				? ContentNoticeText.Format("Requirement.Assigned.Message", culture, requirement.Name, targetName, MyTrainingLink())
				: ContentNoticeText.Format("Requirement.Assigned.MessageDue", culture, requirement.Name, targetName, due, MyTrainingLink()));
		}

		public async Task<int> NotifyDueAsync(int departmentId, string userId, LmsRequirement requirement, string targetName, LmsRequirementStates state, DateTime? dueUtc,
			CancellationToken cancellationToken = default)
		{
			if (requirement == null || (state != LmsRequirementStates.AtRisk && state != LmsRequirementStates.Overdue))
				return 0;
			return await SendAsync(departmentId, new[] { userId }, (culture, department) =>
			{
				var due = ContentNoticeCultures.LocalDate(dueUtc, department, culture);
				var overdue = state == LmsRequirementStates.Overdue;
				var message = overdue
					? due == null ? ContentNoticeText.Format("Requirement.Overdue.Message", culture, requirement.Name, targetName, MyTrainingLink())
						: ContentNoticeText.Format("Requirement.Overdue.MessageOn", culture, requirement.Name, targetName, due, MyTrainingLink())
					: due == null ? ContentNoticeText.Format("Requirement.DueSoon.Message", culture, requirement.Name, targetName, MyTrainingLink())
						: ContentNoticeText.Format("Requirement.DueSoon.MessageOn", culture, requirement.Name, targetName, due, MyTrainingLink());
				return new ContentNoticeDelivery.Notice(ContentNoticeText.Get(overdue ? "Requirement.Overdue.Title" : "Requirement.DueSoon.Title", culture), message);
			}, EventCode(requirement), cancellationToken);
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
				.FirstOrDefault(p => string.Equals(p.UserId, memberUserId, StringComparison.OrdinalIgnoreCase))?.Name;
			return await SendAsync(departmentId, recipients, (culture, _) =>
			{
				var who = string.IsNullOrWhiteSpace(name) ? ContentNoticeText.Get("Requirement.AMember", culture) : name;
				return new ContentNoticeDelivery.Notice(ContentNoticeText.Get(completed ? "Requirement.Supervisor.CompletedTitle" : "Requirement.Overdue.Title", culture),
					ContentNoticeText.Format(completed ? "Requirement.Supervisor.Completed" : "Requirement.Supervisor.Overdue", culture, who, requirement.Name, targetName, ComplianceLink()));
			}, EventCode(requirement), cancellationToken);
		}

		public async Task<int> NotifyEnforcementAsync(int departmentId, string userId, LmsRequirement requirement, string certificationName, bool undone,
			CancellationToken cancellationToken = default)
		{
			if (requirement == null)
				return 0;
			return await SendAsync(departmentId, new[] { userId }, (culture, _) => undone
				? new ContentNoticeDelivery.Notice(ContentNoticeText.Get("Requirement.Met.Title", culture),
					ContentNoticeText.Format("Requirement.Met.Message", culture, requirement.Name, MyTrainingLink()))
				: new ContentNoticeDelivery.Notice(ContentNoticeText.Get("Requirement.Enforced.Title", culture),
					ContentNoticeText.Format("Requirement.Enforced.Message", culture, requirement.Name,
						string.IsNullOrWhiteSpace(certificationName) ? ContentNoticeText.Get("Requirement.Enforced.Certification", culture) : certificationName, MyTrainingLink())),
				EventCode(requirement), cancellationToken);
		}

		public async Task<int> NotifyRefusedAsync(int departmentId, IReadOnlyCollection<LmsRoleBreach> breaches, int maxSharePercent, CancellationToken cancellationToken = default)
		{
			if (breaches == null || breaches.Count == 0)
				return 0;
			var admins = (await _departments.GetActiveAdminsForDepartmentAsync(departmentId) ?? new List<Model.Identity.IdentityUser>()).Select(a => a.UserId).ToList();
			var link = ComplianceLink().Replace("/Compliance", "/Enforcement");
			return await SendAsync(departmentId, admins, (culture, _) =>
			{
				var roles = string.Join("; ", breaches.Take(10).Select(b => ContentNoticeText.Format("Requirement.Refused.Breach", culture,
					b.RoleName ?? ContentNoticeText.Format("Requirement.Refused.Role", culture, b.PersonnelRoleId), b.Losing, b.Holders)));
				return new ContentNoticeDelivery.Notice(ContentNoticeText.Get("Requirement.Refused.Title", culture),
					ContentNoticeText.Format("Requirement.Refused.Message", culture, maxSharePercent, roles, link));
			}, "LR:0", cancellationToken);
		}

		/// <summary>Active members only, each in their own language (<see cref="ContentNoticeDelivery"/>).</summary>
		private Task<int> SendAsync(int departmentId, IEnumerable<string> userIds, Func<CultureInfo, Department, ContentNoticeDelivery.Notice> compose, string eventCode,
			CancellationToken cancellationToken) =>
			ContentNoticeDelivery.SendAsync(_communication.Value, _departments, _settings, _profiles, departmentId, userIds, compose, eventCode, "Learning requirement notice",
				cancellationToken);
	}
}
