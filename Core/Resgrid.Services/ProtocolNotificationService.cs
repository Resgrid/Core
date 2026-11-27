using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Localization.Areas.User.ContentNotices;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Helpers;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Direct protocol notifications (P2, EventTypes 57-59, and the P1 approver notice): push + email through
	/// ICommunicationService, with a push event code the apps route on and the web link in the text for the older apps (R-S7:
	/// their members acknowledge in the browser from the notification link). Active members only; nothing goes out where the
	/// department cannot transmit. The text comes from ContentNotices in each recipient's language (round 9C).
	/// </summary>
	public class ProtocolNotificationService : IProtocolNotificationService
	{
		private readonly Lazy<ICommunicationService> _communication;
		private readonly IDepartmentsService _departments;
		private readonly IDepartmentSettingsService _settings;
		private readonly IPersonnelRolesService _roles;
		private readonly IDepartmentGroupsService _groups;
		private readonly IPermissionsService _permissions;
		private readonly IUserProfileService _profiles;

		public ProtocolNotificationService(Lazy<ICommunicationService> communication, IDepartmentsService departments, IDepartmentSettingsService settings,
			IPersonnelRolesService roles, IDepartmentGroupsService groups, IPermissionsService permissions, IUserProfileService profiles)
		{
			_profiles = profiles;
			_communication = communication;
			_departments = departments;
			_settings = settings;
			_roles = roles;
			_groups = groups;
			_permissions = permissions;
		}

		/// <summary>The read-and-sign page of a version (the deep link every acknowledgment notice carries).</summary>
		public static string ReadLink(int protocolVersionId) =>
			(Config.SystemBehaviorConfig.ResgridBaseUrl ?? string.Empty).TrimEnd('/') + "/User/ProtocolAcknowledgments/Read?versionId=" + protocolVersionId.ToString(CultureInfo.InvariantCulture);

		public static string ReviewLink(int protocolVersionId) =>
			(Config.SystemBehaviorConfig.ResgridBaseUrl ?? string.Empty).TrimEnd('/') + "/User/Protocols/Review?id=" + protocolVersionId.ToString(CultureInfo.InvariantCulture);

		/// <summary>The push event code for an acknowledgment ("PA:{versionId}") or a bulletin ("PB:{versionId}").</summary>
		public static string EventCode(EventTypes type, int protocolVersionId) =>
			(type == EventTypes.ProtocolBulletinIssued ? "PB:" : "PA:") + protocolVersionId.ToString(CultureInfo.InvariantCulture);

		public async Task<int> NotifyAssignedAsync(int departmentId, IReadOnlyCollection<string> userIds, DispatchProtocol protocol, ProtocolVersion version,
			CancellationToken cancellationToken = default)
		{
			if (protocol == null || version == null)
				return 0;

			var type = protocol.IsBulletin ? EventTypes.ProtocolBulletinIssued : EventTypes.ProtocolAcknowledgmentAssigned;
			return await SendAsync(departmentId, userIds, (culture, _) => AssignedNotice(protocol, version, culture), EventCode(type, version.ProtocolVersionId), cancellationToken);
		}

		/// <summary>The acknowledgment (or bulletin) notice for a new version, in <paramref name="culture"/>.</summary>
		public static ContentNoticeDelivery.Notice AssignedNotice(DispatchProtocol protocol, ProtocolVersion version, CultureInfo culture)
		{
			var link = ReadLink(version.ProtocolVersionId);
			if (protocol.IsBulletin)
				return new ContentNoticeDelivery.Notice(ContentNoticeText.Get("Protocol.Bulletin.Title", culture),
					ContentNoticeText.Format("Protocol.Bulletin.Message", culture, protocol.Name, link));
			return new ContentNoticeDelivery.Notice(ContentNoticeText.Get("Protocol.Assigned.Title", culture), string.IsNullOrWhiteSpace(version.Label)
				? ContentNoticeText.Format("Protocol.Assigned.Message", culture, protocol.Code, protocol.Name, link)
				: ContentNoticeText.Format("Protocol.Assigned.MessageVersion", culture, protocol.Code, protocol.Name, version.Label, link));
		}

		/// <summary>The due or overdue acknowledgment reminder: the date in the department's zone, written the reader's way.</summary>
		public static ContentNoticeDelivery.Notice ReminderNotice(DispatchProtocol protocol, ProtocolVersion version, ProtocolAcknowledgmentReminderStages stage,
			DateTime? dueOnUtc, Department department, CultureInfo culture)
		{
			var due = department == null ? null : ContentNoticeCultures.LocalDate(dueOnUtc, department, culture);
			var overdue = stage != ProtocolAcknowledgmentReminderStages.Due;
			var link = ReadLink(version.ProtocolVersionId);
			var message = due == null
				? ContentNoticeText.Format(overdue ? "Protocol.Reminder.Overdue" : "Protocol.Reminder.Due", culture, protocol.Code, protocol.Name, link)
				: ContentNoticeText.Format(overdue ? "Protocol.Reminder.OverdueOn" : "Protocol.Reminder.DueOn", culture, protocol.Code, protocol.Name, due, link);
			return new ContentNoticeDelivery.Notice(ContentNoticeText.Get(overdue ? "Protocol.Reminder.OverdueTitle" : "Protocol.Reminder.DueTitle", culture), message);
		}

		public async Task<int> NotifyBundleAsync(int departmentId, string userId, int count, CancellationToken cancellationToken = default)
		{
			if (count <= 0 || string.IsNullOrWhiteSpace(userId))
				return 0;

			var link = (Config.SystemBehaviorConfig.ResgridBaseUrl ?? string.Empty).TrimEnd('/') + "/User/ProtocolAcknowledgments/Mine";
			return await SendAsync(departmentId, new[] { userId }, (culture, _) => new ContentNoticeDelivery.Notice(ContentNoticeText.Get("Protocol.Bundle.Title", culture),
				count == 1 ? ContentNoticeText.Format("Protocol.Bundle.One", culture, link) : ContentNoticeText.Format("Protocol.Bundle.Many", culture, count, link)),
				"PA:0", cancellationToken);
		}

		public async Task<int> NotifyReminderAsync(int departmentId, string userId, DispatchProtocol protocol, ProtocolVersion version, ProtocolAcknowledgmentReminderStages stage,
			DateTime? dueOnUtc, CancellationToken cancellationToken = default)
		{
			if (protocol == null || version == null || stage == ProtocolAcknowledgmentReminderStages.None)
				return 0;

			return await SendAsync(departmentId, new[] { userId }, (culture, department) => ReminderNotice(protocol, version, stage, dueOnUtc, department, culture),
				EventCode(EventTypes.ProtocolAcknowledgmentOverdue, version.ProtocolVersionId), cancellationToken);
		}

		public async Task<int> NotifyEscalationAsync(int departmentId, IReadOnlyCollection<string> supervisorUserIds, string memberName, DispatchProtocol protocol, ProtocolVersion version,
			CancellationToken cancellationToken = default)
		{
			if (protocol == null || version == null)
				return 0;

			var link = (Config.SystemBehaviorConfig.ResgridBaseUrl ?? string.Empty).TrimEnd('/') + "/User/ProtocolAcknowledgments/Compliance?protocolId=" +
				protocol.DispatchProtocolId.ToString(CultureInfo.InvariantCulture);
			return await SendAsync(departmentId, supervisorUserIds, (culture, _) => new ContentNoticeDelivery.Notice(ContentNoticeText.Get("Protocol.Reminder.OverdueTitle", culture),
					ContentNoticeText.Format("Protocol.Escalation.Message", culture, memberName, protocol.Code, protocol.Name, link)),
				EventCode(EventTypes.ProtocolAcknowledgmentOverdue, version.ProtocolVersionId), cancellationToken);
		}

		public async Task<int> NotifyApproversAsync(int departmentId, DispatchProtocol protocol, ProtocolVersion version, ProtocolVersionApproval step,
			CancellationToken cancellationToken = default)
		{
			if (protocol == null || version == null || step == null)
				return 0;

			var recipients = await ApproversForStepAsync(departmentId, step);
			// The person who submitted (or just approved the step before) knows already.
			recipients.RemoveWhere(id => string.Equals(id, version.SubmittedByUserId, StringComparison.OrdinalIgnoreCase));
			return await SendAsync(departmentId, recipients, (culture, _) => new ContentNoticeDelivery.Notice(ContentNoticeText.Get("Protocol.Review.Title", culture),
					ContentNoticeText.Format("Protocol.Review.Message", culture, protocol.Code, protocol.Name, step.StepOrder, ReviewLink(version.ProtocolVersionId))),
				"PR:" + version.ProtocolVersionId.ToString(CultureInfo.InvariantCulture), cancellationToken);
		}

		/// <summary>
		/// Who may decide a step: its named approver, or the members of its role; for an open step, the holders of Approve Protocols
		/// (department admins, and the roles or group admins the permission names). "Everyone" falls back to the admins, so one
		/// submit never notifies the whole department.
		/// </summary>
		private async Task<HashSet<string>> ApproversForStepAsync(int departmentId, ProtocolVersionApproval step)
		{
			var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			if (!string.IsNullOrWhiteSpace(step.ApproverUserId))
			{
				ids.Add(step.ApproverUserId);
				return ids;
			}

			if (step.ApproverRoleId.HasValue)
			{
				var role = await _roles.GetRoleByIdAsync(step.ApproverRoleId.Value);
				if (role != null && role.DepartmentId == departmentId)
					foreach (var member in await _roles.GetAllMembersOfRoleAsync(role.PersonnelRoleId) ?? new List<PersonnelRoleUser>())
						if (!string.IsNullOrWhiteSpace(member?.UserId))
							ids.Add(member.UserId);
				return ids;
			}

			foreach (var admin in await _departments.GetActiveAdminsForDepartmentAsync(departmentId) ?? new List<Model.Identity.IdentityUser>())
				ids.Add(admin.Id);

			var permission = await _permissions.GetPermissionByDepartmentTypeAsync(departmentId, PermissionTypes.ApproveProtocols);
			if (permission != null && permission.Action == (int)PermissionActions.DepartmentAdminsAndSelectRoles && !string.IsNullOrWhiteSpace(permission.Data))
			{
				foreach (var roleId in permission.Data.Split(',').Select(x => int.TryParse(x.Trim(), out var id) ? id : 0).Where(id => id > 0))
				{
					var role = await _roles.GetRoleByIdAsync(roleId);
					if (role == null || role.DepartmentId != departmentId)
						continue;
					foreach (var member in await _roles.GetAllMembersOfRoleAsync(roleId) ?? new List<PersonnelRoleUser>())
						if (!string.IsNullOrWhiteSpace(member?.UserId))
							ids.Add(member.UserId);
				}
			}
			else if (permission != null && permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins)
			{
				foreach (var member in await _groups.GetAllGroupAdminsByDepartmentIdAsync(departmentId) ?? new List<DepartmentGroupMember>())
					if (!string.IsNullOrWhiteSpace(member?.UserId))
						ids.Add(member.UserId);
			}

			return ids;
		}

		/// <summary>Active members only, each in their own language (<see cref="ContentNoticeDelivery"/>).</summary>
		private Task<int> SendAsync(int departmentId, IEnumerable<string> userIds, Func<CultureInfo, Department, ContentNoticeDelivery.Notice> compose, string eventCode,
			CancellationToken cancellationToken) =>
			ContentNoticeDelivery.SendAsync(_communication.Value, _departments, _settings, _profiles, departmentId, userIds, compose, eventCode, "Protocol notification", cancellationToken);
	}
}
