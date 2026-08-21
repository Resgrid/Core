namespace Resgrid.Model
{
	public enum SystemAuditTypes
	{
		Login = 0,
		Purchase = 1,
		ContactVerification = 2,
		TwoFactorEnabled = 3,
		TwoFactorDisabled = 4,
		TwoFactorLoginVerified = 5,
		TwoFactorRecoveryCodeUsed = 6,
		TwoFactorStepUpVerified = 7,
		SsoLogin = 8,
		SsoLoginFailed = 9,
		ScimOperation = 10,
		AccountDeletionRequested = 11,
		GdprDataExportRequested = 12,
		GdprDataExportDownloaded = 13,
		PasswordChanged = 14,
		PasswordResetByAdministrator = 15,
		PublicPasswordResetCompleted = 16,
		UsernameChanged = 17,
		EmailChanged = 18,
		SessionRevoked = 19,
		OtherSessionsRevoked = 20,
		AllSessionsRevoked = 21,
		ExternalIdentityLinked = 22,
		ExternalIdentityUnlinked = 23,
		PasswordResetLinkSentByAdministrator = 24
	}
}
