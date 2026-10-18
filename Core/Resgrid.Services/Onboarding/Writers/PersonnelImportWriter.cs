using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Model;
using Resgrid.Model.Onboarding;
using Resgrid.Model.Services;

namespace Resgrid.Services.Onboarding.Writers
{
	/// <summary>
	/// People ("personnel"), following the account rules of wizard plan section 12.3. Emails are unique system-wide and
	/// profiles are global, so: a new email gets an account with a pre-generated id, no password, an unconfirmed email
	/// and a Dormant membership; a current member of this department is matched (and, in reviewed-update mode, only
	/// department-scoped data changes); a removed member of this department comes back as a dormant member once the
	/// administrator acknowledges it; an account that belongs only to other departments is never linked or touched and
	/// becomes an "invite to join" at activation. Administrators are flagged, never granted, until activation. A reviewed
	/// update or a reactivation records exactly what it added, so rollback removes it again unless it changed since.
	/// </summary>
	public sealed class PersonnelImportWriter : IImportTargetWriter, IImportPregeneratedKeys, IImportDeferredWork
	{
		private readonly IMemberProvisioningService _provisioning;
		private readonly IMemberAccountService _accounts;
		private readonly IDepartmentsService _departments;
		private readonly IDepartmentGroupsService _groups;
		private readonly IPersonnelRolesService _roles;
		private readonly IDepartmentMemberSensitiveDataService _sensitiveData;
		private readonly IUserDefinedFieldsService _customFields;
		private readonly IDeleteService _delete;

		public PersonnelImportWriter(IMemberProvisioningService provisioning, IMemberAccountService accounts, IDepartmentsService departments,
			IDepartmentGroupsService groups, IPersonnelRolesService roles, IDepartmentMemberSensitiveDataService sensitiveData,
			IUserDefinedFieldsService customFields, IDeleteService delete)
		{
			_provisioning = provisioning;
			_accounts = accounts;
			_departments = departments;
			_groups = groups;
			_roles = roles;
			_sensitiveData = sensitiveData;
			_customFields = customFields;
			_delete = delete;
		}

		public string EntityKey => "personnel";

		public ImportEffects AffectedEffects =>
			ImportEffects.PersonnelVisibility | ImportEffects.GroupVisibility | ImportEffects.RoleVisibility | ImportEffects.DepartmentUsersCache |
			ImportEffects.ProfilesCache | ImportEffects.GroupsCache | ImportEffects.LimitsCache | ImportEffects.SearchRebuild;

		public string NewTargetKey() => Guid.NewGuid().ToString().ToUpperInvariant();

		public Task<ImportGateResult> CheckGateAsync(ImportWriteContext context, CancellationToken cancellationToken) => Task.FromResult(ImportGateResult.Ok());

		/// <summary>Current (including dormant) members of the department by normalized email, for references from other files.</summary>
		public async Task<IReadOnlyDictionary<string, string>> FindExistingAsync(int departmentId, CancellationToken cancellationToken)
		{
			var current = new HashSet<string>((await _departments.GetAllMembersForDepartmentIncludingDeletedAsync(departmentId) ?? new List<DepartmentMember>())
				.Where(m => !m.IsDeleted).Select(m => m.UserId), StringComparer.OrdinalIgnoreCase);
			var map = new Dictionary<string, string>(StringComparer.Ordinal);
			foreach (var user in await _departments.GetAllUsersForDepartmentUnlimitedAsync(departmentId, true) ?? new List<Model.Identity.IdentityUser>())
			{
				var email = ImportFieldConverter.Normalize(user.Email);
				if (email.Length > 0 && current.Contains(user.Id))
					map.TryAdd(email, user.Id);
			}
			return map;
		}

