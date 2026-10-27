namespace Resgrid.Model
{
	public enum AuditLogTypes
	{
		DepartmentSettingsChanged,
		UserAdded,
		UserRemoved,
		GroupAdded,
		GroupRemoved,
		GroupChanged,
		UnitAdded,
		UnitRemoved,
		UnitChanged,
		ProfileUpdated,
		PermissionsChanged,
		SubscriptionUpdated,
		SubscriptionCreated,
		SubscriptionCancelled,
		SubscriptionBillingInfoUpdated,
		// New
		CallReactivated,
		UserAccountDeleted,
		AddonSubscriptionModified,
		DeleteDepartmentRequested,
		DeleteDepartmentRequestedCancelled,
		DeleteStaticShift,
		UpdateStaticShift,
		// New 7-13-2024
		CustomStatusAdded,
		CustomStatusRemoved,
		CustomStatusUpdated,
		CustomStatusDetailUpdated,
		CallTypeAdded,
		CallTypeEdited,
		CallTypeRemoved,
		CallPriorityAdded,
		CallPriorityEdited,
		CallPriorityRemoved,
		UnitTypeAdded,
		UnitTypeEdited,
		UnitTypeRemoved,
		CertificationTypeAdded,
		CertificationTypeEdited,
		CertificationTypeRemoved,
		DocumentCategoryAdded,
		DocumentCategoryEdited,
		DocumentCategoryRemoved,
		DocumentAdded,
		DocumentEdited,
		DocumentRemoved,
		NoteCategoryAdded,
		NoteCategoryEdited,
		NoteCategoryRemoved,
		NoteAdded,
		NoteEdited,
		NoteRemoved,
		ContactAdded,
		ContactEdited,
		ContactRemoved,
		ContactCategoryAdded,
		ContactCategoryEdited,
		ContactCategoryRemoved,
		ContactNoteTypeAdded,
		ContactNoteTypeEdited,
		ContactNoteTypeRemoved,
		// Workflow operations
		WorkflowAdded,
		WorkflowEdited,
		WorkflowDeleted,
		WorkflowStepAdded,
		WorkflowStepEdited,
		WorkflowStepDeleted,
		WorkflowCredentialAdded,
		WorkflowCredentialEdited,
		WorkflowCredentialDeleted,
		// Contact verification
		ContactVerificationCodeSent,
		ContactVerificationConfirmed,
		ContactVerificationFailed,
		// Two-factor authentication
		TwoFactorEnabled,
		TwoFactorDisabled,
		TwoFactorLoginVerified,
		TwoFactorRecoveryCodeUsed,
		TwoFactorStepUpVerified,
		// SSO / SAML / OIDC
		SsoConfigCreated,
		SsoConfigUpdated,
		SsoConfigDeleted,
		SsoLoginSucceeded,
		SsoLoginFailed,
		SsoUserProvisioned,
		// SCIM 2.0
		ScimUserCreated,
		ScimUserUpdated,
		ScimUserDeactivated,
		ScimUserDeleted,
		ScimAuthFailed,
		ScimUserReactivated,
		ScimGroupListed,
		ScimUserListed,
		ScimUserRetrieved,
		// SCIM bearer token lifecycle
		ScimBearerTokenProvisioned,
		ScimBearerTokenRotated,
		// User Defined Fields
		UdfDefinitionCreated,
		UdfDefinitionUpdated,
		UdfDefinitionDeleted,
		UdfFieldAdded,
		UdfFieldUpdated,
		UdfFieldRemoved,
		UdfFieldValueSaved,
		// Route Planning
		RouteCreated,
		RouteUpdated,
		RouteDeleted,
		RouteStarted,
		RouteCompleted,
		RouteCancelled,
		RoutePaused,
		RouteResumed,
		RouteStopCheckedIn,
		RouteStopCheckedOut,
		RouteStopSkipped,
		RouteDeviationDetected,
		RouteDeviationAcknowledged,
		// Check-In Timers
		CheckInTimerConfigCreated,
		CheckInTimerConfigUpdated,
		CheckInTimerConfigDeleted,
		CheckInTimerOverrideCreated,
		CheckInTimerOverrideUpdated,
		CheckInTimerOverrideDeleted,
		CheckInPerformed,
		CheckInTimerEnabledOnCall,
		CheckInTimerDisabledOnCall,
		// Calendar Check-In Attendance
		CalendarCheckInPerformed,
		CalendarCheckOutPerformed,
		CalendarCheckInUpdated,
		CalendarCheckInDeleted,
		CalendarAdminCheckInPerformed,
		// Log operations
		LogCreated,
		LogDeleted,
		// Communication Test operations
		CommunicationTestCreated,
		CommunicationTestUpdated,
		CommunicationTestDeleted,
		CommunicationTestRunStarted,
		// Weather Alerts
		WeatherAlertSourceCreated,
		WeatherAlertSourceUpdated,
		WeatherAlertSourceDeleted,
		WeatherAlertSourceEnabled,
		WeatherAlertSourceDisabled,
		WeatherAlertZoneCreated,
		WeatherAlertZoneUpdated,
		WeatherAlertZoneDeleted,
		WeatherAlertZoneEnabled,
		WeatherAlertZoneDisabled,
		WeatherAlertSettingsChanged,
		// Feature Toggles
		FeatureFlagChanged,
		FeatureFlagOverrideChanged,
		// Unit hardware tracking
		UnitTrackingDeviceCreated,
		UnitTrackingDeviceUpdated,
		UnitTrackingDeviceDisabled,
		UnitTrackingDeviceDeleted,
		UnitTrackingCredentialCreated,
		UnitTrackingCredentialRotated,
		UnitTrackingCredentialRevoked,
		// Department deletion lifecycle
		DeleteDepartmentRequestExecuted,
		// Chat moderation
		ChatMessageDeletedByModerator,
		ChatUserMuted,
		ChatUserUnmuted,
		ChatUserBanned,
		ChatUserUnbanned,
		ChatChannelLocked,
		ChatChannelUnlocked,
		ChatChannelArchived,
		ChatFlagResolved,
		ChatSettingsChanged,
		ChatExportRequested,
		ChatExportDownloaded,
		// General moderation
		ModerationReportSubmitted,
		ModerationRequestReopened,
		ModerationRequestCompleted,
		ModerationEvidenceDownloaded,
		PasswordResetByAdministrator,
		UserAuthenticationSessionsRevoked,
		// Advanced Data Protection: a department releasing (or restoring) the step-up prompt for a
		// client app. Weakening a protection control is exactly the kind of change that has to be
		// answerable later, so it is audited with the before and after mask.
		DataProtectionStepUpExemptionsChanged,
		// Contact pre-plans and site attachments (Contacts plan Phase A, A7)
		ContactPreplanAdded,
		ContactPreplanUpdated,
		ContactPreplanRemoved,
		ContactAttachmentAdded,
		ContactAttachmentRemoved,
		ChecklistDefinitionAdded,
		ChecklistDefinitionUpdated,
		ChecklistDefinitionPublished,
		ChecklistDefinitionRetired,
		ChecklistDefinitionRemoved,
		ChecklistCompletionStarted,
		ChecklistProgressSaved,
		ChecklistCompletionSubmitted,
		ChecklistWitnessAttested,
		ChecklistFileAdded,
		ChecklistFileRemoved,
		ChecklistScheduleAdded,
		ChecklistScheduleUpdated,
		ChecklistOccurrenceMissed,
		ChecklistOccurrenceSkipped,
ChecklistReminderSettingsUpdated,
		WorkOrderChanged,
		InventoryChanged,

		// Workforce & Business Operations plan Phase B (customer invoicing). Append-only.
		BillingProfileChanged,
		RateCardChanged,
		InvoiceCreated,
		InvoiceUpdated,
		InvoiceSent,
		InvoiceVoided,
		InvoicePaymentRecorded,
		DepartmentBillingIdentityChanged,

		// Phase B2 online payments.
		PaymentConnectionConnected,
		PaymentConnectionDisconnected,
		PaymentConnectionRevoked,
		PaymentConnectionActionRequired,
		InvoicePaymentRequestCreated,
		InvoicePaymentRefunded,
		InvoicePaymentDisputed,
		PaymentWebhookRejected,
		InvoicePaymentRequestFailed,
		InvoicePaymentRequestExpired,

		// Workforce & Business Operations plan Phase D (certifications & role qualification). Append-only.
		CertificationAdded,
		CertificationUpdated,
		CertificationRemoved,
		CertificationStatusChanged,
		CertificationVerified,
		CertificationCreditAdded,
		CertificationCreditRemoved,
		RoleCertificationRequirementChanged,
		DepartmentCertificationSettingsChanged,
		RoleMemberAdded,
		RoleMemberRemoved,
		RoleMemberRemovedByCertification,
		UnitCertificationAdded,
		UnitCertificationUpdated,
		UnitCertificationRemoved,
		UnitCertificationStatusChanged,

		// Workforce & Business Operations plan Phase C (deployment core). Append-only; the contractor-billing and
		// Cal OES MARS milestones append their own entries after these.
		DeploymentCreated,
		DeploymentUpdated,
		DeploymentStatusChanged,
		DeploymentRosterChanged,
		DeploymentEquipmentChanged,
		DeploymentAttachmentAdded,
		DeploymentAttachmentRemoved,
		TimeReportCreated,
		TimeReportUpdated,
		TimeReportSubmitted,
		TimeReportApproved,
		TimeReportVoided,
		DeploymentExpenseAdded,
		DeploymentExpenseUpdated,
		DeploymentExpenseRemoved,

		// Workforce & Business Operations plan Phase C-M2 (contractor path). Append-only.
		RateScheduleCreated,
		RateScheduleUpdated,
		RateScheduleDeleted,
		RateScheduleEntryChanged,
		RatePremiumChanged,
		ServiceContractCreated,
		ServiceContractUpdated,
		ServiceContractStatusChanged,
		ServiceContractDeleted,
		ComplianceDocumentAdded,
		ComplianceDocumentUpdated,
		ComplianceDocumentRemoved,
		BidCreated,
		BidUpdated,
		BidSent,
		BidAccepted,
		BidDeclined,
		BidWithdrawn,
		BidExpired,
		BidConverted,
		BidDeleted,
		TimeReportBilled,
		DeploymentInvoiceGenerated,

		// Workforce & Business Operations plan Phase C-M3 (Cal OES MARS cost recovery). Append-only.
		CalOesMarsAgencyProfileChanged,
		CalOesMarsResourceProfileChanged,
		CalOesMarsRateProfileChanged,
		CalOesMarsRateDraftBuilt,
		CalOesMarsRateReviewed,
		CalOesMarsAgreementChanged,
		CalOesMarsAgreementObserved,
		CalOesMarsWorkItemPrepared,
		CalOesMarsWorkItemValidated,
		CalOesMarsReimbursementCalculated,
		CalOesMarsWorkItemOpenedForHandoff,
		CalOesMarsExternalStatusObserved,
		CalOesMarsInvoiceApproved,
		CalOesMarsInvoiceRejected,
		CalOesMarsPaymentReconciled,
		CalOesMarsWorkItemDeleted,

		// Workforce & Business Operations plan Phase E (protected workforce pay data, field costing, California pay data reporting). Append-only.
		WorkforceEmployerProfileChanged,
		WorkforceEstablishmentChanged,
		WorkforceEmploymentChanged,
		WorkforceCompensationChanged,
		WorkforceAnnualPayFactImported,
		PayDataDemographicChanged,
		PayDataReportCreated,
		PayDataReportValidated,
		PayDataReportFrozen,
		PayDataReportExported,
		PayDataReportMarkedCertified,
		PayDataReportCorrected,
		ResourceCostProfileChanged,
		ResourceUsageChanged,
		FieldCostRunCreated,
		FieldCostRunFrozen,

		// Member removal lifecycle (2026-09-22): a removed membership brought back; it returns without its old admin standing. Append-only.
		UserReactivated,
		DepartmentConfigurationChanged,
		AdminAssistReviewChanged,
		AdminAssistDiagnosticAccess,
		/// <summary>AI dispatch department settings saved (enhanced-ai-addon-plan.md §4). Append-only.</summary>
		AiDispatchSettingsUpdated,
		AdminAssistPlanAccess,
		/// <summary>The department security policy changed, including which second factors it accepts (passkey plan section 10.1). Append-only.</summary>
		DepartmentSecurityPolicyChanged,
		/// <summary>A department API key was issued (pending-calls integration work, 2026-10-06). Append-only.</summary>
		DepartmentApiKeyCreated,
		/// <summary>A department API key was revoked. Append-only.</summary>
		DepartmentApiKeyRevoked,
		// Department Import (wizard plan section 18.3 I9). Append-only.
		/// <summary>An import run's preview was approved (approval digest frozen).</summary>
		DepartmentImportApproved,
		/// <summary>An import commit run finished; one summary entry per run, never per row.</summary>
		DepartmentImportCommitted,
		/// <summary>An import commit run was rolled back.</summary>
		DepartmentImportRolledBack,
		/// <summary>An import activation item was completed (activation sign-off, enablement or administrator flags).</summary>
		DepartmentImportActivated,
		/// <summary>Invitations for imported people were scheduled or sent.</summary>
		DepartmentImportInvitationsSent,
		/// <summary>Resgrid support access to an import workspace was granted or raised.</summary>
		DepartmentImportSupportAccessGranted,
		/// <summary>Resgrid support access to an import workspace was revoked or expired.</summary>
		DepartmentImportSupportAccessRevoked,
		/// <summary>Resgrid staff viewed sampled values of an import workspace under a grant.</summary>
		DepartmentImportStaffValuesViewed,
		/// <summary>A raw import artifact or a report containing values was downloaded.</summary>
		DepartmentImportArtifactDownloaded,
		/// <summary>Import artifacts were purged early on request.</summary>
		DepartmentImportArtifactsPurged,

		// Incident Back Office (large-incident-logistics-finance-plan.md §10 R10, §15.4): ledger mutations through AuditLog with the
		// acting incident position in the audit body; record access stays RmsAccessAudit.
		/// <summary>An Incident Back Office activation was opened (plan §9.0).</summary>
		IncidentActivationOpened,
		/// <summary>An activation header, link or complex membership changed.</summary>
		IncidentActivationUpdated,
		/// <summary>An activation moved through its lifecycle (demobilizing, closed, sealed, reopened).</summary>
		IncidentActivationStatusChanged,
		/// <summary>An Incident Back Office ledger or catalog row was created (table, id and acting position in the audit body).</summary>
		IncidentRecordCreated,
		/// <summary>An Incident Back Office ledger or catalog row was changed.</summary>
		IncidentRecordUpdated,
		/// <summary>An Incident Back Office row was soft-deleted.</summary>
		IncidentRecordDeleted,
		/// <summary>A fixed-lifecycle transition (request, fill, demob, engagement, requisition, time, claim) was recorded.</summary>
		IncidentStatusTransition,
		/// <summary>A standing team roster was activated into an incident in one action.</summary>
		IncidentTeamActivated,
		/// <summary>A financial obligation was committed, an invoice accepted or funding authorized.</summary>
		IncidentFinancialCommitment,
		/// <summary>A participant or vendor self-service link was issued.</summary>
		IncidentSelfServiceLinkIssued,
		/// <summary>A self-service link was revoked.</summary>
		IncidentSelfServiceLinkRevoked,
		/// <summary>A self-service page was opened or acted on through its link.</summary>
		IncidentSelfServiceAccessed,
		/// <summary>A coordination board or lane was shared with a partner agency.</summary>
		CoordinationBoardShared,
		/// <summary>A coordination board share grant was revoked.</summary>
		CoordinationBoardShareRevoked,
		/// <summary>A partner agency read a shared coordination board.</summary>
		CoordinationBoardShareAccessed,
		/// <summary>A read of protected Incident Back Office data was refused (plan §15.4: denied attempts are audited).</summary>
		IncidentProtectedAccessDenied,
		/// <summary>An Incident Back Office department policy (settings 85-97, 120) changed.</summary>
		IncidentBackOfficeSettingsChanged
	}
}
