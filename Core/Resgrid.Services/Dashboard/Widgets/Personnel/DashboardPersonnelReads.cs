using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Services;

namespace Resgrid.Services.Dashboard.Widgets.Personnel
{
	/// <summary>
	/// The people the personnel widgets count, with the status table's rules (IPersonnelStatusBoardService): the members in the
	/// plan limit with deleted, disabled and hidden members left out (the inactive-member policy), then the personnel
	/// visibility matrix for the viewer.
	/// </summary>
	public static class DashboardPersonnelReads
	{
		public static string EditProfileUrl(string userId) => "/User/Home/EditUserProfile?userId=" + Uri.EscapeDataString(userId ?? string.Empty);

		public static async Task<List<UserGroupRole>> GetVisibleMembersAsync(IUsersService usersService, IDepartmentsService departmentsService,
			IAuthorizationService authorizationService, int departmentId, string viewerUserId)
		{
			var members = await usersService.GetUserGroupAndRolesByDepartmentIdInLimitAsync(departmentId, false, false, false) ?? new List<UserGroupRole>();
			var excluded = new HashSet<string>(await departmentsService.GetAllDisabledOrHiddenUsersAsync(departmentId) ?? new List<string>(), StringComparer.OrdinalIgnoreCase);

			var visible = new List<UserGroupRole>();
			var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (var member in members)
			{
				if (member == null || string.IsNullOrWhiteSpace(member.UserId) || excluded.Contains(member.UserId) || !seen.Add(member.UserId))
					continue;

				if (await authorizationService.CanUserViewPersonViaMatrixAsync(member.UserId, viewerUserId, departmentId))
					visible.Add(member);
			}

			return visible;
		}

		/// <summary>
		/// Where a personnel status points (the status table's labels): a station by name; a call by its identifier when the
		/// call is in <paramref name="calls"/> (the viewer's scoped active calls), otherwise nothing. Legacy rows without a
		/// destination type read by their built-in status (4/5 station, 6 call).
		/// </summary>
		public static (string Name, int Type, int? Id) Destination(ActionLog log, IEnumerable<DepartmentGroup> stations, IEnumerable<Call> calls, int? detailType)
		{
			if (log?.DestinationId == null || log.DestinationId.Value <= 0)
				return (null, 0, null);

			var type = log.DestinationType;
			if (type.ToDestinationEntityType() == DestinationEntityTypes.None)
			{
				if (log.ActionTypeId == (int)ActionTypes.AvailableStation || log.ActionTypeId == (int)ActionTypes.RespondingToStation)
					type = (int)DestinationEntityTypes.Station;
				else if (log.ActionTypeId == (int)ActionTypes.RespondingToScene)
					type = (int)DestinationEntityTypes.Call;
			}

			var resolved = DestinationResolutionHelper.Resolve(log.DestinationId, type, detailType, calls, stations, null);
			if (string.IsNullOrWhiteSpace(resolved?.Name))
				return (null, type.ToDestinationEntityType() == DestinationEntityTypes.None ? 0 : type.Value, log.DestinationId);

			return (ProtectedDataEnvelope.SafeDisplay(resolved.Name), resolved.DestinationType ?? 0, resolved.DestinationId);
		}

		public static string Id(int value) => value.ToString(CultureInfo.InvariantCulture);
	}
}