		public async Task<ImportPlanResult> PlanAsync(ImportWriteContext context, IReadOnlyList<ImportCanonicalRow> rows, CancellationToken cancellationToken)
		{
			var members = (await _departments.GetAllMembersForDepartmentIncludingDeletedAsync(context.DepartmentId) ?? new List<DepartmentMember>())
				.GroupBy(m => m.UserId, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.OrderBy(m => m.IsDeleted).First(), StringComparer.OrdinalIgnoreCase);
			var result = new ImportPlanResult();

			foreach (var row in rows)
			{
				var planned = new ImportPlannedRow { StagedRowId = row.StagedRowId, TargetKey = row.TargetKey };
				result.Rows.Add(planned);

				if (row.Decision == ImportRowDecision.Skip || row.Decision == ImportRowDecision.Unchanged)
				{
					planned.Decision = row.Decision;
					continue;
				}

				if (row.Bool("will_be_admin") == true)
					planned.Issues.Add(ImportWriterIssues.Row(row, "IMP-PERSON-WILL-BE-ADMIN", ImportIssueSeverity.Info, "will_be_admin"));
				if (row.Bool("active") == false)
					planned.Issues.Add(ImportWriterIssues.Row(row, "IMP-PERSON-INACTIVE", ImportIssueSeverity.Info, "active"));

				var account = await _accounts.FindByEmailAsync(row.Text("email"));
				if (account == null)
				{
					planned.Decision = ImportRowDecision.Create;
					planned.TargetKey = null;
					result.PersonnelSeats++;
					continue;
				}

				if (members.TryGetValue(account.Id, out var member))
				{
					if (member.IsDeleted)
					{
						// A reactivation proposal per row (ReactivateUserAsync semantics), applied as Dormant; the administrator
						// acknowledges it as a consequence at approval, or leaves the row out.
						planned.TargetKey = account.Id;
						planned.Decision = ImportRowDecision.Reactivate;
						planned.Issues.Add(ImportWriterIssues.Row(row, "IMP-PERSON-DELETED-MEMBER", ImportIssueSeverity.Info, "email"));
						result.Consequences[ReactivatesConsequence] = (result.Consequences.TryGetValue(ReactivatesConsequence, out var n) ? n : 0) + 1;
						result.PersonnelSeats++;
						continue;
					}

					planned.TargetKey = account.Id;
					planned.Decision = ImportRowDecision.Link;
					if (context.WriteMode == ImportWriteMode.ReviewedUpdate)
					{
						foreach (var key in DepartmentScopedKeys.Where(k => row.Values.ContainsKey(k)))
							planned.Changes[key] = (null, null);
						if (planned.Changes.Count > 0)
							planned.Decision = ImportRowDecision.Update;
					}
					else
					{
						planned.Issues.Add(ImportWriterIssues.Row(row, "IMP-MATCH-EXISTING", ImportIssueSeverity.Info, "email"));
					}
					continue;
				}

				// An account of other departments only: never linked, and nothing about it is shown (section 12.3).
				planned.TargetKey = null;
				planned.Decision = ImportRowDecision.InviteToJoin;
				planned.Issues.Add(ImportWriterIssues.Row(row, "IMP-PERSON-EXISTING-ACCOUNT", ImportIssueSeverity.Info, "email"));
			}

			return result;
		}

		/// <summary>The consequence a reactivation is acknowledged under.</summary>
		public const string ReactivatesConsequence = "personnel.reactivates-removed";

		/// <summary>The department-scoped fields a reviewed update may change on a matched member (never the global profile).</summary>
		private static readonly string[] DepartmentScopedKeys =
			{ "group", "group_admin", "roles", "identification_number", "notes", "home_street", "home_city", "home_state", "home_postal_code", "home_country" };

