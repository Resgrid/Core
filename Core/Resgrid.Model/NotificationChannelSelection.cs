namespace Resgrid.Model
{
	/// <summary>Preference/contact gates used by the ordinary notification sender, before provider/device checks.</summary>
	public sealed record NotificationChannelSelection(bool Sms, bool Email, bool Push)
	{
		public static NotificationChannelSelection From(UserProfile profile) => profile == null
			? new(true, true, true)
			: From(profile.SendNotificationSms, profile.MobileNumberVerified, profile.SendNotificationEmail,
				profile.EmailVerified, profile.SendNotificationPush);
		public static NotificationChannelSelection From(bool sms, bool? mobileVerified, bool email, bool? emailVerified, bool push) =>
			new(sms && mobileVerified.IsContactMethodAllowedForSending(), email && emailVerified.IsContactMethodAllowedForSending(), push);
	}
}
