using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// See <see cref="ITextDepartmentSwitchService"/>. Single implementation of the inbound-SMS
	/// department switch flow shared by the Twilio and SignalWire controllers.
	/// </summary>
	public class TextDepartmentSwitchService : ITextDepartmentSwitchService
	{
		private readonly IDepartmentsService _departmentsService;
		private readonly IUserProfileService _userProfileService;
		private readonly IUsersService _usersService;
		private readonly ITextCommandService _textCommandService;

		public TextDepartmentSwitchService(IDepartmentsService departmentsService, IUserProfileService userProfileService,
			IUsersService usersService, ITextCommandService textCommandService)
		{
			_departmentsService = departmentsService;
			_userProfileService = userProfileService;
			_usersService = usersService;
			_textCommandService = textCommandService;
		}

		public async Task<string> BuildUnsupportedActiveDepartmentResponseAsync(string messageText, int departmentId, UserProfile profile,
			string logPrefix, CancellationToken cancellationToken = default(CancellationToken))
		{
			// STOP always works — checked before the plan/alternatives logic so a verified user whose
			// active department is on a free plan can still unsubscribe (telecom opt-out compliance).
			var payload = _textCommandService.DetermineType(messageText);
			if (payload.Type == TextCommandTypes.Stop)
			{
				await _userProfileService.DisableTextMessagesForUserAsync(profile.UserId, cancellationToken);
				return "Text messages are now turned off for this user, to enable again log in to Resgrid and update your profile.";
			}

			var supported = await _departmentsService.GetSmsSupportedMembershipsForUserAsync(profile.UserId);
			if (supported.Count == 0)
			{
				Logging.LogInfo($"{logPrefix} DepartmentId={departmentId} not authorized for inbound text (plan gate); user {profile.UserId} has no SMS-supported departments; replying with unsupported message.");
				return "Resgrid: Inbound text messaging isn't available on your department's current plan. Please upgrade to a paid plan to enable text commands.";
			}

			if (payload.Type == TextCommandTypes.SwitchDepartment)
				return await BuildSwitchCommandResponseAsync(profile, payload.Data, logPrefix, cancellationToken);

			Logging.LogInfo($"{logPrefix} DepartmentId={departmentId} not authorized for inbound text (plan gate); user {profile.UserId} has {supported.Count} SMS-supported department(s); replying with switch options.");
			return await BuildSwitchOptionsMessageAsync(supported,
				"Resgrid: Your active department's plan doesn't include inbound text messaging, but you belong to other departments that do.");
		}

		public async Task<string> BuildSwitchCommandResponseAsync(UserProfile profile, string departmentIdentifier,
			string logPrefix, CancellationToken cancellationToken = default(CancellationToken))
		{
			var supported = await _departmentsService.GetSmsSupportedMembershipsForUserAsync(profile.UserId);
			if (supported.Count == 0)
				return "Resgrid: None of your departments' current plans include inbound text messaging. Please upgrade to a paid plan to enable text commands.";

			if (string.IsNullOrWhiteSpace(departmentIdentifier))
				return await BuildSwitchOptionsMessageAsync(supported, "Resgrid: Your departments that support text messaging:");

			DepartmentMember target = null;
			var trimmedId = departmentIdentifier.Trim();

			// A small number is a pick from the displayed list; otherwise try a department id, then name/code.
			if (int.TryParse(trimmedId, out var listIndex) && listIndex >= 1 && listIndex <= supported.Count)
				target = supported[listIndex - 1];

			if (target == null && int.TryParse(trimmedId, out var deptId))
				target = supported.FirstOrDefault(m => m.DepartmentId == deptId);

			if (target == null)
			{
				foreach (var membership in supported)
				{
					// Cached read (bypassCache: false): the default bypasses the cache and pulls the
					// department WITH members per iteration — an N+1 query for a name/code comparison.
					var dept = await _departmentsService.GetDepartmentByIdAsync(membership.DepartmentId, false);
					if (dept == null)
						continue;

					if (string.Equals(dept.Name, trimmedId, StringComparison.OrdinalIgnoreCase)
						|| string.Equals(dept.Code, trimmedId, StringComparison.OrdinalIgnoreCase))
					{
						target = membership;
						break;
					}

					if (target == null && !string.IsNullOrWhiteSpace(dept.Name) && dept.Name.IndexOf(trimmedId, StringComparison.OrdinalIgnoreCase) >= 0)
						target = membership;
				}
			}

			if (target == null)
				return await BuildSwitchOptionsMessageAsync(supported, $"Resgrid: Couldn't find a department matching \"{trimmedId}\".");

			if (target.IsActive)
			{
				var alreadyActive = await _departmentsService.GetDepartmentByIdAsync(target.DepartmentId, false);
				return $"Resgrid: {alreadyActive?.Name ?? "That department"} is already your active department.";
			}

			var identityUser = _usersService.GetUserById(profile.UserId);
			var success = await _departmentsService.SetActiveDepartmentForUserAsync(profile.UserId, target.DepartmentId, identityUser, cancellationToken);

			var targetDept = await _departmentsService.GetDepartmentByIdAsync(target.DepartmentId, false);
			if (success)
			{
				Logging.LogInfo($"{logPrefix} User {profile.UserId} switched active department to {target.DepartmentId} via text command.");
				return $"Resgrid: Your active department is now {targetDept?.Name ?? ("department " + target.DepartmentId)}. Text commands now apply to this department.";
			}

			return "Resgrid: Unable to switch departments right now. Please try again later.";
		}

		private async Task<string> BuildSwitchOptionsMessageAsync(System.Collections.Generic.List<DepartmentMember> supported, string prefix)
		{
			var sb = new StringBuilder();
			sb.Append(prefix + Environment.NewLine);

			for (int i = 0; i < supported.Count; i++)
			{
				// Cached read (bypassCache: false) — the default hits the database (department + members)
				// once per listed department just to render its name.
				var dept = await _departmentsService.GetDepartmentByIdAsync(supported[i].DepartmentId, false);
				var activeMarker = supported[i].IsActive ? " (active)" : "";
				sb.Append($"{i + 1}: {dept?.Name ?? ("Department " + supported[i].DepartmentId)}{activeMarker}" + Environment.NewLine);
			}

			sb.Append("Reply SWITCH <number or name> to change your active department.");
			return sb.ToString();
		}
	}
}