		public async Task<ImportRowWriteResult> WriteRowAsync(ImportWriteContext context, ImportCanonicalRow row, ImportJournalWriter journal, CancellationToken cancellationToken)
		{
			var result = new ImportRowWriteResult { StagedRowId = row.StagedRowId, TargetKey = row.TargetKey };

			switch (row.Decision)
			{
				case ImportRowDecision.Create:
				{
					if (!context.HasPersonnelSeat)
						return Outcome(result, ImportRowOutcome.Skipped, "IMP-PLAN-CAPACITY");

					var groupId = await ResolveIntAsync(context, "groups", row.Text("group"), cancellationToken);
					var active = row.Bool("active") != false;
					var provisioned = await _provisioning.ProvisionAsync(new MemberProvisioningRequest
					{
						DepartmentId = context.DepartmentId,
						UserId = row.TargetKey,
						// The engine's account claim, committed before the chunk transaction (IImportPregeneratedKeys).
						UserIdReserved = true,
						Email = row.Text("email"),
						// No password and an unconfirmed email: the person sets a password when they accept the invitation (section 15).
						Password = null,
						EmailConfirmed = false,
						Profile = new UserProfile
						{
							FirstName = row.Text("first_name"),
							LastName = row.Text("last_name"),
							MobileNumber = row.Text("mobile_phone"),
							HomeNumber = row.Text("home_phone"),
							TimeZone = row.Text("time_zone"),
							Language = row.Text("language")
						},
						// "Will become administrator" is applied at activation after per-person confirmation (section 12.3).
						IsAdmin = false,
						IsDisabled = !active,
						IsHidden = !active,
						ImportActivationState = (int)ImportActivationState.Dormant,
						SensitiveData = SensitiveData(context, row),
						GroupId = groupId,
						GroupAdmin = row.Bool("group_admin") == true,
						RoleIds = await RoleIdsAsync(context, row, cancellationToken),
						CustomFieldValues = await ImportCustomFields.BuildValuesAsync(_customFields, context.DepartmentId, UdfEntityType.Personnel, row.CustomFields),
						ActingUserId = context.ActingUserId
					}, context.Effects, cancellationToken);

					if (!provisioned.Succeeded)
						return Outcome(result, provisioned.ErrorCode == "account_exists" ? ImportRowOutcome.Skipped : ImportRowOutcome.Failed,
							provisioned.ErrorCode == "account_exists" ? "IMP-PERSON-EXISTING-ACCOUNT" : "IMP-PERSON-ACCOUNT-FAILED");

					context.UsePersonnelSeat();
					result.TargetKey = provisioned.UserId;
					result.Outcome = ImportRowOutcome.Created;
					await journal.AppendAsync(EntityKey, provisioned.UserId, ImportJournalOperation.Create, createdAccount: provisioned.CreatedAccount,
						cancellationToken: cancellationToken);
					return result;
				}

				case ImportRowDecision.Link:
				case ImportRowDecision.Unchanged:
				{
					var member = row.TargetKey == null ? null : await _departments.GetDepartmentMemberAsync(row.TargetKey, context.DepartmentId, true);
					if (member == null || member.IsDeleted)
						return Outcome(result, ImportRowOutcome.Skipped, "IMP-TARGET-MISSING");

					if (row.Decision == ImportRowDecision.Link)
						await journal.AppendAsync(EntityKey, row.TargetKey, ImportJournalOperation.Link, cancellationToken: cancellationToken);
					return Outcome(result, row.Decision == ImportRowDecision.Link ? ImportRowOutcome.Linked : ImportRowOutcome.Unchanged, null);
				}

				case ImportRowDecision.Update:
					return await UpdateMemberAsync(context, row, journal, result, cancellationToken);

				case ImportRowDecision.Reactivate:
					return await ReactivateMemberAsync(context, row, journal, result, cancellationToken);

				case ImportRowDecision.InviteToJoin:
					// Recorded for activation: the administrator confirms each invitation to join (section 15).
					return Outcome(result, ImportRowOutcome.Skipped, "IMP-PERSON-EXISTING-ACCOUNT");

				default:
					return Outcome(result, ImportRowOutcome.Skipped, null);
			}
		}

		/// <summary>Reviewed update of a matched member: group when they have none, additional roles, and sensitive data fields left empty.</summary>
		private async Task<ImportRowWriteResult> UpdateMemberAsync(ImportWriteContext context, ImportCanonicalRow row, ImportJournalWriter journal,
			ImportRowWriteResult result, CancellationToken cancellationToken)
		{
			var member = row.TargetKey == null ? null : await _departments.GetDepartmentMemberAsync(row.TargetKey, context.DepartmentId, true);
			if (member == null || member.IsDeleted)
				return Outcome(result, ImportRowOutcome.Skipped, "IMP-TARGET-MISSING");

			var added = await AddDepartmentDataAsync(context, row, cancellationToken);
			if (added.IsEmpty)
				return Outcome(result, ImportRowOutcome.Unchanged, null);

			await journal.AppendAsync(EntityKey, row.TargetKey, ImportJournalOperation.Update, JsonConvert.SerializeObject(added), cancellationToken: cancellationToken);
			return Outcome(result, ImportRowOutcome.Updated, null);
		}

