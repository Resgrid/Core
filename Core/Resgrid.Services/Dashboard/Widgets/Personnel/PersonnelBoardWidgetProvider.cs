using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.Operations;
using Resgrid.Model.Dashboard.Widgets.Personnel;
using Resgrid.Model.Helpers;
using Resgrid.Model.Reporting;
using Resgrid.Model.Services;

namespace Resgrid.Services.Dashboard.Widgets.Personnel
{
	/// <summary>
	/// personnel.board (plan §7.3): the classic dashboard's status table from the same IPersonnelStatusBoardService, so the
	/// widget lists exactly the people, groups and order the classic partial does (visibility matrix; disabled and hidden
	/// members excluded). Child groups follow their parent at any depth. Staffing notes (ADP userstates.note) go through
	/// IProtectedReadService.ResolveUserStatesForReadAsync with the caller's grant; nothing else on the row is catalogued.
	/// </summary>
	[DashboardWidget("personnel.board")]
	public class PersonnelBoardWidgetProvider : IDashboardWidgetDataProvider
	{
		private readonly IPersonnelStatusBoardService _personnelStatusBoardService;
		private readonly ICustomStateService _customStateService;
		private readonly IProtectedReadService _protectedReadService;

		public PersonnelBoardWidgetProvider(IPersonnelStatusBoardService personnelStatusBoardService, ICustomStateService customStateService,
			IProtectedReadService protectedReadService)
		{
			_personnelStatusBoardService = personnelStatusBoardService;
			_customStateService = customStateService;
			_protectedReadService = protectedReadService;
		}

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			var departmentId = context.DepartmentId;
			var viewer = context.Viewer;
			var isAdmin = viewer != null && viewer.IsDepartmentAdmin;

			var board = await _personnelStatusBoardService.GetBoardAsync(departmentId, context.UserId);
			var palette = await DashboardStatusPalette.LoadAsync(_customStateService, departmentId);

			var data = new PersonnelBoardWidgetData
			{
				CustomStatuses = board.States != null,
				CanResetAll = isAdmin,
				ResetAll = isAdmin ? new DashboardWidgetEndpoint { Url = "/User/Home/ResetAllToStandingBy", Method = "POST", Body = "query" } : null,
				ResetGroup = new DashboardWidgetEndpoint { Url = "/User/Home/ResetGroupToStandingBy", Method = "POST", Body = "query", Parameters = new[] { "groupId" } }
			};

			var departmentGroups = (board.DepartmentGroups ?? new List<DepartmentGroup>()).Where(g => g != null && g.DepartmentId == departmentId).ToList();

			// Groups filter: this department's groups only, each with the groups beneath it; the ungrouped people drop out.
			HashSet<int> wantedGroups = null;
			var requested = DashboardCallReads.ParseIds(context.GetStrings("groups"));
			if (requested.Count > 0)
			{
				var valid = requested.Where(id => departmentGroups.Any(g => g.DepartmentGroupId == id)).ToList();
				if (valid.Count == 0)
				{
					data.GroupFilterIgnored = true;
				}
				else
				{
					wantedGroups = new HashSet<int>(valid.SelectMany(id => DepartmentGroupHierarchy.GetSelfAndDescendantIds(departmentGroups, id)));
					data.GroupFilterIgnored = valid.Count < requested.Count;
				}
			}

			var defaultStatus = board.DefaultStatus != null && board.DefaultStatus.CustomStateDetailId > 0
				? DashboardStatusPalette.FromCustomStatus(board.DefaultStatus)
				: palette.PersonnelStatus((int)ActionTypes.StandingBy);
			var stations = board.Stations ?? new List<DepartmentGroup>();
			var hideUnavailable = context.GetBool("hideUnavailable", false);

			// One protected-read batch for every staffing note on the board (passes through when the department isn't enforced).
			var withNotes = board.Groups.SelectMany(g => g.Entries).Select(e => e.CurrentStaffing)
				.Where(s => s != null && s.UserStateId > 0 && !string.IsNullOrEmpty(s.Note))
				.Distinct()
				.ToList();
			if (withNotes.Count > 0)
				await _protectedReadService.ResolveUserStatesForReadAsync(departmentId, withNotes, context.ProtectedGrantToken, context.UserId, cancellationToken);

