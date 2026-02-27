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
		TwoFactorStepUpVerified = 7
	}
}