		/// <summary>
		/// A removed member brought back: not deleted, not admin, Dormant (nothing reaches them but the invitation), hidden and
		/// disabled again only when the row says inactive; then the row's department data as in a reviewed update. One save,
		/// so the membership is never active in between.
		/// </summary>
		private async Task<ImportRowWriteResult> ReactivateMemberAsync(ImportWriteContext context, ImportCanonicalRow row, ImportJournalWriter journal,
			ImportRowWriteResult result, CancellationToken cancellationToken)
		{
			var member = row.TargetKey == null ? null : await _departments.GetDepartmentMemberAsync(row.TargetKey, context.DepartmentId, true);
			if (member == null)
				return Outcome(result, ImportRowOutcome.Skipped, "IMP-TARGET-MISSING");
			if (!member.IsDeleted)
			{
				// Someone brought them back since the preview: a plain match now.
				await journal.AppendAsync(EntityKey, row.TargetKey, ImportJournalOperation.Link, cancellationToken: cancellationToken);
				return Outcome(result, ImportRowOutcome.Linked, null);
			}
			if (!context.HasPersonnelSeat)
				return Outcome(result, ImportRowOutcome.Skipped, "IMP-PLAN-CAPACITY");

			var before = new ReactivatedMember
			{
				IsHidden = member.IsHidden,
				IsDisabled = member.IsDisabled,
				IsAdmin = member.IsAdmin,
				ImportActivationState = member.ImportActivationState
			};
			var active = row.Bool("active") != false;
			member.IsDeleted = false;
			member.IsAdmin = false;
			member.IsHidden = !active;
			member.IsDisabled = !active;
			member.ImportActivationState = (int)ImportActivationState.Dormant;
			await _departments.SaveDepartmentMemberAsync(member, cancellationToken);
			context.UsePersonnelSeat();

			before.Added = await AddDepartmentDataAsync(context, row, cancellationToken);
			await journal.AppendAsync(EntityKey, row.TargetKey, ImportJournalOperation.Reactivate, JsonConvert.SerializeObject(before), cancellationToken: cancellationToken);
			result.TargetKey = row.TargetKey;
			return Outcome(result, ImportRowOutcome.Updated, null);
		}

		/// <summary>What a reviewed update or a reactivation added to a member, recorded so rollback can take exactly that back.</summary>
		public sealed class MemberAdditions
		{
			[JsonProperty("group_added")]
			public int? GroupId { get; set; }

			[JsonProperty("roles_added_ids")]
			public List<int> RoleIds { get; set; } = new List<int>();

			/// <summary>The created sensitive-data row's write stamp: rollback deletes it only while it is unchanged.</summary>
			[JsonProperty("sensitive_data_version")]
			public string SensitiveDataVersion { get; set; }

			[JsonIgnore]
			public bool IsEmpty => GroupId == null && RoleIds.Count == 0 && SensitiveDataVersion == null;
		}

		/// <summary>The membership as it was before a reactivation, and what the reactivation added.</summary>
		public sealed class ReactivatedMember
		{
			public bool? IsHidden { get; set; }
			public bool? IsDisabled { get; set; }
			public bool? IsAdmin { get; set; }
			public int? ImportActivationState { get; set; }
			public MemberAdditions Added { get; set; } = new MemberAdditions();
		}

		/// <summary>Group when they have none, roles they do not hold, and sensitive data when none is stored.</summary>
		private async Task<MemberAdditions> AddDepartmentDataAsync(ImportWriteContext context, ImportCanonicalRow row, CancellationToken cancellationToken)
		{
			var added = new MemberAdditions();
			var groupId = await ResolveIntAsync(context, "groups", row.Text("group"), cancellationToken);
			if (groupId != null && await _groups.GetGroupMemberForUserAsync(row.TargetKey, context.DepartmentId) == null)
			{
				await _groups.SaveGroupMemberAsync(new DepartmentGroupMember
				{
					DepartmentGroupId = groupId.Value,
					DepartmentId = context.DepartmentId,
					UserId = row.TargetKey,
					IsAdmin = row.Bool("group_admin") == true
				}, context.Effects, cancellationToken);
				added.GroupId = groupId;
			}

			added.RoleIds = await AddRolesAsync(context, row.TargetKey, await RoleIdsAsync(context, row, cancellationToken), cancellationToken);

			var incoming = SensitiveData(context, row);
			if (incoming != null && await _sensitiveData.GetByDepartmentAndUserAsync(context.DepartmentId, row.TargetKey) == null)
			{
				incoming.UserId = row.TargetKey;
				incoming.LegacyProfileRelocatedOn ??= DateTime.UtcNow;
				var saved = await _sensitiveData.SaveAsync(incoming, context.Effects, cancellationToken) ?? incoming;
				added.SensitiveDataVersion = Stamp(saved);
			}

			return added;
		}

