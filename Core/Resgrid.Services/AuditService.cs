using System;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Checklists;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class AuditService : IAuditService
	{
		private readonly IAuditLogsRepository _auditLogsRepository;
		private readonly IUserProfileService _userProfileService;
		private readonly Lazy<IReadinessHistoryProtectionService> _history;

		public AuditService(IAuditLogsRepository auditLogsRepository, IUserProfileService userProfileService, Lazy<IReadinessHistoryProtectionService> history = null)
		{
			_auditLogsRepository = auditLogsRepository;
			_userProfileService = userProfileService;
			_history = history;
		}

		public async Task<AuditLog> SaveAuditLogAsync(AuditLog auditLog, CancellationToken cancellationToken = default(CancellationToken))
		{
			auditLog.LoggedOn = DateTime.UtcNow;
			return await _auditLogsRepository.SaveOrUpdateAsync(auditLog, cancellationToken);
		}

		public async Task<AuditLog> GetAuditLogByIdAsync(int auditLogId)
		{
			return await DisplayAsync(await _auditLogsRepository.GetByIdAsync(auditLogId));
		}

		public async Task<List<AuditLog>> GetAllAuditLogsForDepartmentAsync(int departmentId)
		{
			var logs = await _auditLogsRepository.GetAllByDepartmentIdAsync(departmentId);
			var display = new List<AuditLog>();
			foreach (var log in logs)
				display.Add(await DisplayAsync(log));
			return display;
		}

		public async Task<List<AuditLog>> GetAuditLogsForDepartmentPagedAsync(int departmentId, DateTime startDate, DateTime endDate, AuditLogTypes? logType, int page, int pageSize)
		{
			// Clamp paging so the repository never builds a negative OFFSET or an invalid LIMIT/FETCH.
			var safePage = page < 1 ? 1 : page;
			var safePageSize = pageSize < 1 ? 1 : (pageSize > 1000 ? 1000 : pageSize);

			// Normalize the time window so callers that leave a bound unset (DateTime.MinValue) don't
			// crash or silently return nothing. Floor the inclusive start at the SQL datetime minimum,
			// and default the exclusive end to "now" when it is unset or precedes the start.
			var safeStart = startDate < (DateTime)SqlDateTime.MinValue ? (DateTime)SqlDateTime.MinValue : startDate;
			var safeEnd = (endDate == default(DateTime) || endDate < safeStart) ? DateTime.UtcNow : endDate;

			var logs = await _auditLogsRepository.GetAuditLogsForDepartmentPagedAsync(departmentId, safeStart, safeEnd, (int?)logType, safePage, safePageSize);
			var display = new List<AuditLog>();
			foreach (var log in logs)
				display.Add(await DisplayAsync(log));
			return display;
		}

		private Task<AuditLog> DisplayAsync(AuditLog log)
		{
			if (log == null || !ReadinessHistoryFields.IsChecklistAudit(log.LogType)) return Task.FromResult(log);
			var history = _history?.Value ?? throw new InvalidOperationException("Readiness history protection is unavailable.");
			return history.ForDisplayAsync(log.DepartmentId, log, ReadinessHistoryFields.Audits);
		}

		public string GetAuditLogTypeString(AuditLogTypes logType)
		{
			switch (logType)
			{
				case AuditLogTypes.DepartmentConfigurationChanged:
					return "Department Configuration Changed";
				case AuditLogTypes.DepartmentSecurityPolicyChanged:
					return "Security Policy Changed";
				case AuditLogTypes.AdminAssistReviewChanged:
					return "Admin Assist Review Changed";
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
				case AuditLogTypes.PasswordResetByAdministrator:
					return "Password Reset by Administrator";
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
				// User Defined Fields
				case AuditLogTypes.UdfDefinitionCreated:
					return "UDF Definition Created";
				case AuditLogTypes.UdfDefinitionUpdated:
					return "UDF Definition Updated";
				case AuditLogTypes.UdfDefinitionDeleted:
					return "UDF Definition Deleted";
				case AuditLogTypes.UdfFieldAdded:
					return "UDF Field Added";
				case AuditLogTypes.UdfFieldUpdated:
					return "UDF Field Updated";
				case AuditLogTypes.UdfFieldRemoved:
					return "UDF Field Removed";
				case AuditLogTypes.UdfFieldValueSaved:
					return "UDF Field Values Saved";
				// Calendar Check-In Attendance
				case AuditLogTypes.CalendarCheckInPerformed:
					return "Calendar Event Check-In";
				case AuditLogTypes.CalendarCheckOutPerformed:
					return "Calendar Event Check-Out";
				case AuditLogTypes.CalendarCheckInUpdated:
					return "Calendar Check-In Times Updated";
				case AuditLogTypes.CalendarCheckInDeleted:
					return "Calendar Check-In Deleted";
				case AuditLogTypes.CalendarAdminCheckInPerformed:
					return "Admin Calendar Check-In";
				case AuditLogTypes.UnitTrackingDeviceCreated:
					return "Unit Tracking Device Created";
				case AuditLogTypes.UnitTrackingDeviceUpdated:
					return "Unit Tracking Device Updated";
				case AuditLogTypes.UnitTrackingDeviceDisabled:
					return "Unit Tracking Device Disabled";
				case AuditLogTypes.UnitTrackingDeviceDeleted:
					return "Unit Tracking Device Deleted";
				case AuditLogTypes.UnitTrackingCredentialCreated:
					return "Unit Tracking Credential Created";
				case AuditLogTypes.UnitTrackingCredentialRotated:
					return "Unit Tracking Credential Rotated";
			case AuditLogTypes.UnitTrackingCredentialRevoked:
				return "Unit Tracking Credential Revoked";
			case AuditLogTypes.DeleteDepartmentRequested:
				return "Department Deletion Requested";
			case AuditLogTypes.DeleteDepartmentRequestedCancelled:
				return "Department Deletion Request Cancelled";
			case AuditLogTypes.DeleteDepartmentRequestExecuted:
				return "Department Deletion Executed";
			case AuditLogTypes.ChatMessageDeletedByModerator:
				return "Chat Message Deleted by Moderator";
			case AuditLogTypes.ChatUserMuted:
				return "Chat User Muted";
			case AuditLogTypes.ChatUserUnmuted:
				return "Chat User Unmuted";
			case AuditLogTypes.ChatUserBanned:
				return "Chat User Banned";
			case AuditLogTypes.ChatUserUnbanned:
				return "Chat User Unbanned";
			case AuditLogTypes.ChatChannelLocked:
				return "Chat Channel Locked";
			case AuditLogTypes.ChatChannelUnlocked:
				return "Chat Channel Unlocked";
			case AuditLogTypes.ChatChannelArchived:
				return "Chat Channel Archived";
			case AuditLogTypes.ChatFlagResolved:
				return "Chat Flag Resolved";
			case AuditLogTypes.ChatSettingsChanged:
				return "Chat Settings Changed";
			case AuditLogTypes.ChatExportRequested:
				return "Chat Export Requested";
			case AuditLogTypes.ChatExportDownloaded:
				return "Chat Export Downloaded";
			case AuditLogTypes.ModerationReportSubmitted:
				return "Moderation Report Submitted";
			case AuditLogTypes.ModerationRequestReopened:
				return "Moderation Request Reopened";
			case AuditLogTypes.ModerationRequestCompleted:
				return "Moderation Request Completed";
			case AuditLogTypes.ModerationEvidenceDownloaded:
				return "Moderation Evidence Downloaded";
			case AuditLogTypes.UserReactivated:
				return "User Reactivated";
			// These types used to fall through to "Unknown (...)". The same English names are the
			// AuditLogType* entries in Security.en.resx; AuditServiceTypeNameTests keeps the two in step.
			case AuditLogTypes.SubscriptionUpdated:
				return "Subscription Updated";
			case AuditLogTypes.SubscriptionCreated:
				return "Subscription Created";
			case AuditLogTypes.SubscriptionCancelled:
				return "Subscription Cancelled";
			case AuditLogTypes.SubscriptionBillingInfoUpdated:
				return "Subscription Billing Info Updated";
			case AuditLogTypes.CallReactivated:
				return "Call Reactivated";
			case AuditLogTypes.UserAccountDeleted:
				return "User Account Deleted";
			case AuditLogTypes.AddonSubscriptionModified:
				return "Add-on Subscription Modified";
			case AuditLogTypes.DeleteStaticShift:
				return "Static Shift Deleted";
			case AuditLogTypes.UpdateStaticShift:
				return "Static Shift Updated";
			case AuditLogTypes.CustomStatusAdded:
				return "Custom Status Added";
			case AuditLogTypes.CustomStatusRemoved:
				return "Custom Status Removed";
			case AuditLogTypes.CustomStatusUpdated:
				return "Custom Status Updated";
			case AuditLogTypes.CustomStatusDetailUpdated:
				return "Custom Status Detail Updated";
			case AuditLogTypes.CallTypeAdded:
				return "Call Type Added";
			case AuditLogTypes.CallTypeEdited:
				return "Call Type Edited";
			case AuditLogTypes.CallTypeRemoved:
				return "Call Type Removed";
			case AuditLogTypes.CallPriorityAdded:
				return "Call Priority Added";
			case AuditLogTypes.CallPriorityEdited:
				return "Call Priority Edited";
			case AuditLogTypes.CallPriorityRemoved:
				return "Call Priority Removed";
			case AuditLogTypes.UnitTypeAdded:
				return "Unit Type Added";
			case AuditLogTypes.UnitTypeEdited:
				return "Unit Type Edited";
			case AuditLogTypes.UnitTypeRemoved:
				return "Unit Type Removed";
			case AuditLogTypes.CertificationTypeAdded:
				return "Certification Type Added";
			case AuditLogTypes.CertificationTypeEdited:
				return "Certification Type Edited";
			case AuditLogTypes.CertificationTypeRemoved:
				return "Certification Type Removed";
			case AuditLogTypes.DocumentCategoryAdded:
				return "Document Category Added";
			case AuditLogTypes.DocumentCategoryEdited:
				return "Document Category Edited";
			case AuditLogTypes.DocumentCategoryRemoved:
				return "Document Category Removed";
			case AuditLogTypes.DocumentAdded:
				return "Document Added";
			case AuditLogTypes.DocumentEdited:
				return "Document Edited";
			case AuditLogTypes.DocumentRemoved:
				return "Document Removed";
			case AuditLogTypes.NoteCategoryAdded:
				return "Note Category Added";
			case AuditLogTypes.NoteCategoryEdited:
				return "Note Category Edited";
			case AuditLogTypes.NoteCategoryRemoved:
				return "Note Category Removed";
			case AuditLogTypes.NoteAdded:
				return "Note Added";
			case AuditLogTypes.NoteEdited:
				return "Note Edited";
			case AuditLogTypes.NoteRemoved:
				return "Note Removed";
			case AuditLogTypes.ContactAdded:
				return "Contact Added";
			case AuditLogTypes.ContactEdited:
				return "Contact Edited";
			case AuditLogTypes.ContactRemoved:
				return "Contact Removed";
			case AuditLogTypes.ContactCategoryAdded:
				return "Contact Category Added";
			case AuditLogTypes.ContactCategoryEdited:
				return "Contact Category Edited";
			case AuditLogTypes.ContactCategoryRemoved:
				return "Contact Category Removed";
			case AuditLogTypes.ContactNoteTypeAdded:
				return "Contact Note Type Added";
			case AuditLogTypes.ContactNoteTypeEdited:
				return "Contact Note Type Edited";
			case AuditLogTypes.ContactNoteTypeRemoved:
				return "Contact Note Type Removed";
			case AuditLogTypes.RouteCreated:
				return "Route Created";
			case AuditLogTypes.RouteUpdated:
				return "Route Updated";
			case AuditLogTypes.RouteDeleted:
				return "Route Deleted";
			case AuditLogTypes.RouteStarted:
				return "Route Started";
			case AuditLogTypes.RouteCompleted:
				return "Route Completed";
			case AuditLogTypes.RouteCancelled:
				return "Route Cancelled";
			case AuditLogTypes.RoutePaused:
				return "Route Paused";
			case AuditLogTypes.RouteResumed:
				return "Route Resumed";
			case AuditLogTypes.RouteStopCheckedIn:
				return "Route Stop Checked In";
			case AuditLogTypes.RouteStopCheckedOut:
				return "Route Stop Checked Out";
			case AuditLogTypes.RouteStopSkipped:
				return "Route Stop Skipped";
			case AuditLogTypes.RouteDeviationDetected:
				return "Route Deviation Detected";
			case AuditLogTypes.RouteDeviationAcknowledged:
				return "Route Deviation Acknowledged";
			case AuditLogTypes.CheckInTimerConfigCreated:
				return "Check-In Timer Configuration Created";
			case AuditLogTypes.CheckInTimerConfigUpdated:
				return "Check-In Timer Configuration Updated";
			case AuditLogTypes.CheckInTimerConfigDeleted:
				return "Check-In Timer Configuration Deleted";
			case AuditLogTypes.CheckInTimerOverrideCreated:
				return "Check-In Timer Override Created";
			case AuditLogTypes.CheckInTimerOverrideUpdated:
				return "Check-In Timer Override Updated";
			case AuditLogTypes.CheckInTimerOverrideDeleted:
				return "Check-In Timer Override Deleted";
			case AuditLogTypes.CheckInPerformed:
				return "Check-In Performed";
			case AuditLogTypes.CheckInTimerEnabledOnCall:
				return "Check-In Timer Enabled on Call";
			case AuditLogTypes.CheckInTimerDisabledOnCall:
				return "Check-In Timer Disabled on Call";
			case AuditLogTypes.LogCreated:
				return "Log Created";
			case AuditLogTypes.LogDeleted:
				return "Log Deleted";
			case AuditLogTypes.CommunicationTestCreated:
				return "Communication Test Created";
			case AuditLogTypes.CommunicationTestUpdated:
				return "Communication Test Updated";
			case AuditLogTypes.CommunicationTestDeleted:
				return "Communication Test Deleted";
			case AuditLogTypes.CommunicationTestRunStarted:
				return "Communication Test Run Started";
			case AuditLogTypes.WeatherAlertSourceCreated:
				return "Weather Alert Source Created";
			case AuditLogTypes.WeatherAlertSourceUpdated:
				return "Weather Alert Source Updated";
			case AuditLogTypes.WeatherAlertSourceDeleted:
				return "Weather Alert Source Deleted";
			case AuditLogTypes.WeatherAlertSourceEnabled:
				return "Weather Alert Source Enabled";
			case AuditLogTypes.WeatherAlertSourceDisabled:
				return "Weather Alert Source Disabled";
			case AuditLogTypes.WeatherAlertZoneCreated:
				return "Weather Alert Zone Created";
			case AuditLogTypes.WeatherAlertZoneUpdated:
				return "Weather Alert Zone Updated";
			case AuditLogTypes.WeatherAlertZoneDeleted:
				return "Weather Alert Zone Deleted";
			case AuditLogTypes.WeatherAlertZoneEnabled:
				return "Weather Alert Zone Enabled";
			case AuditLogTypes.WeatherAlertZoneDisabled:
				return "Weather Alert Zone Disabled";
			case AuditLogTypes.WeatherAlertSettingsChanged:
				return "Weather Alert Settings Changed";
			case AuditLogTypes.FeatureFlagChanged:
				return "Feature Flag Changed";
			case AuditLogTypes.FeatureFlagOverrideChanged:
				return "Feature Flag Override Changed";
			case AuditLogTypes.UserAuthenticationSessionsRevoked:
				return "User Sign-In Sessions Revoked";
			case AuditLogTypes.DataProtectionStepUpExemptionsChanged:
				return "Data Protection Step-Up Exemptions Changed";
			case AuditLogTypes.ContactPreplanAdded:
				return "Contact Pre-Plan Added";
			case AuditLogTypes.ContactPreplanUpdated:
				return "Contact Pre-Plan Updated";
			case AuditLogTypes.ContactPreplanRemoved:
				return "Contact Pre-Plan Removed";
			case AuditLogTypes.ContactAttachmentAdded:
				return "Contact Attachment Added";
			case AuditLogTypes.ContactAttachmentRemoved:
				return "Contact Attachment Removed";
			case AuditLogTypes.ChecklistDefinitionAdded:
				return "Checklist Definition Added";
			case AuditLogTypes.ChecklistDefinitionUpdated:
				return "Checklist Definition Updated";
			case AuditLogTypes.ChecklistDefinitionPublished:
				return "Checklist Definition Published";
			case AuditLogTypes.ChecklistDefinitionRetired:
				return "Checklist Definition Retired";
			case AuditLogTypes.ChecklistDefinitionRemoved:
				return "Checklist Definition Removed";
			case AuditLogTypes.ChecklistCompletionStarted:
				return "Checklist Completion Started";
			case AuditLogTypes.ChecklistProgressSaved:
				return "Checklist Progress Saved";
			case AuditLogTypes.ChecklistCompletionSubmitted:
				return "Checklist Completion Submitted";
			case AuditLogTypes.ChecklistWitnessAttested:
				return "Checklist Witness Attested";
			case AuditLogTypes.ChecklistFileAdded:
				return "Checklist File Added";
			case AuditLogTypes.ChecklistFileRemoved:
				return "Checklist File Removed";
			case AuditLogTypes.ChecklistScheduleAdded:
				return "Checklist Schedule Added";
			case AuditLogTypes.ChecklistScheduleUpdated:
				return "Checklist Schedule Updated";
			case AuditLogTypes.ChecklistOccurrenceMissed:
				return "Checklist Occurrence Missed";
			case AuditLogTypes.ChecklistOccurrenceSkipped:
				return "Checklist Occurrence Skipped";
			case AuditLogTypes.ChecklistReminderSettingsUpdated:
				return "Checklist Reminder Settings Updated";
			case AuditLogTypes.WorkOrderChanged:
				return "Work Order Changed";
			case AuditLogTypes.InventoryChanged:
				return "Inventory Changed";
			case AuditLogTypes.BillingProfileChanged:
				return "Billing Profile Changed";
			case AuditLogTypes.RateCardChanged:
				return "Rate Card Changed";
			case AuditLogTypes.InvoiceCreated:
				return "Invoice Created";
			case AuditLogTypes.InvoiceUpdated:
				return "Invoice Updated";
			case AuditLogTypes.InvoiceSent:
				return "Invoice Sent";
			case AuditLogTypes.InvoiceVoided:
				return "Invoice Voided";
			case AuditLogTypes.InvoicePaymentRecorded:
				return "Invoice Payment Recorded";
			case AuditLogTypes.DepartmentBillingIdentityChanged:
				return "Department Billing Identity Changed";
			case AuditLogTypes.PaymentConnectionConnected:
				return "Payment Connection Connected";
			case AuditLogTypes.PaymentConnectionDisconnected:
				return "Payment Connection Disconnected";
			case AuditLogTypes.PaymentConnectionRevoked:
				return "Payment Connection Revoked";
			case AuditLogTypes.PaymentConnectionActionRequired:
				return "Payment Connection Action Required";
			case AuditLogTypes.InvoicePaymentRequestCreated:
				return "Invoice Payment Request Created";
			case AuditLogTypes.InvoicePaymentRefunded:
				return "Invoice Payment Refunded";
			case AuditLogTypes.InvoicePaymentDisputed:
				return "Invoice Payment Disputed";
			case AuditLogTypes.PaymentWebhookRejected:
				return "Payment Webhook Rejected";
			case AuditLogTypes.InvoicePaymentRequestFailed:
				return "Invoice Payment Request Failed";
			case AuditLogTypes.InvoicePaymentRequestExpired:
				return "Invoice Payment Request Expired";
			case AuditLogTypes.CertificationAdded:
				return "Certification Added";
			case AuditLogTypes.CertificationUpdated:
				return "Certification Updated";
			case AuditLogTypes.CertificationRemoved:
				return "Certification Removed";
			case AuditLogTypes.CertificationStatusChanged:
				return "Certification Status Changed";
			case AuditLogTypes.CertificationVerified:
				return "Certification Verified";
			case AuditLogTypes.CertificationCreditAdded:
				return "Certification Credit Added";
			case AuditLogTypes.CertificationCreditRemoved:
				return "Certification Credit Removed";
			case AuditLogTypes.RoleCertificationRequirementChanged:
				return "Role Certification Requirement Changed";
			case AuditLogTypes.DepartmentCertificationSettingsChanged:
				return "Department Certification Settings Changed";
			case AuditLogTypes.RoleMemberAdded:
				return "Role Member Added";
			case AuditLogTypes.RoleMemberRemoved:
				return "Role Member Removed";
			case AuditLogTypes.RoleMemberRemovedByCertification:
				return "Role Member Removed by Certification";
			case AuditLogTypes.UnitCertificationAdded:
				return "Unit Certification Added";
			case AuditLogTypes.UnitCertificationUpdated:
				return "Unit Certification Updated";
			case AuditLogTypes.UnitCertificationRemoved:
				return "Unit Certification Removed";
			case AuditLogTypes.UnitCertificationStatusChanged:
				return "Unit Certification Status Changed";
			case AuditLogTypes.DeploymentCreated:
				return "Deployment Created";
			case AuditLogTypes.DeploymentUpdated:
				return "Deployment Updated";
			case AuditLogTypes.DeploymentStatusChanged:
				return "Deployment Status Changed";
			case AuditLogTypes.DeploymentRosterChanged:
				return "Deployment Roster Changed";
			case AuditLogTypes.DeploymentEquipmentChanged:
				return "Deployment Equipment Changed";
			case AuditLogTypes.DeploymentAttachmentAdded:
				return "Deployment Attachment Added";
			case AuditLogTypes.DeploymentAttachmentRemoved:
				return "Deployment Attachment Removed";
			case AuditLogTypes.TimeReportCreated:
				return "Time Report Created";
			case AuditLogTypes.TimeReportUpdated:
				return "Time Report Updated";
			case AuditLogTypes.TimeReportSubmitted:
				return "Time Report Submitted";
			case AuditLogTypes.TimeReportApproved:
				return "Time Report Approved";
			case AuditLogTypes.TimeReportVoided:
				return "Time Report Voided";
			case AuditLogTypes.DeploymentExpenseAdded:
				return "Deployment Expense Added";
			case AuditLogTypes.DeploymentExpenseUpdated:
				return "Deployment Expense Updated";
			case AuditLogTypes.DeploymentExpenseRemoved:
				return "Deployment Expense Removed";
			case AuditLogTypes.RateScheduleCreated:
				return "Rate Schedule Created";
			case AuditLogTypes.RateScheduleUpdated:
				return "Rate Schedule Updated";
			case AuditLogTypes.RateScheduleDeleted:
				return "Rate Schedule Deleted";
			case AuditLogTypes.RateScheduleEntryChanged:
				return "Rate Schedule Entry Changed";
			case AuditLogTypes.RatePremiumChanged:
				return "Rate Premium Changed";
			case AuditLogTypes.ServiceContractCreated:
				return "Service Contract Created";
			case AuditLogTypes.ServiceContractUpdated:
				return "Service Contract Updated";
			case AuditLogTypes.ServiceContractStatusChanged:
				return "Service Contract Status Changed";
			case AuditLogTypes.ServiceContractDeleted:
				return "Service Contract Deleted";
			case AuditLogTypes.ComplianceDocumentAdded:
				return "Compliance Document Added";
			case AuditLogTypes.ComplianceDocumentUpdated:
				return "Compliance Document Updated";
			case AuditLogTypes.ComplianceDocumentRemoved:
				return "Compliance Document Removed";
			case AuditLogTypes.BidCreated:
				return "Bid Created";
			case AuditLogTypes.BidUpdated:
				return "Bid Updated";
			case AuditLogTypes.BidSent:
				return "Bid Sent";
			case AuditLogTypes.BidAccepted:
				return "Bid Accepted";
			case AuditLogTypes.BidDeclined:
				return "Bid Declined";
			case AuditLogTypes.BidWithdrawn:
				return "Bid Withdrawn";
			case AuditLogTypes.BidExpired:
				return "Bid Expired";
			case AuditLogTypes.BidConverted:
				return "Bid Converted";
			case AuditLogTypes.BidDeleted:
				return "Bid Deleted";
			case AuditLogTypes.TimeReportBilled:
				return "Time Report Billed";
			case AuditLogTypes.DeploymentInvoiceGenerated:
				return "Deployment Invoice Generated";
			case AuditLogTypes.CalOesMarsAgencyProfileChanged:
				return "Cal OES MARS Agency Profile Changed";
			case AuditLogTypes.CalOesMarsResourceProfileChanged:
				return "Cal OES MARS Resource Profile Changed";
			case AuditLogTypes.CalOesMarsRateProfileChanged:
				return "Cal OES MARS Rate Profile Changed";
			case AuditLogTypes.CalOesMarsRateDraftBuilt:
				return "Cal OES MARS Rate Draft Built";
			case AuditLogTypes.CalOesMarsRateReviewed:
				return "Cal OES MARS Rate Reviewed";
			case AuditLogTypes.CalOesMarsAgreementChanged:
				return "Cal OES MARS Agreement Changed";
			case AuditLogTypes.CalOesMarsAgreementObserved:
				return "Cal OES MARS Agreement Observed";
			case AuditLogTypes.CalOesMarsWorkItemPrepared:
				return "Cal OES MARS Work Item Prepared";
			case AuditLogTypes.CalOesMarsWorkItemValidated:
				return "Cal OES MARS Work Item Validated";
			case AuditLogTypes.CalOesMarsReimbursementCalculated:
				return "Cal OES MARS Reimbursement Calculated";
			case AuditLogTypes.CalOesMarsWorkItemOpenedForHandoff:
				return "Cal OES MARS Work Item Opened for Handoff";
			case AuditLogTypes.CalOesMarsExternalStatusObserved:
				return "Cal OES MARS External Status Observed";
			case AuditLogTypes.CalOesMarsInvoiceApproved:
				return "Cal OES MARS Invoice Approved";
			case AuditLogTypes.CalOesMarsInvoiceRejected:
				return "Cal OES MARS Invoice Rejected";
			case AuditLogTypes.CalOesMarsPaymentReconciled:
				return "Cal OES MARS Payment Reconciled";
			case AuditLogTypes.CalOesMarsWorkItemDeleted:
				return "Cal OES MARS Work Item Deleted";
			case AuditLogTypes.WorkforceEmployerProfileChanged:
				return "Workforce Employer Profile Changed";
			case AuditLogTypes.WorkforceEstablishmentChanged:
				return "Workforce Establishment Changed";
			case AuditLogTypes.WorkforceEmploymentChanged:
				return "Workforce Employment Changed";
			case AuditLogTypes.WorkforceCompensationChanged:
				return "Workforce Compensation Changed";
			case AuditLogTypes.WorkforceAnnualPayFactImported:
				return "Workforce Annual Pay Data Imported";
			case AuditLogTypes.PayDataDemographicChanged:
				return "Pay Data Demographics Changed";
			case AuditLogTypes.PayDataReportCreated:
				return "Pay Data Report Created";
			case AuditLogTypes.PayDataReportValidated:
				return "Pay Data Report Validated";
			case AuditLogTypes.PayDataReportFrozen:
				return "Pay Data Report Frozen";
			case AuditLogTypes.PayDataReportExported:
				return "Pay Data Report Exported";
			case AuditLogTypes.PayDataReportMarkedCertified:
				return "Pay Data Report Marked Certified";
			case AuditLogTypes.PayDataReportCorrected:
				return "Pay Data Report Corrected";
			case AuditLogTypes.ResourceCostProfileChanged:
				return "Resource Cost Profile Changed";
			case AuditLogTypes.ResourceUsageChanged:
				return "Resource Usage Changed";
			case AuditLogTypes.FieldCostRunCreated:
				return "Field Cost Run Created";
			case AuditLogTypes.FieldCostRunFrozen:
				return "Field Cost Run Frozen";
			case AuditLogTypes.AdminAssistDiagnosticAccess:
				return "Admin Assist Diagnostic Access";
			case AuditLogTypes.AiDispatchSettingsUpdated:
				return "AI Dispatch Settings Updated";
			case AuditLogTypes.AdminAssistPlanAccess:
				return "Admin Assist Plan Access";
		}

			return $"Unknown ({logType})";
		}
	}
}
