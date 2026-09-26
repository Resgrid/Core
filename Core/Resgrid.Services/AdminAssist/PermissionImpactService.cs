using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Helpers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.AdminAssist
{
	/// <summary>Evaluates actors, not the targets returned by GetAllowedUsersAsync. No permission writes or claim refresh.</summary>
	public sealed class PermissionImpactService(IAdminAssistAccessService access, IAdminAssistRepository repository,
		IAdminAssistCatalog catalog, IDepartmentsService departments, IDepartmentMembersRepository members,
		IDepartmentGroupsRepository groups, IPersonnelRolesRepository roles, IPersonnelRoleUsersRepository roleMembers,
		IUnitsRepository units, IPermissionsRepository permissions, IPermissionsService policy, TimeProvider clock) : IPermissionImpactService, IAdminAssistPermissionEvaluator
	{
		public static readonly IReadOnlyList<string> Supported = Array.AsReadOnly(new[] {
			nameof(PermissionTypes.CreateCall), nameof(PermissionTypes.CreateNote), nameof(PermissionTypes.ViewPersonalInfo),
			nameof(PermissionTypes.ViewGroupUsers), nameof(PermissionTypes.ViewGroupUnits),
			nameof(PermissionTypes.CanSeePersonnelLocations), nameof(PermissionTypes.CanSeeUnitLocations) });
		private sealed record Person(string Id, bool Admin, int? Group, bool GroupAdmin, int[] Roles);
		private sealed record Group(int Id, int? Parent, string[] Admins);
		private sealed record Target(string Id, int? Group);
		private sealed record Inputs(Person[] People, Group[] Groups, Target[] Targets, int[] OwnedRoles, Permission Current);
		private static bool Scoped(PermissionTypes type) => type is PermissionTypes.ViewGroupUsers or PermissionTypes.ViewGroupUnits or PermissionTypes.CanSeePersonnelLocations or PermissionTypes.CanSeeUnitLocations;
		private static bool UnitScope(PermissionTypes type) => type is PermissionTypes.ViewGroupUnits or PermissionTypes.CanSeeUnitLocations;
		public async Task<IReadOnlyDictionary<string, bool>> EvaluateCurrentTargetsAsync(AdminAssistActor administrator, string permissionType, IReadOnlyList<string> targetIds, CancellationToken ct)
		{
			if (!await access.CanAccessAsync(administrator, false, ct).WaitAsync(ct)) throw new UnauthorizedAccessException();
			if (!Supported.Contains(permissionType) || !Enum.TryParse<PermissionTypes>(permissionType, out var type) || !Scoped(type) || targetIds == null || targetIds.Count > Math.Clamp(Config.AdminAssistConfig.MaxEvidenceRows, 1, 10000) || targetIds.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("Invalid visibility scope.");
			try {
				// One viewer against at most MaxEvidenceRows targets, so the department-wide comparison bound does not apply.
				var input = await ReadAsync(administrator, type, ct, boundComparisons: false);
				var requested = targetIds.ToHashSet(StringComparer.Ordinal);
				var selected = input with { People = input.People.Where(p => p.Id == administrator.UserId).ToArray(), Targets = input.Targets.Where(t => requested.Contains(t.Id)).ToArray() };
				var allowed = Evaluate(selected, input.Current, true, ct);
				if (Fingerprint(input) != Fingerprint(await ReadAsync(administrator, type, ct, boundComparisons: false))) return null;
				if (!await access.CanAccessAsync(administrator, false, ct).WaitAsync(ct)) throw new UnauthorizedAccessException();
				return requested.ToDictionary(id => id, id => allowed.Contains((administrator.UserId, id)), StringComparer.Ordinal);
			} catch (OperationCanceledException) { throw; } catch (UnauthorizedAccessException) { throw; } catch (Exception) { return null; }
		}
		public async Task<bool?> EvaluateCurrentAsync(AdminAssistActor administrator, string memberId, string permissionType, string targetId, CancellationToken ct)
		{
			if (!await access.CanAccessAsync(administrator, false, ct).WaitAsync(ct)) throw new UnauthorizedAccessException();
			if (!Supported.Contains(permissionType) || !Enum.TryParse<PermissionTypes>(permissionType, out var type)) throw new ArgumentException("Unsupported permission.");
			if (Scoped(type) && string.IsNullOrWhiteSpace(targetId)) return null;
			try
			{
				var input = await ReadAsync(administrator, type, ct, boundComparisons: false);
				var target = Scoped(type) ? targetId : "department-action";
				// Evaluate a single edge from fresh repository inputs. Do not trust the legacy
				// visibility cache: a missing matrix currently fails open in that API.
				var selected = input with { People = input.People.Where(p => p.Id == memberId).ToArray(), Targets = input.Targets.Where(t => t.Id == target).ToArray() };
				var allowed = Evaluate(selected, input.Current, Scoped(type), ct).Contains((memberId, target));
				if (Fingerprint(input) != Fingerprint(await ReadAsync(administrator, type, ct, boundComparisons: false))) return null;
				if (!await access.CanAccessAsync(administrator, false, ct).WaitAsync(ct)) throw new UnauthorizedAccessException();
				return allowed;
			}
			catch (OperationCanceledException) { throw; }
			catch (UnauthorizedAccessException) { throw; }
			catch (Exception) { return null; }
		}
		public async Task<IReadOnlyList<PermissionRoleOption>> GetRoleOptionsAsync(AdminAssistActor actor, string expectedRevision, CancellationToken ct = default)
		{
			if (!await access.CanAccessAsync(actor, false, ct)) throw new UnauthorizedAccessException();
			using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
			timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(Config.AdminAssistConfig.SnapshotTimeoutSeconds, 1, 60)));
			ct = timeout.Token;
			var revision = (await repository.GetConfigurationRevisionAsync(actor.DepartmentId, ct)).ToString(CultureInfo.InvariantCulture);
			if (revision != expectedRevision) throw new AdminAssistConcurrencyException();
			async Task<PermissionRoleOption[]> Read()
			{
				var rows = (await roles.GetPersonnelRolesByDepartmentIdAsync(actor.DepartmentId).WaitAsync(ct))?.ToArray() ?? throw new InvalidOperationException();
				if (rows.Length > Math.Clamp(Config.AdminAssistConfig.MaxEvidenceRows, 1, 10000) || rows.Any(r => r.DepartmentId != actor.DepartmentId || r.PersonnelRoleId <= 0 || string.IsNullOrWhiteSpace(r.Name)) ||
					rows.Select(r => r.PersonnelRoleId).Distinct().Count() != rows.Length) throw new InvalidOperationException();
				// Role names are ordinary organization metadata; no member names, contacts or grant-protected fields.
				return rows.OrderBy(r => r.PersonnelRoleId).Select(r => new PermissionRoleOption(r.PersonnelRoleId, r.Name)).ToArray();
			}
			var options = await Read();
			if (!options.SequenceEqual(await Read()) || (await repository.GetConfigurationRevisionAsync(actor.DepartmentId, ct)).ToString(CultureInfo.InvariantCulture) != revision) throw new AdminAssistConcurrencyException();
			if (!await access.CanAccessAsync(actor, false, ct)) throw new UnauthorizedAccessException();
			return options.OrderBy(o => o.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(o => o.Id).ToArray();
		}
		public async Task<ConfigurationImpactReport> PreviewAsync(AdminAssistActor actor, PermissionImpactRequest request, CancellationToken ct = default)
		{
			if (!await access.CanAccessAsync(actor, false, ct)) throw new UnauthorizedAccessException();
			if (request == null || !Supported.Contains(request.PermissionType, StringComparer.Ordinal) ||
				request.Action < 0 || request.Action > 3 || request.RoleIds == null || request.RoleIds.Length > 100 ||
				request.RoleIds.Any(id => id <= 0) || request.RoleIds.Distinct().Count() != request.RoleIds.Length ||
				request.Action != 2 && request.RoleIds.Length != 0) throw new ArgumentException("Unsupported permission proposal.");
			var type = Enum.Parse<PermissionTypes>(request.PermissionType);
			if (!Scoped(type) && request.LockToGroup) throw new ArgumentException("This action does not use a resource group lock.");
			using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
			timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(Config.AdminAssistConfig.SnapshotTimeoutSeconds, 1, 60)));
			ct = timeout.Token;
			var revision = (await repository.GetConfigurationRevisionAsync(actor.DepartmentId, ct)).ToString(CultureInfo.InvariantCulture);
			if (revision != request.ExpectedRevision) throw new AdminAssistConcurrencyException();
			var now = clock.GetUtcNow().UtcDateTime;
			var metrics = new List<ConfigurationImpactMetric>();
			try
			{
				var input = await ReadAsync(actor, type, ct);
				if (request.RoleIds.Except(input.OwnedRoles).Any()) throw new ArgumentException("A selected role is unavailable in this department.");
				var proposed = new Permission { DepartmentId = actor.DepartmentId, PermissionType = (int)type,
					Action = request.Action, LockToGroup = request.LockToGroup, Data = string.Join(",", request.RoleIds.OrderBy(id => id)) };
				var before = Evaluate(input, input.Current, Scoped(type), ct);
				var after = Evaluate(input, proposed, Scoped(type), ct);
				if (Fingerprint(input) != Fingerprint(await ReadAsync(actor, type, ct))) throw new AdminAssistConcurrencyException();
				void Count(string key, int oldValue, int newValue) => metrics.Add(new("Impact." + key, EvidenceState.Known, oldValue, newValue));
				Count("PermissionActors", before.Select(p => p.Actor).Distinct().Count(), after.Select(p => p.Actor).Distinct().Count());
				Count("PermissionEdges", before.Count, after.Count);
				Count("PermissionGained", 0, after.Except(before).Count());
				Count("PermissionLost", 0, before.Except(after).Count());
				Count("PermissionSample", input.People.Length, input.People.Length);
				Count("PermissionTargets", input.Targets.Length, input.Targets.Length);
			}
			catch (ArgumentException) { throw; }
			catch (AdminAssistConcurrencyException) { throw; }
			catch (UnauthorizedAccessException) { throw; }
			catch (OperationCanceledException) { throw; }
			catch (Exception) { metrics.Clear(); metrics.Add(new("Impact.PermissionActors", EvidenceState.Unknown, null, null, "SourceUnavailable")); }
			if ((await repository.GetConfigurationRevisionAsync(actor.DepartmentId, ct)).ToString(CultureInfo.InvariantCulture) != revision) throw new AdminAssistConcurrencyException();
			if (!await access.CanAccessAsync(actor, false, ct)) throw new UnauthorizedAccessException();
			var entry = catalog.Settings.Single(e => e.Id == "permission." + request.PermissionType);
			return new(entry.Id, revision, now, "permission-impact-v1", entry.Impact, metrics, Array.Empty<ConfigurationImpactRule>(),
				new[] { "Impact.NoMutation", "Impact.PermissionScope", "Impact.PermissionTiming", "Impact.Window" }, entry.Location.Url);
		}
		private HashSet<(string Actor, string Target)> Evaluate(Inputs input, Permission permission, bool scoped, CancellationToken ct)
		{
			var result = new HashSet<(string, string)>();
			var byGroup = input.Groups.ToDictionary(g => g.Id);
			foreach (var person in input.People)
			{
				ct.ThrowIfCancellationRequested();
				foreach (var target in input.Targets)
				{
					bool ancestorAdmin = false; var groupId = target.Group; var visited = new HashSet<int>();
					while (groupId.HasValue)
					{
						if (!visited.Add(groupId.Value) || !byGroup.TryGetValue(groupId.Value, out var group)) throw new InvalidOperationException("Invalid group hierarchy.");
						ancestorAdmin |= group.Admins.Contains(person.Id, StringComparer.OrdinalIgnoreCase); groupId = group.Parent;
					}
					var allowed = scoped ? ResourceVisibilityPermission.Allows(permission, person.Admin, person.GroupAdmin, person.Group, target.Group, person.Roles, ancestorAdmin) :
						policy.IsUserAllowed(permission, person.Admin, person.GroupAdmin, person.Roles.Select(id => new PersonnelRole { PersonnelRoleId = id }).ToList());
					if (allowed) result.Add((person.Id, target.Id));
				}
			}
			return result;
		}
		private async Task<Inputs> ReadAsync(AdminAssistActor actor, PermissionTypes type, CancellationToken ct, bool boundComparisons = true)
		{
			var department = await departments.GetDepartmentByIdAsync(actor.DepartmentId, true).WaitAsync(ct) ?? throw new InvalidOperationException();
			var memberRows = (await members.GetAllDepartmentMembersUnlimitedAsync(actor.DepartmentId).WaitAsync(ct))?.ToArray() ?? throw new InvalidOperationException();
			var groupRows = (await groups.GetAllGroupsByDepartmentIdAsync(actor.DepartmentId).WaitAsync(ct))?.ToArray() ?? throw new InvalidOperationException();
			var roleRows = (await roles.GetPersonnelRolesByDepartmentIdAsync(actor.DepartmentId).WaitAsync(ct))?.ToArray() ?? throw new InvalidOperationException();
			var assignments = (await roleMembers.GetAllRoleUsersForDepartmentAsync(actor.DepartmentId).WaitAsync(ct))?.ToArray() ?? throw new InvalidOperationException();
			var permissionRows = (await permissions.GetAllByDepartmentIdAsync(actor.DepartmentId).WaitAsync(ct))?.ToArray() ?? throw new InvalidOperationException();
			var bound = Math.Clamp(Config.AdminAssistConfig.MaxEvidenceRows, 1, 10000);
			if (department.DepartmentId != actor.DepartmentId || memberRows.Any(m => m.DepartmentId != actor.DepartmentId) ||
				groupRows.Any(g => g.DepartmentId != actor.DepartmentId || g.Members == null || g.Members.Any(m => m.DepartmentId != actor.DepartmentId || m.DepartmentGroupId != g.DepartmentGroupId)) ||
				roleRows.Any(r => r.DepartmentId != actor.DepartmentId) || permissionRows.Any(p => p.DepartmentId != actor.DepartmentId)) throw new InvalidOperationException("Invalid tenant source.");
			if (memberRows.Length + groupRows.Length + groupRows.Sum(g => g.Members.Count) + roleRows.Length + assignments.Length + permissionRows.Length > bound) throw new InvalidOperationException("Permission evidence bound exceeded.");
			var ownedRoles = roleRows.Select(r => r.PersonnelRoleId).OrderBy(id => id).ToArray();
			if (assignments.Any(a => !ownedRoles.Contains(a.PersonnelRoleId))) throw new InvalidOperationException();
			var byUser = groupRows.SelectMany(g => g.Members).GroupBy(m => m.UserId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
			var people = memberRows.Where(m => DepartmentMemberStateHelper.IsCurrentMember(m, actor.DepartmentId)).Select(m => {
				byUser.TryGetValue(m.UserId, out var memberships);
				// The owning service takes its first group without stable ordering. Do not guess when several exist.
				if (memberships?.Length > 1) throw new InvalidOperationException("Ambiguous group membership.");
				var group = memberships?.SingleOrDefault();
				return new Person(m.UserId, m.IsAdmin.GetValueOrDefault() || m.UserId == department.ManagingUserId, group?.DepartmentGroupId,
					group?.IsAdmin == true, assignments.Where(a => a.UserId == m.UserId).Select(a => a.PersonnelRoleId).Distinct().OrderBy(id => id).ToArray());
			}).OrderBy(p => p.Id, StringComparer.Ordinal).ToArray();
			if (people.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count() != people.Length) throw new InvalidOperationException();
			Target[] targets;
			if (UnitScope(type))
			{
				var unitRows = (await units.GetAllUnitsByDepartmentIdAsync(actor.DepartmentId).WaitAsync(ct))?.ToArray() ?? throw new InvalidOperationException();
				if (unitRows.Length > bound || unitRows.Any(u => u.DepartmentId != actor.DepartmentId)) throw new InvalidOperationException();
				targets = unitRows.OrderBy(u => u.UnitId).Select(u => new Target(u.UnitId.ToString(CultureInfo.InvariantCulture), u.StationGroupId)).ToArray();
			}
			else targets = Scoped(type) ? people.Select(p => new Target(p.Id, p.Group)).ToArray() : new[] { new Target("department-action", null) };
			// Bounds the department-wide matrix PreviewAsync evaluates; single-viewer callers opt out and stay under the row bound.
			if (boundComparisons && (long)people.Length * targets.Length > 100000) throw new InvalidOperationException("Permission comparison bound exceeded.");
			var current = permissionRows.SingleOrDefault(p => p.PermissionType == (int)type);
			if (current != null && (!Enum.IsDefined(typeof(PermissionActions), current.Action) || (!string.IsNullOrWhiteSpace(current.Data) &&
				current.Action == 2 && current.Data.Split(',').Any(id => !int.TryParse(id, out var parsed) || !ownedRoles.Contains(parsed))))) throw new InvalidOperationException("Invalid current permission.");
			return new(people, groupRows.OrderBy(g => g.DepartmentGroupId).Select(g => new Group(g.DepartmentGroupId, g.ParentDepartmentGroupId,
				g.Members.Where(m => m.IsAdmin == true).Select(m => m.UserId).OrderBy(id => id, StringComparer.Ordinal).ToArray())).ToArray(), targets, ownedRoles,
				current == null ? null : new Permission { Action = current.Action, LockToGroup = current.LockToGroup, Data = current.Data });
		}
		private static string Fingerprint(Inputs input) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(input))));
	}
}
