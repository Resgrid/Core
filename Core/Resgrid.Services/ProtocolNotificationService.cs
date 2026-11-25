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
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Direct protocol notifications (P2, EventTypes 57-59, and the P1 approver notice): push + email through
	/// ICommunicationService, with a push event code the apps route on and the web link in the text for the older apps (R-S7:
	/// their members acknowledge in the browser from the notification link). Active members only; nothing goes out where the
	/// department cannot transmit. Outbound text is English, like the rest of Resgrid's outbound messages.
	/// </summary>
	public class ProtocolNotificationService : IProtocolNotificationService
	{
		private readonly Lazy<ICommunicationService> _communication;
		private readonly IDepartmentsService _departments;
		private readonly IDepartmentSettingsService _settings;
		private readonly IPersonnelRolesService _roles;
		private readonly IDepartmentGroupsService _groups;
		private readonly IPermissionsService _permissions;

		public ProtocolNotificationService(Lazy<ICommunicationService> communication, IDepartmentsService departments, IDepartmentSettingsService settings,
			IPersonnelRolesService roles, IDepartmentGroupsService groups, IPermissionsService permissions)
		{
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

			var bulletin = protocol.IsBulletin;
			var title = bulletin ? "Priority bulletin" : "Protocol to acknowledge";
			var label = string.IsNullOrWhiteSpace(version.Label) ? string.Empty : " version " + version.Label;
			var message = bulletin
				? $"{protocol.Name}: read and acknowledge this bulletin. {ReadLink(version.ProtocolVersionId)}"
				: $"Please read and acknowledge protocol {protocol.Code} {protocol.Name}{label}. {ReadLink(version.ProtocolVersionId)}";
			var type = bulletin ? EventTypes.ProtocolBulletinIssued : EventTypes.ProtocolAcknowledgmentAssigned;
			return await SendAsync(departmentId, userIds, title, message, EventCode(type, version.ProtocolVersionId), cancellationToken);
		}

		public async Task<int> NotifyBundleAsync(int departmentId, string userId, int count, CancellationToken cancellationToken = default)
		{
			if (count <= 0 || string.IsNullOrWhiteSpace(userId))
				return 0;

			var link = (Config.SystemBehaviorConfig.ResgridBaseUrl ?? string.Empty).TrimEnd('/') + "/User/ProtocolAcknowledgments/Mine";
			var message = count == 1
				? $"You have a protocol to read and acknowledge. {link}"
				: $"You have {count.ToString(CultureInfo.InvariantCulture)} protocols to read and acknowledge. {link}";
			return await SendAsync(departmentId, new[] { userId }, "Protocols to acknowledge", message, "PA:0", cancellationToken);
		}

		public async Task<int> NotifyReminderAsync(int departmentId, string userId, DispatchProtocol protocol, ProtocolVersion version, ProtocolAcknowledgmentReminderStages stage,
			DateTime? dueOnUtc, CancellationToken cancellationToken = default)
		{
			if (protocol == null || version == null || stage == ProtocolAcknowledgmentReminderStages.None)
				return 0;

			var department = await _departments.GetDepartmentByIdAsync(departmentId, false);
			var due = dueOnUtc.HasValue && department != null ? DateTime.SpecifyKind(dueOnUtc.Value, DateTimeKind.Utc).TimeConverter(department).ToShortDateString() : null;
			var title = stage == ProtocolAcknowledgmentReminderStages.Due ? "Protocol acknowledgment due" : "Protocol acknowledgment overdue";
			var message = stage == ProtocolAcknowledgmentReminderStages.Due
				? $"Protocol {protocol.Code} {protocol.Name} is due to be acknowledged{(due == null ? string.Empty : " on " + due)}. {ReadLink(version.ProtocolVersionId)}"
				: $"Protocol {protocol.Code} {protocol.Name} was due to be acknowledged{(due == null ? string.Empty : " on " + due)}. {ReadLink(version.ProtocolVersionId)}";
			return await SendAsync(departmentId, new[] { userId }, title, message, EventCode(EventTypes.ProtocolAcknowledgmentOverdue, version.ProtocolVersionId), cancellationToken);
		}

		public async Task<int> NotifyEscalationAsync(int departmentId, IReadOnlyCollection<string> supervisorUserIds, string memberName, DispatchProtocol protocol, ProtocolVersion version,
			CancellationToken cancellationToken = default)
		{
			if (protocol == null || version == null)
				return 0;

			var link = (Config.SystemBehaviorConfig.ResgridBaseUrl ?? string.Empty).TrimEnd('/') + "/User/ProtocolAcknowledgments/Compliance?protocolId=" +
				protocol.DispatchProtocolId.ToString(CultureInfo.InvariantCulture);
			var message = $"{memberName} has not acknowledged protocol {protocol.Code} {protocol.Name}, which is overdue. {link}";
			return await SendAsync(departmentId, supervisorUserIds, "Protocol acknowledgment overdue", message,
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
			var message = $"Protocol {protocol.Code} {protocol.Name} is waiting for your review (step {step.StepOrder.ToString(CultureInfo.InvariantCulture)}). {ReviewLink(version.ProtocolVersionId)}";
			return await SendAsync(departmentId, recipients, "Protocol to review", message, "PR:" + version.ProtocolVersionId.ToString(CultureInfo.InvariantCulture), cancellationToken);
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
			foreach (var userId in wanted.Where(active.Contains))
			{
				cancellationToken.ThrowIfCancellationRequested();
				try
				{
					if (await _communication.Value.SendNotificationAsync(userId, departmentId, message, number, department, title, null, false, eventCode))
						sent++;
				}
				catch (Exception ex)
				{
					// One member's channel failing never stops the others.
					Logging.LogException(ex, $"Protocol notification to {userId} in department {departmentId} failed.");
				}
			}

			return sent;
		}
	}
}
