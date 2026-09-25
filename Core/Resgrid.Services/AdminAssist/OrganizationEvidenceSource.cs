using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.AdminAssist
{
	/// <summary>Fresh structural metadata. No names, addresses, coordinates or member identifiers leave this adapter.</summary>
	public sealed class OrganizationEvidenceSource(IDepartmentGroupsRepository groups, IUnitsRepository units,
		IDepartmentsService departments, IUsersService users, IAuthorizationService authorization,
		IRecordsAuthorizationService membership) : IAdminAssistEvidenceSource
	{
		public string SourceId => "Organization";
		public IReadOnlyList<string> EvidenceIds { get; } = new[] { "emptyGroupCount", "unitsWithoutType", "unitsWithoutGroup", "stationsWithoutLocation", "activePersonnelCount", "unitCount", "groupCount" };
		public async Task<IReadOnlyList<ConfigurationEvidence>> ReadAsync(AdminAssistActor actor, DateTime now, CancellationToken ct)
		{
			var groupRows = (await groups.GetAllGroupsByDepartmentIdAsync(actor.DepartmentId).WaitAsync(ct))?.ToList()
				?? throw new InvalidOperationException("Group metadata unavailable.");
			var unitRows = (await units.GetAllUnitsByDepartmentIdAsync(actor.DepartmentId).WaitAsync(ct))?.ToList()
				?? throw new InvalidOperationException("Unit metadata unavailable.");
			var members = await departments.GetAllUsersForDepartmentUnlimitedMinusDisabledAsync(actor.DepartmentId, true).WaitAsync(ct)
				?? throw new InvalidOperationException("Member metadata unavailable.");
			var limit = Math.Clamp(Config.AdminAssistConfig.MaxEvidenceRows, 1, 10000);
			if (groupRows.Count + unitRows.Count + members.Count > limit || groupRows.Any(g => g.DepartmentId != actor.DepartmentId) || unitRows.Any(u => u.DepartmentId != actor.DepartmentId))
				throw new InvalidOperationException("Scope or row bound exceeded.");
			var visibleUnits = new List<Unit>();
			foreach (var unit in unitRows)
			{
				ct.ThrowIfCancellationRequested();
				if (await authorization.CanUserViewUnitAsync(actor.UserId, unit.UnitId)) visibleUnits.Add(unit);
				else throw new UnauthorizedAccessException("The department-wide unit check requires complete visible evidence.");
			}
			var active = new HashSet<string>(StringComparer.Ordinal);
			foreach (var member in members)
			{
				ct.ThrowIfCancellationRequested();
				if (await membership.IsAssignableMemberAsync(member.UserId, actor.DepartmentId))
				{
					if (!await authorization.CanUserViewPersonAsync(actor.UserId, member.UserId, actor.DepartmentId)) throw new UnauthorizedAccessException();
					active.Add(member.UserId);
				}
			}
			ConfigurationEvidence Count(string id, decimal count) => new(id, EvidenceState.Known, SourceId, "1", now, Number: count);
			bool Coordinate(string value, decimal min, decimal max) => decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && number >= min && number <= max;
			return new[]
			{
				groupRows.Any(g => g.Members == null) ? new ConfigurationEvidence("emptyGroupCount", EvidenceState.Unknown, SourceId, "1", now, ReasonCode: "GroupMembershipUnavailable") : Count("emptyGroupCount", groupRows.Count(g => !g.Members.Any(m => active.Contains(m.UserId)))),
				Count("unitsWithoutType", visibleUnits.Count(u => string.IsNullOrWhiteSpace(u.Type))),
				Count("unitsWithoutGroup", visibleUnits.Count(u => !u.StationGroupId.HasValue || !groupRows.Any(g => g.DepartmentGroupId == u.StationGroupId))),
				Count("stationsWithoutLocation", groupRows.Count(g => g.Type == (int)DepartmentGroupTypes.Station && !g.AddressId.HasValue && !(Coordinate(g.Latitude, -90, 90) && Coordinate(g.Longitude, -180, 180)))),
				Count("activePersonnelCount", active.Count), Count("unitCount", visibleUnits.Count), Count("groupCount", groupRows.Count)
			};
		}
	}
}
