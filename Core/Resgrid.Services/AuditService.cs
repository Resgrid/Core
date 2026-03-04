using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class AuditService : IAuditService
	{
		private readonly IAuditLogsRepository _auditLogsRepository;
		private readonly IUserProfileService _userProfileService;

		public AuditService(IAuditLogsRepository auditLogsRepository, IUserProfileService userProfileService)
		{
			_auditLogsRepository = auditLogsRepository;
			_userProfileService = userProfileService;
		}

		public async Task<AuditLog> SaveAuditLogAsync(AuditLog auditLog, CancellationToken cancellationToken = default(CancellationToken))
		{
			auditLog.LoggedOn = DateTime.UtcNow;
			return await _auditLogsRepository.SaveOrUpdateAsync(auditLog, cancellationToken);
		}

		public async Task<AuditLog> GetAuditLogByIdAsync(int auditLogId)
		{
			return await _auditLogsRepository.GetByIdAsync(auditLogId);
		}

		public async Task<List<AuditLog>> GetAllAuditLogsForDepartmentAsync(int departmentId)
		{
			var logs = await _auditLogsRepository.GetAllByDepartmentIdAsync(departmentId);
			return logs.ToList();
		}

		public string GetAuditLogTypeString(AuditLogTypes logType)
		{
			switch (logType)
			{
				case AuditLogTypes.DepartmentSettingsChanged:
					return "Department Settings Changed";
				case AuditLogTypes.UserAdded:
					return "User Added";
				case AuditLogTypes.UserRemoved:
					return "User Removed";
				case AuditLogTypes.GroupAdded:
					return "Group Added";
				case AuditLogTypes.GroupRemoved:
					return "Group Removed";
				case AuditLogTypes.GroupChanged:
					return "Group Changed";
				case AuditLogTypes.UnitAdded:
					return "Unit Added";
				case AuditLogTypes.UnitRemoved:
					return "Unit Removed";
				case AuditLogTypes.UnitChanged:
					return "Unit Changed";
				case AuditLogTypes.ProfileUpdated:
					return "Profile Updated";
				case AuditLogTypes.PermissionsChanged:
					return "Permissions Changed";
				case AuditLogTypes.WorkflowAdded:
					return "Workflow Created";
				case AuditLogTypes.WorkflowEdited:
					return "Workflow Updated";
				case AuditLogTypes.WorkflowDeleted:
					return "Workflow Deleted";
				case AuditLogTypes.WorkflowStepAdded:
					return "Workflow Step Added";
				case AuditLogTypes.WorkflowStepEdited:
					return "Workflow Step Updated";
				case AuditLogTypes.WorkflowStepDeleted:
					return "Workflow Step Deleted";
				case AuditLogTypes.WorkflowCredentialAdded:
					return "Workflow Credential Added";
				case AuditLogTypes.WorkflowCredentialEdited:
					return "Workflow Credential Updated";
				case AuditLogTypes.WorkflowCredentialDeleted:
					return "Workflow Credential Deleted";
				case AuditLogTypes.ContactVerificationCodeSent:
					return "Contact Verification Code Sent";
				case AuditLogTypes.ContactVerificationConfirmed:
					return "Contact Verification Confirmed";
				case AuditLogTypes.ContactVerificationFailed:
					return "Contact Verification Failed";
				// Two-factor authentication
				case AuditLogTypes.TwoFactorEnabled:
					return "Two-Factor Enabled";
				case AuditLogTypes.TwoFactorDisabled:
					return "Two-Factor Disabled";
				case AuditLogTypes.TwoFactorLoginVerified:
					return "Two-Factor Login Verified";
				case AuditLogTypes.TwoFactorRecoveryCodeUsed:
					return "Two-Factor Recovery Code Used";
				case AuditLogTypes.TwoFactorStepUpVerified:
					return "Two-Factor Step-Up Verified";
				// SSO / SAML / OIDC
				case AuditLogTypes.SsoConfigCreated:
					return "SSO Config Created";
				case AuditLogTypes.SsoConfigUpdated:
					return "SSO Config Updated";
				case AuditLogTypes.SsoConfigDeleted:
					return "SSO Config Deleted";
				case AuditLogTypes.SsoLoginSucceeded:
					return "SSO Login Succeeded";
				case AuditLogTypes.SsoLoginFailed:
					return "SSO Login Failed";
				case AuditLogTypes.SsoUserProvisioned:
					return "SSO User Provisioned";
				// SCIM 2.0
				case AuditLogTypes.ScimUserCreated:
					return "SCIM User Created";
				case AuditLogTypes.ScimUserUpdated:
					return "SCIM User Updated";
				case AuditLogTypes.ScimUserDeactivated:
					return "SCIM User Deactivated";
				case AuditLogTypes.ScimUserDeleted:
					return "SCIM User Deleted";
				case AuditLogTypes.ScimAuthFailed:
					return "SCIM Auth Failed";
				case AuditLogTypes.ScimUserReactivated:
					return "SCIM User Reactivated";
				case AuditLogTypes.ScimGroupListed:
					return "SCIM Group Listed";
				case AuditLogTypes.ScimUserListed:
					return "SCIM User Listed";
				case AuditLogTypes.ScimUserRetrieved:
					return "SCIM User Retrieved";
				// SCIM bearer token lifecycle
				case AuditLogTypes.ScimBearerTokenProvisioned:
					return "SCIM Bearer Token Provisioned";
				case AuditLogTypes.ScimBearerTokenRotated:
					return "SCIM Bearer Token Rotated";
			}

			return $"Unknown ({logType})";
		}
	}
}