		private static string Stamp(DepartmentMemberSensitiveData data) =>
			(data.UpdatedOn ?? data.CreatedOn).ToUniversalTime().ToString("o", System.Globalization.CultureInfo.InvariantCulture);

		/// <summary>
		/// Takes back what an import added to a member, piece by piece. Anything changed since (another group now, sensitive
		/// data edited) is left alone and reported; returns whether everything came back.
		/// </summary>
		private async Task<bool> RemoveAdditionsAsync(int departmentId, string userId, MemberAdditions added, CancellationToken cancellationToken)
		{
			var complete = true;
			if (added.GroupId != null)
			{
				var current = await _groups.GetGroupMemberForUserAsync(userId, departmentId);
				if (current?.DepartmentGroupId == added.GroupId)
					await _groups.DeleteUserFromGroupsAsync(userId, departmentId, cancellationToken);
				else if (current != null)
					complete = false;
			}

			foreach (var roleId in added.RoleIds ?? new List<int>())
			{
				var holding = (await _roles.GetAllMembersOfRoleAsync(roleId) ?? new List<PersonnelRoleUser>())
					.Where(u => u.DepartmentId == departmentId && string.Equals(u.UserId, userId, StringComparison.OrdinalIgnoreCase)).ToList();
				if (holding.Count > 0)
					await _roles.DeleteRoleUsersAsync(holding, cancellationToken);
			}

			if (added.SensitiveDataVersion != null)
			{
				var current = await _sensitiveData.GetByDepartmentAndUserAsync(departmentId, userId);
				if (current != null && Stamp(current) == added.SensitiveDataVersion)
					await _sensitiveData.DeleteForMemberAsync(departmentId, userId, cancellationToken);
				else if (current != null)
					complete = false;
			}

			return complete;
		}

		private static DepartmentMemberSensitiveData SensitiveData(ImportWriteContext context, ImportCanonicalRow row)
		{
			var data = new DepartmentMemberSensitiveData
			{
				DepartmentId = context.DepartmentId,
				IdentificationNumber = row.Text("identification_number"),
				Notes = row.Text("notes"),
				HomeAddress1 = row.Text("home_street"),
				HomeCity = row.Text("home_city"),
				HomeState = row.Text("home_state"),
				HomePostalCode = row.Text("home_postal_code"),
				HomeCountry = row.Text("home_country")
			};
			return new[] { data.IdentificationNumber, data.Notes, data.HomeAddress1, data.HomeCity, data.HomeState, data.HomePostalCode, data.HomeCountry }
				.Any(v => !string.IsNullOrWhiteSpace(v)) ? data : null;
		}

		private static async Task<List<int>> RoleIdsAsync(ImportWriteContext context, ImportCanonicalRow row, CancellationToken cancellationToken)
		{
			var ids = new List<int>();
			foreach (var token in row.List("roles"))
			{
				var id = await ResolveIntAsync(context, "roles", token, cancellationToken);
				if (id != null && !ids.Contains(id.Value))
					ids.Add(id.Value);
			}
			return ids;
		}

		private static async Task<int?> ResolveIntAsync(ImportWriteContext context, string entityKey, string token, CancellationToken cancellationToken)
		{
			if (string.IsNullOrWhiteSpace(token) || ImportReferenceToken.Is(token, ImportReferenceToken.Label))
				return null;
			return ImportCanonical.ToNullableInt(await context.ResolveReference(entityKey, token, cancellationToken));
		}