			foreach (var boardGroup in Ordered(board.Groups, departmentGroups))
			{
				var group = boardGroup.Group;
				if (wantedGroups != null && (group == null || !wantedGroups.Contains(group.DepartmentGroupId)))
					continue;

				var canAdminGroup = isAdmin || (group != null && group.IsUserGroupAdmin(context.UserId));
				var rows = new List<PersonnelBoardRow>();

				foreach (var entry in boardGroup.Entries)
				{
					if (entry?.UserInfo == null)
						continue;

					var log = entry.CurrentStatus;
					var staffing = entry.CurrentStaffing;
					var staffingChip = palette.Staffing(staffing?.State ?? (int)UserStateTypes.Available);
					if (hideUnavailable && staffingChip.Availability == (int)AvailabilityClass.Unavailable)
						continue;

					var destination = log == null
						? (Name: (string)null, Type: 0, Id: (int?)null)
						: DashboardPersonnelReads.Destination(log, stations, null, palette.PersonnelStatusOption(log.ActionTypeId)?.DetailType);
					var realStaffing = staffing != null && staffing.UserStateId > 0 && !staffing.AutoGenerated;

					rows.Add(new PersonnelBoardRow
					{
						UserId = entry.UserInfo.UserId,
						Name = entry.UserInfo.Name?.Trim(),
						RoleNames = entry.UserInfo.RoleNames,
						Status = log != null ? palette.PersonnelStatus(log.ActionTypeId) : defaultStatus,
						StatusOnUtc = log?.Timestamp,
						StatusDestination = destination.Type == (int)DestinationEntityTypes.Call ? null : destination.Name,
						StatusDestinationType = destination.Type,
						StatusDestinationId = destination.Id,
						Staffing = staffingChip,
						StaffingOnUtc = realStaffing ? staffing.Timestamp : (DateTime?)null,
						StaffingNote = realStaffing ? DashboardUnitReads.ProtectedNote(staffing.Note, viewer) : null,
						CanEdit = canAdminGroup,
						Url = canAdminGroup ? DashboardPersonnelReads.EditProfileUrl(entry.UserInfo.UserId) : null
					});
				}

				switch (context.GetString("sort", "department"))
				{
					case "name":
						rows = rows.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
						break;
					case "status":
						rows = rows.OrderBy(r => r.Status?.Text, StringComparer.CurrentCultureIgnoreCase).ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
						break;
				}

				// The ungrouped people only appear when there are some, as on the classic table.
				if (group == null && rows.Count == 0)
					continue;

				data.Groups.Add(new PersonnelBoardGroup
				{
					GroupId = group?.DepartmentGroupId,
					ParentGroupId = group?.ParentDepartmentGroupId,
					Name = group?.Name,
					IsViewerGroup = group != null && board.UsersGroup != null && board.UsersGroup.DepartmentGroupId == group.DepartmentGroupId,
					// The classic table offers the group reset only with the built-in statuses.
					CanReset = group != null && canAdminGroup && board.States == null,
					People = rows
				});
				data.TotalCount += rows.Count;
			}

			return data;
		}

		/// <summary>Top-level groups in board order, each followed by its descendants depth-first; the ungrouped people last.</summary>
		private static IEnumerable<PersonnelStatusBoardGroup> Ordered(List<PersonnelStatusBoardGroup> groups, List<DepartmentGroup> departmentGroups)
		{
			var listed = groups.Where(g => g?.Group != null && departmentGroups.Any(d => d.DepartmentGroupId == g.Group.DepartmentGroupId)).ToList();
			var ids = new HashSet<int>(listed.Select(g => g.Group.DepartmentGroupId));
			var emitted = new HashSet<int>();

			IEnumerable<PersonnelStatusBoardGroup> Walk(PersonnelStatusBoardGroup node)
			{
				if (!emitted.Add(node.Group.DepartmentGroupId))
					yield break;

				yield return node;

				foreach (var child in listed.Where(c => c.Group.ParentDepartmentGroupId == node.Group.DepartmentGroupId))
					foreach (var descendant in Walk(child))
						yield return descendant;
			}

			// A group whose parent isn't on the board (or that has none) starts a branch.
			foreach (var root in listed.Where(g => !g.Group.ParentDepartmentGroupId.HasValue || !ids.Contains(g.Group.ParentDepartmentGroupId.Value)))
				foreach (var node in Walk(root))
					yield return node;

			// A parent loop in bad data leaves groups unreached; they still list (once each), never silently dropped.
			foreach (var stray in listed.Where(g => !emitted.Contains(g.Group.DepartmentGroupId)).ToList())
				foreach (var node in Walk(stray))
					yield return node;

			foreach (var ungrouped in groups.Where(g => g != null && g.Group == null))
				yield return ungrouped;
		}
	}
}
