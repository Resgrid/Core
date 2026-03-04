using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Helpers;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// SCIM 2.0 provisioning endpoint for automated user lifecycle management
	/// from external identity providers. Authenticate requests with the per-department
	/// SCIM bearer token configured in DepartmentSsoConfig.
	/// The bearer token must belong to the same department as the X-Resgrid-Department-Id header;
	/// mismatches are rejected and audited as <see cref="AuditLogTypes.ScimAuthFailed"/>.
	/// </summary>
	[Route("scim/v2")]
	[ApiExplorerSettings(GroupName = "v4")]
	[ApiController]
	[AllowAnonymous] // Auth is handled manually via SCIM bearer token
	public class ScimController : ControllerBase
	{
		private readonly IDepartmentSsoService _ssoService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IUserProfileService _userProfileService;
		private readonly ISystemAuditsService _systemAuditsService;
		private readonly UserManager<Model.Identity.IdentityUser> _userManager;

		public ScimController(
			IDepartmentSsoService ssoService,
			IDepartmentsService departmentsService,
			IUserProfileService userProfileService,
			ISystemAuditsService systemAuditsService,
			UserManager<Model.Identity.IdentityUser> userManager)
		{
			_ssoService = ssoService;
			_departmentsService = departmentsService;
			_userProfileService = userProfileService;
			_systemAuditsService = systemAuditsService;
			_userManager = userManager;
		}

		// -- GET /scim/v2/Users ------------------------------------------------

		/// <summary>Lists users for the department associated with the SCIM bearer token.</summary>
		[HttpGet("Users")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		public async Task<IActionResult> GetUsers(
			[FromHeader(Name = SsoConfig.ScimDepartmentIdHeader)] int departmentId,
			[FromQuery] int startIndex = 1,
			[FromQuery] int count = 100,
			CancellationToken cancellationToken = default)
		{
			if (!await AuthorizeScimRequestAsync(departmentId, null, cancellationToken))
				return ScimUnauthorized();

			var users = await _departmentsService.GetAllUsersForDepartment(departmentId, false, true);
			var scimUsers = new List<object>();

			foreach (var u in users ?? Enumerable.Empty<Model.Identity.IdentityUser>())
			{
				var profile = await _userProfileService.GetProfileByUserIdAsync(u.Id);
				scimUsers.Add(BuildScimUser(u, profile));
			}

			var paged = scimUsers.Skip(startIndex - 1).Take(count).ToList();

			await SaveScimAuditAsync(departmentId, null, AuditLogTypes.ScimUserListed,
				successful: true,
				data: $"startIndex={startIndex} count={count} totalResults={scimUsers.Count}");

			return Ok(new
			{
				schemas = new[] { "urn:ietf:params:scim:api:messages:2.0:ListResponse" },
				totalResults = scimUsers.Count,
				startIndex,
				itemsPerPage = paged.Count,
				Resources = paged
			});
		}

		// -- GET /scim/v2/Users/{id} -------------------------------------------

		/// <summary>Returns a single SCIM User resource by Resgrid user ID.</summary>
		[HttpGet("Users/{id}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		public async Task<IActionResult> GetUser(
			string id,
			[FromHeader(Name = SsoConfig.ScimDepartmentIdHeader)] int departmentId,
			CancellationToken cancellationToken = default)
		{
			if (!await AuthorizeScimRequestAsync(departmentId, id, cancellationToken))
				return ScimUnauthorized();

			var user = await _userManager.FindByIdAsync(id);
			if (user == null)
			{
				await SaveScimAuditAsync(departmentId, id, AuditLogTypes.ScimUserRetrieved,
					successful: false, data: $"User {id} not found");
				return ScimNotFound(id);
			}

			var profile = await _userProfileService.GetProfileByUserIdAsync(id);
			await SaveScimAuditAsync(departmentId, id, AuditLogTypes.ScimUserRetrieved,
				successful: true, data: $"userName={user.UserName}");

			return Ok(BuildScimUser(user, profile));
		}

		// -- POST /scim/v2/Users -----------------------------------------------

		/// <summary>Creates a new user via SCIM provisioning.</summary>
		[HttpPost("Users")]
		[ProducesResponseType(StatusCodes.Status201Created)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status409Conflict)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		public async Task<IActionResult> CreateUser(
			[FromHeader(Name = SsoConfig.ScimDepartmentIdHeader)] int departmentId,
			[FromBody] ScimUserResource resource,
			CancellationToken cancellationToken = default)
		{
			if (!await AuthorizeScimRequestAsync(departmentId, null, cancellationToken))
				return ScimUnauthorized();

			if (resource == null || string.IsNullOrWhiteSpace(resource.UserName))
			{
				await SaveScimAuditAsync(departmentId, null, AuditLogTypes.ScimUserCreated,
					successful: false, data: "Rejected: userName is required");
				return ScimBadRequest("userName is required.");
			}

			var email = resource.Emails?.FirstOrDefault()?.Value ?? resource.UserName;
			var existing = await _userManager.FindByEmailAsync(email);
			if (existing != null)
			{
				await SaveScimAuditAsync(departmentId, existing.Id, AuditLogTypes.ScimUserCreated,
					successful: false, data: $"Rejected: duplicate email={email}");
				return Conflict(ScimError("uniqueness", "A user with this email already exists."));
			}

			var newUser = new Model.Identity.IdentityUser
			{
				UserName = resource.UserName,
				Email = email,
				EmailConfirmed = true
			};

			var createResult = await _userManager.CreateAsync(newUser, Guid.NewGuid().ToString("N") + "Aa1!");
			if (!createResult.Succeeded)
			{
				var errors = string.Join("; ", createResult.Errors.Select(e => e.Description));
				await SaveScimAuditAsync(departmentId, null, AuditLogTypes.ScimUserCreated,
					successful: false, data: $"IdentityUser creation failed: {errors}");
				return ScimBadRequest(errors);
			}

			// Add to department
			await _departmentsService.AddUserToDepartmentAsync(departmentId, newUser.Id, false, cancellationToken);

			// Create profile
			var profile = new UserProfile
			{
				UserId = newUser.Id,
				FirstName = resource.Name?.GivenName ?? string.Empty,
				LastName = resource.Name?.FamilyName ?? string.Empty,
				MobileCarrier = 0
			};
			await _userProfileService.SaveProfileAsync(departmentId, profile, cancellationToken);

			await SaveScimAuditAsync(departmentId, newUser.Id, AuditLogTypes.ScimUserCreated,
				successful: true,
				data: $"userName={resource.UserName} email={email} externalId={resource.ExternalId}");

			return CreatedAtAction(nameof(GetUser), new { id = newUser.Id, departmentId }, BuildScimUser(newUser, profile));
		}

		// -- PUT /scim/v2/Users/{id} -------------------------------------------

		/// <summary>Replaces a user resource (full update).</summary>
		[HttpPut("Users/{id}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		public async Task<IActionResult> ReplaceUser(
			string id,
			[FromHeader(Name = SsoConfig.ScimDepartmentIdHeader)] int departmentId,
			[FromBody] ScimUserResource resource,
			CancellationToken cancellationToken = default)
		{
			if (!await AuthorizeScimRequestAsync(departmentId, id, cancellationToken))
				return ScimUnauthorized();

			var user = await _userManager.FindByIdAsync(id);
			if (user == null)
			{
				await SaveScimAuditAsync(departmentId, id, AuditLogTypes.ScimUserUpdated,
					successful: false, data: $"User {id} not found");
				return ScimNotFound(id);
			}

			// Update active/disabled state and emit a targeted audit entry for the state change
			if (resource.Active == false)
			{
				var member = await _departmentsService.GetDepartmentMemberAsync(id, departmentId);
				if (member != null)
				{
					member.IsDisabled = true;
					await _departmentsService.SaveDepartmentMemberAsync(member, cancellationToken);
					await SaveScimAuditAsync(departmentId, id, AuditLogTypes.ScimUserDeactivated,
						successful: true, data: "active=false via PUT");
				}
			}
			else if (resource.Active == true)
			{
				var member = await _departmentsService.GetDepartmentMemberAsync(id, departmentId);
				if (member is { IsDisabled: true })
				{
					member.IsDisabled = false;
					await _departmentsService.SaveDepartmentMemberAsync(member, cancellationToken);
					await SaveScimAuditAsync(departmentId, id, AuditLogTypes.ScimUserReactivated,
						successful: true, data: "active=true via PUT");
				}
			}

			var profile = await _userProfileService.GetProfileByUserIdAsync(id);
			if (profile == null) profile = new UserProfile { UserId = id };
			profile.FirstName = resource.Name?.GivenName ?? profile.FirstName;
			profile.LastName = resource.Name?.FamilyName ?? profile.LastName;
			await _userProfileService.SaveProfileAsync(departmentId, profile, cancellationToken);

			await SaveScimAuditAsync(departmentId, id, AuditLogTypes.ScimUserUpdated,
				successful: true,
				data: $"firstName={resource.Name?.GivenName} lastName={resource.Name?.FamilyName} active={resource.Active}");

			return Ok(BuildScimUser(user, profile));
		}

		// -- PATCH /scim/v2/Users/{id} -----------------------------------------

		/// <summary>Applies a partial update to a user (supports active/displayName operations).</summary>
		[HttpPatch("Users/{id}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		public async Task<IActionResult> PatchUser(
			string id,
			[FromHeader(Name = SsoConfig.ScimDepartmentIdHeader)] int departmentId,
			[FromBody] ScimPatchRequest patchRequest,
			CancellationToken cancellationToken = default)
		{
			if (!await AuthorizeScimRequestAsync(departmentId, id, cancellationToken))
				return ScimUnauthorized();

			var user = await _userManager.FindByIdAsync(id);
			if (user == null)
			{
				await SaveScimAuditAsync(departmentId, id, AuditLogTypes.ScimUserUpdated,
					successful: false, data: $"User {id} not found");
				return ScimNotFound(id);
			}

			var appliedOps = new List<string>();

			foreach (var op in patchRequest?.Operations ?? Enumerable.Empty<ScimPatchOperation>())
			{
				if (string.Equals(op.Path, "active", StringComparison.OrdinalIgnoreCase))
				{
					var active = op.Value?.ToString()?.Equals("true", StringComparison.OrdinalIgnoreCase) ?? true;
					var member = await _departmentsService.GetDepartmentMemberAsync(id, departmentId);
					if (member != null)
					{
						member.IsDisabled = !active;
						if (!active) member.IsDeleted = false; // deactivate without hard-delete
						await _departmentsService.SaveDepartmentMemberAsync(member, cancellationToken);

						var activeAuditType = active ? AuditLogTypes.ScimUserReactivated : AuditLogTypes.ScimUserDeactivated;
						await SaveScimAuditAsync(departmentId, id, activeAuditType,
							successful: true, data: $"active={active} op={op.Op}");

						appliedOps.Add($"active={active}");
					}
				}
			}

			var profile = await _userProfileService.GetProfileByUserIdAsync(id);
			await SaveScimAuditAsync(departmentId, id, AuditLogTypes.ScimUserUpdated,
				successful: true,
				data: $"PATCH ops applied: [{string.Join(", ", appliedOps)}]");

			return Ok(BuildScimUser(user, profile));
		}

		// -- DELETE /scim/v2/Users/{id} ----------------------------------------

		/// <summary>Soft-deletes (disables and marks deleted) a user via SCIM.</summary>
		[HttpDelete("Users/{id}")]
		[ProducesResponseType(StatusCodes.Status204NoContent)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		public async Task<IActionResult> DeleteUser(
			string id,
			[FromHeader(Name = SsoConfig.ScimDepartmentIdHeader)] int departmentId,
			CancellationToken cancellationToken = default)
		{
			if (!await AuthorizeScimRequestAsync(departmentId, id, cancellationToken))
				return ScimUnauthorized();

			var user = await _userManager.FindByIdAsync(id);
			if (user == null)
			{
				await SaveScimAuditAsync(departmentId, id, AuditLogTypes.ScimUserDeleted,
					successful: false, data: $"User {id} not found");
				return ScimNotFound(id);
			}

			var member = await _departmentsService.GetDepartmentMemberAsync(id, departmentId);
			if (member != null)
			{
				member.IsDisabled = true;
				member.IsDeleted = true;
				await _departmentsService.SaveDepartmentMemberAsync(member, cancellationToken);
			}

			await SaveScimAuditAsync(departmentId, id, AuditLogTypes.ScimUserDeactivated,
				successful: true, data: "IsDisabled=true IsDeleted=true via DELETE");
			await SaveScimAuditAsync(departmentId, id, AuditLogTypes.ScimUserDeleted,
				successful: true, data: $"userName={user.UserName}");

			return NoContent();
		}

		// -- GET /scim/v2/Groups -----------------------------------------------

		/// <summary>Returns department groups as SCIM Group resources.</summary>
		[HttpGet("Groups")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		public async Task<IActionResult> GetGroups(
			[FromHeader(Name = SsoConfig.ScimDepartmentIdHeader)] int departmentId,
			CancellationToken cancellationToken = default)
		{
			if (!await AuthorizeScimRequestAsync(departmentId, null, cancellationToken))
				return ScimUnauthorized();

			await SaveScimAuditAsync(departmentId, null, AuditLogTypes.ScimGroupListed, successful: true);

			// Groups map to DepartmentGroups — return empty list with proper schema for now
			return Ok(new
			{
				schemas = new[] { "urn:ietf:params:scim:api:messages:2.0:ListResponse" },
				totalResults = 0,
				startIndex = 1,
				itemsPerPage = 0,
				Resources = Array.Empty<object>()
			});
		}

		// -- Private helpers ---------------------------------------------------

		/// <summary>
		/// Validates the SCIM bearer token and confirms it is bound to the department
		/// supplied in the request header. Any mismatch is rejected and recorded as
		/// <see cref="AuditLogTypes.ScimAuthFailed"/> before returning false.
		/// </summary>
		private async Task<bool> AuthorizeScimRequestAsync(int departmentId, string targetUserId, CancellationToken cancellationToken)
		{
			var authHeader = Request.Headers["Authorization"].FirstOrDefault();

			// Missing or malformed Authorization header
			if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
			{
				await SaveScimAuditAsync(departmentId, targetUserId, AuditLogTypes.ScimAuthFailed,
					successful: false, data: "Missing or malformed Authorization header");
				return false;
			}

			var token = authHeader.Substring("Bearer ".Length).Trim();

			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId);
			if (department == null)
			{
				// Do not reveal whether the department exists to the caller.
				await SaveScimAuditAsync(departmentId, targetUserId, AuditLogTypes.ScimAuthFailed,
					successful: false, data: $"Department {departmentId} not found");
				return false;
			}

			// Validate the token AND confirm it belongs to the claimed department.
			// Returns null on any failure (invalid token, wrong department, decryption error).
			var tokenDepartmentId = await _ssoService.ValidateScimBearerTokenAndGetDepartmentAsync(
				token, departmentId, department.Code, cancellationToken);

			if (tokenDepartmentId == null)
			{
				await SaveScimAuditAsync(departmentId, targetUserId, AuditLogTypes.ScimAuthFailed,
					successful: false, data: "Bearer token invalid or not configured for this department");
				return false;
			}

			// Explicit defence-in-depth check: the token's owning department must equal the header value.
			if (tokenDepartmentId.Value != departmentId)
			{
				await SaveScimAuditAsync(departmentId, targetUserId, AuditLogTypes.ScimAuthFailed,
					successful: false,
					data: $"Token department mismatch: token belongs to dept={tokenDepartmentId.Value}, header claims dept={departmentId}");
				return false;
			}

			return true;
		}

		private static object BuildScimUser(Model.Identity.IdentityUser user, UserProfile profile)
		{
			return new
			{
				schemas = new[] { "urn:ietf:params:scim:schemas:core:2.0:User" },
				id = user.Id,
				userName = user.UserName,
				active = true,
				name = new
				{
					formatted = $"{profile?.FirstName} {profile?.LastName}".Trim(),
					givenName = profile?.FirstName ?? string.Empty,
					familyName = profile?.LastName ?? string.Empty
				},
				emails = new[]
				{
					new { value = user.Email, primary = true, type = "work" }
				},
				meta = new
				{
					resourceType = "User",
					location = $"/scim/v2/Users/{user.Id}"
				}
			};
		}

		/// <summary>
		/// Persists a SCIM system audit record. The <paramref name="data"/> field carries
		/// structured contextual information about the operation to support forensic review.
		/// </summary>
		private async Task SaveScimAuditAsync(
			int departmentId,
			string userId,
			AuditLogTypes auditType,
			bool successful = true,
			string data = null)
		{
			var audit = new SystemAudit
			{
				System = (int)SystemAuditSystems.Api,
				Type = (int)SystemAuditTypes.ScimOperation,
				DepartmentId = departmentId,
				UserId = userId,
				Successful = successful,
				IpAddress = IpAddressHelper.GetRequestIP(Request, true),
				ServerName = Environment.MachineName,
				Data = string.IsNullOrWhiteSpace(data)
					? $"SCIM {auditType} dept={departmentId}"
					: $"SCIM {auditType} dept={departmentId} | {data}"
			};
			await _systemAuditsService.SaveSystemAuditAsync(audit);
		}

		private IActionResult ScimUnauthorized() =>
			Unauthorized(new
			{
				schemas = new[] { "urn:ietf:params:scim:api:messages:2.0:Error" },
				status = "401",
				detail = "Bearer token is missing or invalid."
			});

		private IActionResult ScimNotFound(string id) =>
			NotFound(new
			{
				schemas = new[] { "urn:ietf:params:scim:api:messages:2.0:Error" },
				status = "404",
				detail = $"User {id} not found."
			});

		private IActionResult ScimBadRequest(string detail) =>
			BadRequest(new
			{
				schemas = new[] { "urn:ietf:params:scim:api:messages:2.0:Error" },
				status = "400",
				detail
			});

		private static object ScimError(string scimType, string detail) =>
			new
			{
				schemas = new[] { "urn:ietf:params:scim:api:messages:2.0:Error" },
				scimType,
				status = "409",
				detail
			};
	}

	// -- SCIM resource models --------------------------------------------------

	/// <summary>Minimal SCIM 2.0 User resource for inbound provisioning requests.</summary>
	public sealed class ScimUserResource
	{
		[JsonProperty("schemas")]
		public List<string> Schemas { get; set; }

		[JsonProperty("userName")]
		public string UserName { get; set; }

		[JsonProperty("name")]
		public ScimName Name { get; set; }

		[JsonProperty("emails")]
		public List<ScimEmail> Emails { get; set; }

		[JsonProperty("active")]
		public bool? Active { get; set; }

		[JsonProperty("externalId")]
		public string ExternalId { get; set; }
	}

	public sealed class ScimName
	{
		[JsonProperty("givenName")]
		public string GivenName { get; set; }

		[JsonProperty("familyName")]
		public string FamilyName { get; set; }

		[JsonProperty("formatted")]
		public string Formatted { get; set; }
	}

	public sealed class ScimEmail
	{
		[JsonProperty("value")]
		public string Value { get; set; }

		[JsonProperty("primary")]
		public bool Primary { get; set; }

		[JsonProperty("type")]
		public string Type { get; set; }
	}

	public sealed class ScimPatchRequest
	{
		[JsonProperty("schemas")]
		public List<string> Schemas { get; set; }

		[JsonProperty("Operations")]
		public List<ScimPatchOperation> Operations { get; set; }
	}

	public sealed class ScimPatchOperation
	{
		[JsonProperty("op")]
		public string Op { get; set; }

		[JsonProperty("path")]
		public string Path { get; set; }

		[JsonProperty("value")]
		public object Value { get; set; }
	}
}

