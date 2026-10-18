using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Onboarding;
using Resgrid.Model.Services;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;

namespace Resgrid.Services
{
	/// <summary>
	/// Creates a member in one idempotent pass (Department Import plan section 11.2): every step is keyed by the target user
	/// id and skipped when already done, so a crash mid-way is finished by the retry rather than duplicated. The interactive
	/// policy reproduces what AddPerson and SCIM did inline; the import policy raises no events, sends nothing and defers
	/// visibility refresh and search projection to the import engine.
	/// </summary>
	public class MemberProvisioningService : IMemberProvisioningService
	{
		private readonly IMemberAccountService _accounts;
		private readonly IUsersService _usersService;
		private readonly IUserProfileService _profiles;
		private readonly IDepartmentsService _departments;
		private readonly IDepartmentMemberSensitiveDataService _sensitiveData;
		private readonly IDepartmentGroupsService _groups;
		private readonly IPersonnelRolesService _roles;
		private readonly IUserDefinedFieldsService _customFields;

		public MemberProvisioningService(IMemberAccountService accounts, IUsersService usersService, IUserProfileService profiles, IDepartmentsService departments,
			IDepartmentMemberSensitiveDataService sensitiveData, IDepartmentGroupsService groups, IPersonnelRolesService roles, IUserDefinedFieldsService customFields)
		{
			_accounts = accounts;
			_usersService = usersService;
			_profiles = profiles;
			_departments = departments;
			_sensitiveData = sensitiveData;
			_groups = groups;
			_roles = roles;
			_customFields = customFields;
		}