		/// <summary>Adds the roles the member does not hold; a role blocked by certification enforcement is left for the deferred pass.</summary>
		private async Task<List<int>> AddRolesAsync(ImportWriteContext context, string userId, IReadOnlyCollection<int> roleIds, CancellationToken cancellationToken)
		{
			var added = new List<int>();
			if (roleIds.Count == 0)
				return added;

			var held = (await _roles.GetRolesForUserAsync(userId, context.DepartmentId))?.Select(r => r.PersonnelRoleId).ToHashSet() ?? new HashSet<int>();
			foreach (var roleId in roleIds.Where(r => !held.Contains(r)))
			{
				var role = await _roles.GetRoleByIdAsync(roleId);
				if (role == null || role.DepartmentId != context.DepartmentId)
					continue;

				role.Users = new List<PersonnelRoleUser> { new PersonnelRoleUser { PersonnelRoleId = roleId, DepartmentId = context.DepartmentId, UserId = userId } };
				try
				{
					await _roles.SaveRoleAsync(role, context.Effects, cancellationToken, context.ActingUserId);
					added.Add(roleId);
				}
				catch (RoleMembershipException ex) when (ex.Message == RoleMembershipException.RequirementsUnmet)
				{
					// Certifications import after people: the deferred pass retries once they exist.
				}
			}

			return added;
		}

		/// <summary>After every dataset is written: roles that certification enforcement refused while certifications were not yet imported.</summary>
		public async Task<int> CompleteDeferredAsync(ImportWriteContext context, IReadOnlyList<ImportCanonicalRow> writtenRows, ImportJournalWriter journal,
			CancellationToken cancellationToken)
		{
			var changed = 0;
			foreach (var row in writtenRows.Where(r => r.TargetKey != null && r.Decision == ImportRowDecision.Create && r.List("roles").Count > 0))
				if ((await AddRolesAsync(context, row.TargetKey, await RoleIdsAsync(context, row, cancellationToken), cancellationToken)).Count > 0)
					changed++;
			return changed;
		}

		public async Task<ImportRollbackEntryResult> RollbackAsync(ImportWriteContext context, ImportChangeJournalEntry entry, CancellationToken cancellationToken)
		{
			var result = new ImportRollbackEntryResult { JournalEntryId = entry.Id };
			if (entry.Operation == (int)ImportJournalOperation.Update)
				return await RollbackUpdateAsync(context, entry, result, cancellationToken);
			if (entry.Operation == (int)ImportJournalOperation.Reactivate)
				return await RollbackReactivationAsync(context, entry, result, cancellationToken);
			if (entry.Operation != (int)ImportJournalOperation.Create)
			{
				// A link changed nothing.
				result.Outcome = ImportRollbackOutcome.None;
				return result;
			}

			var member = await _departments.GetDepartmentMemberAsync(entry.TargetKey, context.DepartmentId, true);
			if (member == null || member.IsDeleted)
			{
				result.Outcome = ImportRollbackOutcome.Deleted;
				return result;
			}

			// A person who accepted the invitation is real now: never removed by rollback (section 11.8).
			if (member.ImportActivationState == (int)ImportActivationState.Activated || !member.IsImportDormant && member.ImportActivationState != null)
			{
				result.Outcome = ImportRollbackOutcome.ManualReconciliation;
				result.Code = "IMP-ROLLBACK-HAS-ACTIVITY";
				return result;
			}

			var memberships = await _departments.GetAllDepartmentsForUserAsync(entry.TargetKey) ?? new List<DepartmentMember>();
			var elsewhere = memberships.Any(m => m.DepartmentId != context.DepartmentId && !m.IsDeleted);
			if (entry.CreatedAccount && !elsewhere)
			{
				// The account exists only because of this import and never signed in: deactivated with the membership.
				var deleted = await _delete.DeleteUserAsync(context.DepartmentId, context.ActingUserId, entry.TargetKey, cancellationToken);
				result.Outcome = deleted == DeleteUserResults.NoFailure ? ImportRollbackOutcome.Deleted : ImportRollbackOutcome.ManualReconciliation;
				result.Code = deleted == DeleteUserResults.NoFailure ? null : "IMP-ROLLBACK-ACCOUNT";
				return result;
			}

			await _delete.RevokeDepartmentAccessAsync(entry.TargetKey, context.DepartmentId, context.ActingUserId, cancellationToken);
			result.Outcome = ImportRollbackOutcome.MembershipRemoved;
			return result;
		}

