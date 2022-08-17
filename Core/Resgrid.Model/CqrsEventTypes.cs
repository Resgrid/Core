namespace Resgrid.Model
{
	public enum CqrsEventTypes
	{
		None = 0,
		UnitLocation = 1,
		PushRegistration = 2,
		UnitPushRegistration = 3,
		StripeChargeSucceeded = 4,
		StripeChargeFailed = 5,
		StripeChargeRefunded = 6,
		StripeSubUpdated = 7,
		StripeSubDeleted = 8,
		ClearDepartmentCache = 9,
		AuditLog = 10,
		NewChatMessage = 11,
		TroubleAlert = 12,
		StripeCheckoutCompleted = 13,
		StripeCheckoutUpdated = 14,
		StripeInvoicePaid = 15,
		StripeInvoiceItemDeleted = 16,
	}
}