		public async Task<MemberProvisioningResult> ProvisionAsync(MemberProvisioningRequest request, ImportEffectPolicy effects, CancellationToken cancellationToken = default)
		{
			if (request == null || request.DepartmentId <= 0 || string.IsNullOrWhiteSpace(request.Email))
				return new MemberProvisioningResult { ErrorCode = "account_request_invalid" };

			var result = new MemberProvisioningResult { UserId = request.UserId };

			// ---- 1. Account (pre-generated id; a retry finds it) -------------------------------------------------------
			var user = await _accounts.FindByIdAsync(request.UserId);
			if (user == null)
			{
				var byEmail = await _accounts.FindByEmailAsync(request.Email);
				if (byEmail != null)
				{
					// Emails are unique system-wide: an account that already exists is never re-created or silently linked here.
					result.UserId = byEmail.Id;
					result.ErrorCode = "account_exists";
					return result;
				}

				user = new IdentityUser
				{
					Id = string.IsNullOrWhiteSpace(request.UserId) ? Guid.NewGuid().ToString().ToUpper() : request.UserId,
					UserName = request.UserName ?? await GenerateUserNameAsync(request.Email),
					Email = request.Email,
					EmailConfirmed = request.EmailConfirmed,
					SecurityStamp = Guid.NewGuid().ToString().ToUpper()
				};

				var created = await _accounts.CreateAsync(user, request.Password);
				if (!created.Succeeded)
				{
					result.ErrorCode = created.Errors.Contains("DuplicateUserName") ? "account_username_taken"
						: created.Errors.Contains("DuplicateEmail") ? "account_exists" : "account_create_failed";
					return result;
				}

				result.CreatedAccount = true;
			}

			result.UserId = user.Id;
			result.UserName = user.UserName;

			// ---- 2. Role and extension rows (InitUserExtInfo is not idempotent and confirms the email) ----------------
			if (request.InitializeAccountExtras)
				_usersService.AddUserToUserRole(user.Id);
			if (result.CreatedAccount && request.InitializeAccountExtras)
			{
				try
				{
					_usersService.InitUserExtInfo(user.Id);
				}
				catch (Exception ex)
				{
					// Already initialized by an earlier attempt; the caller's original AddPerson path tolerates this the same way.
					Logging.LogException(ex, "InitUserExtInfo during member provisioning");
				}

				// InitUserExtInfo marks the email confirmed; an imported, not-yet-invited account must stay unconfirmed so it
				// cannot recover a password before activation (section 14.3).
				if (!request.EmailConfirmed)
				{
					var reloaded = await _accounts.FindByIdAsync(user.Id) ?? user;
					await _accounts.SetEmailConfirmedAsync(reloaded, false);
				}
			}

			// ---- 3. Profile (only for an account this call created: a global profile is never changed by import) ------
			if (request.Profile != null && (result.CreatedAccount || await _profiles.GetProfileByUserIdAsync(user.Id, true) == null))
			{
				request.Profile.UserId = user.Id;
				// Department-issued identifiers never live on the global profile (they belong to member sensitive data).
				request.Profile.IdentificationNumber = null;
				request.Profile.HomeAddressId = null;
				request.Profile.MailingAddressId = null;
				await _profiles.SaveProfileAsync(request.DepartmentId, request.Profile, effects, cancellationToken);
			}

			// ---- 4. Membership ---------------------------------------------------------------------------------------
			result.Member = await _departments.AddMemberAsync(new DepartmentMember
			{
				DepartmentId = request.DepartmentId,
				UserId = user.Id,
				IsAdmin = request.IsAdmin,
				IsDisabled = request.IsDisabled,
				IsHidden = request.IsHidden,
				IsDefault = true,
				IsActive = true,
				MustChangePassword = request.MustChangePassword,
				ImportActivationState = request.ImportActivationState
			}, effects, cancellationToken);

			// ---- 5. Department-scoped sensitive data (ADP seam) ---------------------------------------------------------
			if (request.SensitiveData != null && HasSensitiveContent(request.SensitiveData))
			{
				var existing = await _sensitiveData.GetByDepartmentAndUserAsync(request.DepartmentId, user.Id);
				var data = existing ?? new DepartmentMemberSensitiveData { DepartmentId = request.DepartmentId, UserId = user.Id };
				data.IdentificationNumber = request.SensitiveData.IdentificationNumber ?? data.IdentificationNumber;
				data.Notes = request.SensitiveData.Notes ?? data.Notes;
				data.HomeAddress1 = request.SensitiveData.HomeAddress1 ?? data.HomeAddress1;
				data.HomeCity = request.SensitiveData.HomeCity ?? data.HomeCity;
				data.HomeState = request.SensitiveData.HomeState ?? data.HomeState;
				data.HomePostalCode = request.SensitiveData.HomePostalCode ?? data.HomePostalCode;
				data.HomeCountry = request.SensitiveData.HomeCountry ?? data.HomeCountry;
				// New rows have no legacy profile data to relocate; stamping it keeps the relocation sweep away.
				data.LegacyProfileRelocatedOn ??= DateTime.UtcNow;
				await _sensitiveData.SaveAsync(data, effects, cancellationToken);
			}

			// ---- 6. Group (one group per member per department) --------------------------------------------------------
			if (request.GroupId.HasValue && request.GroupId.Value > 0)
			{
				var current = await _groups.GetGroupMemberForUserAsync(user.Id, request.DepartmentId);
				if (current == null)
					await _groups.SaveGroupMemberAsync(new DepartmentGroupMember
					{
						DepartmentGroupId = request.GroupId.Value,
						DepartmentId = request.DepartmentId,
						UserId = user.Id,
						IsAdmin = request.GroupAdmin
					}, effects, cancellationToken);
			}

			// ---- 7. Roles (additive; enforcement may refuse a role the member is not certified for) -------------------
			var held = (await _roles.GetRolesForUserAsync(user.Id, request.DepartmentId))?.Select(r => r.PersonnelRoleId).ToHashSet() ?? new HashSet<int>();
			foreach (var roleId in request.RoleIds.Distinct().Where(r => !held.Contains(r)))
			{
				var role = await _roles.GetRoleByIdAsync(roleId);
				if (role == null || role.DepartmentId != request.DepartmentId)
					continue;

				role.Users = new List<PersonnelRoleUser> { new PersonnelRoleUser { PersonnelRoleId = roleId, DepartmentId = request.DepartmentId, UserId = user.Id } };
				try
				{
					await _roles.SaveRoleAsync(role, effects, cancellationToken, request.ActingUserId);
				}
				catch (RoleMembershipException ex) when (ex.Message == RoleMembershipException.RequirementsUnmet)
				{
					result.BlockedRoleIds.Add(roleId);
				}
			}

			// ---- 8. Custom field values ----------------------------------------------------------------------------------
			if (request.CustomFieldValues.Count > 0)
				await _customFields.SaveFieldValuesForEntityAsync(request.DepartmentId, (int)UdfEntityType.Personnel, user.Id, request.CustomFieldValues,
					request.ActingUserId, true, false, effects, cancellationToken);

			result.Succeeded = true;
			return result;
		}

		public async Task<string> GenerateUserNameAsync(string email)
		{
			var local = (email ?? string.Empty).Split('@')[0];
			var builder = new StringBuilder();
			foreach (var c in local)
				if (char.IsLetterOrDigit(c) && c < 128 || c == '.' || c == '_' || c == '-')
					builder.Append(char.ToLowerInvariant(c));

			var stem = builder.Length >= 3 ? builder.ToString() : "member" + builder;
			if (stem.Length > 40)
				stem = stem.Substring(0, 40);

			if (await _accounts.FindByNameAsync(stem) == null)
				return stem;

			for (var suffix = 2; suffix < 1000; suffix++)
			{
				var candidate = stem + suffix;
				if (await _accounts.FindByNameAsync(candidate) == null)
					return candidate;
			}

			return stem + Guid.NewGuid().ToString("N").Substring(0, 8);
		}

		private static bool HasSensitiveContent(DepartmentMemberSensitiveData data) =>
			!string.IsNullOrWhiteSpace(data.IdentificationNumber) || !string.IsNullOrWhiteSpace(data.Notes) || !string.IsNullOrWhiteSpace(data.HomeAddress1) ||
			!string.IsNullOrWhiteSpace(data.HomeCity) || !string.IsNullOrWhiteSpace(data.HomeState) || !string.IsNullOrWhiteSpace(data.HomePostalCode) ||
			!string.IsNullOrWhiteSpace(data.HomeCountry);
	}
}