		/// <summary>A reviewed update only added department data: take back exactly what it added, unless it changed since.</summary>
		private async Task<ImportRollbackEntryResult> RollbackUpdateAsync(ImportWriteContext context, ImportChangeJournalEntry entry, ImportRollbackEntryResult result,
			CancellationToken cancellationToken)
		{
			var added = Read<MemberAdditions>(entry.BeforeValues);
			// An entry from before additions were recorded (group, sensitive data with no stamp) cannot be undone safely.
			if (added == null || HasLegacyKeys(entry.BeforeValues))
				return Manual(result, "IMP-ROLLBACK-MEMBER-UPDATE");

			return await RemoveAdditionsAsync(context.DepartmentId, entry.TargetKey, added, cancellationToken)
				? Restored(result)
				: Manual(result, "IMP-ROLLBACK-MEMBER-UPDATE");
		}

		/// <summary>A reactivated member goes back to removed, with the flags they had, unless they accepted their invitation since.</summary>
		private async Task<ImportRollbackEntryResult> RollbackReactivationAsync(ImportWriteContext context, ImportChangeJournalEntry entry, ImportRollbackEntryResult result,
			CancellationToken cancellationToken)
		{
			var before = Read<ReactivatedMember>(entry.BeforeValues);
			var member = await _departments.GetDepartmentMemberAsync(entry.TargetKey, context.DepartmentId, true);
			if (before == null || member == null)
				return Manual(result, "IMP-ROLLBACK-MEMBER-UPDATE");
			if (member.IsDeleted)
			{
				result.Outcome = ImportRollbackOutcome.MembershipRemoved;
				return result;
			}
			// They accepted the invitation (or someone activated them): a real member again, never removed by rollback (section 11.8).
			if (!member.IsImportDormant)
				return Manual(result, "IMP-ROLLBACK-HAS-ACTIVITY");

			var complete = await RemoveAdditionsAsync(context.DepartmentId, entry.TargetKey, before.Added ?? new MemberAdditions(), cancellationToken);
			member.IsDeleted = true;
			member.IsHidden = before.IsHidden;
			member.IsDisabled = before.IsDisabled;
			member.IsAdmin = before.IsAdmin;
			member.ImportActivationState = before.ImportActivationState;
			await _departments.SaveDepartmentMemberAsync(member, cancellationToken);

			if (!complete)
				return Manual(result, "IMP-ROLLBACK-MEMBER-UPDATE");
			result.Outcome = ImportRollbackOutcome.MembershipRemoved;
			return result;
		}

		private static T Read<T>(string json) where T : class
		{
			if (string.IsNullOrWhiteSpace(json))
				return null;
			try
			{
				return JsonConvert.DeserializeObject<T>(json);
			}
			catch (JsonException)
			{
				return null;
			}
		}

		private static bool HasLegacyKeys(string json) =>
			json != null && (json.Contains("\"group\":", StringComparison.Ordinal) || json.Contains("\"sensitive_data\":", StringComparison.Ordinal) ||
				json.Contains("\"roles_added\":", StringComparison.Ordinal));

		private static ImportRollbackEntryResult Manual(ImportRollbackEntryResult result, string code)
		{
			result.Outcome = ImportRollbackOutcome.ManualReconciliation;
			result.Code = code;
			return result;
		}

		private static ImportRollbackEntryResult Restored(ImportRollbackEntryResult result)
		{
			result.Outcome = ImportRollbackOutcome.Restored;
			return result;
		}

		public async Task<ImportVerifyResult> VerifyAsync(ImportWriteContext context, IReadOnlyList<ImportExternalId> written, CancellationToken cancellationToken)
		{
			var current = new HashSet<string>((await _departments.GetAllMembersForDepartmentIncludingDeletedAsync(context.DepartmentId) ?? new List<DepartmentMember>())
				.Where(m => !m.IsDeleted).Select(m => m.UserId), StringComparer.OrdinalIgnoreCase);
			var expected = written.Where(w => w.TargetKey != null).Select(w => w.TargetKey).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			var result = new ImportVerifyResult();
			result.Checks[EntityKey + ".present"] = (expected.Count, expected.Count(current.Contains));
			return result;
		}

		private static ImportRowWriteResult Outcome(ImportRowWriteResult result, ImportRowOutcome outcome, string code)
		{
			result.Outcome = outcome;
			result.Code = code;
			return result;
		}
	}
}
