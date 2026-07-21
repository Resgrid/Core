using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Messages;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Best-effort fan-out of incident-command changes to affected Resgrid users (multi-channel per their
	/// profile preferences) and units (push). Mirrors the CallBroadcast recipient pattern but runs inline
	/// with the mutation; every send is individually guarded so one bad recipient can't break the rest and
	/// a notification outage can't fail the command action itself.
	/// </summary>
	public class IncidentCommandNotificationService : IIncidentCommandNotificationService
	{
		private readonly ICommunicationService _communicationService;
		private readonly IPushService _pushService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly IUserProfileService _userProfileService;
		private readonly IResourceAssignmentRepository _resourceAssignmentRepository;

		public IncidentCommandNotificationService(
			ICommunicationService communicationService,
			IPushService pushService,
			IDepartmentsService departmentsService,
			IDepartmentSettingsService departmentSettingsService,
			IUserProfileService userProfileService,
			IResourceAssignmentRepository resourceAssignmentRepository)
		{
			_communicationService = communicationService;
			_pushService = pushService;
			_departmentsService = departmentsService;
			_departmentSettingsService = departmentSettingsService;
			_userProfileService = userProfileService;
			_resourceAssignmentRepository = resourceAssignmentRepository;
		}

		public async Task NotifyResourceAssignedAsync(ResourceAssignment assignment, string laneName, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (assignment == null)
				return;

			var body = string.IsNullOrWhiteSpace(laneName)
				? $"You have been added to incident #{assignment.CallId}."
				: $"You have been assigned to '{laneName}' on incident #{assignment.CallId}.";

			await NotifyResourceAsync(assignment, "Incident Assignment", body, cancellationToken);
		}

		public async Task NotifyResourceMovedAsync(ResourceAssignment assignment, string fromLaneName, string toLaneName, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (assignment == null)
				return;

			var body = string.IsNullOrWhiteSpace(fromLaneName)
				? $"Your assignment on incident #{assignment.CallId} is now '{toLaneName}'."
				: $"Your assignment on incident #{assignment.CallId} moved from '{fromLaneName}' to '{toLaneName}'.";

			await NotifyResourceAsync(assignment, "Assignment Changed", body, cancellationToken);
		}

		public async Task NotifyResourceReleasedAsync(ResourceAssignment assignment, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (assignment == null)
				return;

			await NotifyResourceAsync(assignment, "Released from Incident",
				$"You have been released from incident #{assignment.CallId}.", cancellationToken);
		}

		public async Task NotifyLaneLeadChangedAsync(int departmentId, int callId, string laneName, bool isPrimary,
			string previousLeadUserId, string previousLeadName, string newLeadUserId, string newLeadName,
			CancellationToken cancellationToken = default(CancellationToken))
		{
			var slot = isPrimary ? "primary" : "secondary";
			var goingOff = await ResolveDisplayNameAsync(previousLeadUserId, previousLeadName);
			var comingOn = await ResolveDisplayNameAsync(newLeadUserId, newLeadName);

			string body;
			if (comingOn != null && goingOff != null)
				body = $"'{laneName}' {slot} lead changed on incident #{callId}: {comingOn} is coming on, {goingOff} is going off.";
			else if (comingOn != null)
				body = $"{comingOn} is now the '{laneName}' {slot} lead on incident #{callId}.";
			else if (goingOff != null)
				body = $"{goingOff} is no longer the '{laneName}' {slot} lead on incident #{callId}.";
			else
				return;

			var extraUsers = new List<string>();
			if (!string.IsNullOrWhiteSpace(newLeadUserId))
				extraUsers.Add(newLeadUserId);
			if (!string.IsNullOrWhiteSpace(previousLeadUserId))
				extraUsers.Add(previousLeadUserId);

			await BroadcastToIncidentAsync(departmentId, callId, "Lane Lead Changed", body, extraUsers, cancellationToken);
		}

		public async Task NotifyCommandTransferredAsync(IncidentCommand command, string fromUserId, string toUserId, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (command == null)
				return;

			var from = await ResolveDisplayNameAsync(fromUserId, null) ?? "the previous commander";
			var to = await ResolveDisplayNameAsync(toUserId, null) ?? "a new commander";
			var body = $"Command of incident #{command.CallId} has been transferred from {from} to {to}.";

			await BroadcastToIncidentAsync(command.DepartmentId, command.CallId, "Command Transferred", body,
				new List<string> { fromUserId, toUserId }, cancellationToken);
		}

		/// <summary>Routes one message to the resource behind an assignment: own-department users and units only.</summary>
		private async Task NotifyResourceAsync(ResourceAssignment assignment, string title, string body, CancellationToken cancellationToken)
		{
			try
			{
				if (assignment.ResourceKind == (int)ResourceAssignmentKind.RealPersonnel && !string.IsNullOrWhiteSpace(assignment.ResourceId))
				{
					await SendToUserAsync(assignment.ResourceId, assignment.DepartmentId, title, body);
				}
				else if (assignment.ResourceKind == (int)ResourceAssignmentKind.RealUnit && int.TryParse(assignment.ResourceId, out var unitId))
				{
					await SendToUnitAsync(unitId, assignment.DepartmentId, assignment.CallId, title, body);
				}
			}
			catch (Exception ex)
			{
				Resgrid.Framework.Logging.LogException(ex);
			}
		}

		/// <summary>
		/// Notifies every active own-department user and unit assigned to the incident (deduped), plus any
		/// explicitly-passed extra users (e.g. the incoming/outgoing lead or commander).
		/// </summary>
		private async Task BroadcastToIncidentAsync(int departmentId, int callId, string title, string body, List<string> extraUserIds, CancellationToken cancellationToken)
		{
			try
			{
				var assignments = (await _resourceAssignmentRepository.GetAllByDepartmentIdAsync(departmentId)) ?? Enumerable.Empty<ResourceAssignment>();
				var active = assignments.Where(a => a.CallId == callId && a.ReleasedOn == null).ToList();

				var userIds = active
					.Where(a => a.ResourceKind == (int)ResourceAssignmentKind.RealPersonnel && !string.IsNullOrWhiteSpace(a.ResourceId))
					.Select(a => a.ResourceId)
					.Concat(extraUserIds?.Where(u => !string.IsNullOrWhiteSpace(u)) ?? Enumerable.Empty<string>())
					.Distinct(StringComparer.OrdinalIgnoreCase)
					.ToList();

				var unitIds = active
					.Where(a => a.ResourceKind == (int)ResourceAssignmentKind.RealUnit)
					.Select(a => int.TryParse(a.ResourceId, out var id) ? id : 0)
					.Where(id => id > 0)
					.Distinct()
					.ToList();

				foreach (var userId in userIds)
				{
					try
					{
						await SendToUserAsync(userId, departmentId, title, body);
					}
					catch (Exception ex)
					{
						Resgrid.Framework.Logging.LogException(ex);
					}
				}

				foreach (var unitId in unitIds)
				{
					try
					{
						await SendToUnitAsync(unitId, departmentId, callId, title, body);
					}
					catch (Exception ex)
					{
						Resgrid.Framework.Logging.LogException(ex);
					}
				}
			}
			catch (Exception ex)
			{
				Resgrid.Framework.Logging.LogException(ex);
			}
		}

		private async Task SendToUserAsync(string userId, int departmentId, string title, string body)
		{
			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId, false);
			var departmentNumber = await _departmentSettingsService.GetTextToCallNumberForDepartmentAsync(departmentId);
			var profile = await _userProfileService.GetProfileByUserIdAsync(userId);

			await _communicationService.SendNotificationAsync(userId, departmentId, body, departmentNumber, department, title, profile);
		}

		private async Task SendToUnitAsync(int unitId, int departmentId, int callId, string title, string body)
		{
			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId, false);

			await _pushService.PushCallUnit(new StandardPushCall
			{
				CallId = callId,
				Title = title,
				SubTitle = body,
				DepartmentId = departmentId,
				DepartmentCode = department?.Code
			}, unitId);
		}

		/// <summary>A lead's display name: profile name for Resgrid users, the entered name for external leads.</summary>
		private async Task<string> ResolveDisplayNameAsync(string userId, string fallbackName)
		{
			if (!string.IsNullOrWhiteSpace(userId))
			{
				try
				{
					var profile = await _userProfileService.GetProfileByUserIdAsync(userId);
					if (profile != null)
						return profile.FullName.AsFirstNameLastName;
				}
				catch (Exception ex)
				{
					Resgrid.Framework.Logging.LogException(ex);
				}
				return userId;
			}

			return string.IsNullOrWhiteSpace(fallbackName) ? null : fallbackName;
		}
	}
}
