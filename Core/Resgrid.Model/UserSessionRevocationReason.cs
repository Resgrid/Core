namespace Resgrid.Model
{
	public enum UserSessionRevocationReason
	{
		UserRevoked = 0,
		OtherSessionsRevoked = 1,
		PasswordChanged = 2,
		PasswordReset = 3,
		UsernameChanged = 4,
		EmailChanged = 5,
		AccountCompromised = 6,
		AdministratorRevoked = 7,
		MembershipDisabled = 8,
		SsoIdentityUnlinked = 9,
		ConcurrentSessionLimit = 10,
		Expired = 11,
		LoggedOut = 12,
		AccountDeactivated = 13
	}
}
