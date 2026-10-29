namespace Resgrid.Model.Billing.Api;

/// <summary>What happened to a discount code a department entered after signup.</summary>
public enum DiscountCodeOutcomes
{
	/// <summary>The code was applied to the department's active subscription.</summary>
	Applied = 0,

	/// <summary>No active, redeemable code matches (unknown, expired, used up, or refused by the provider's own restrictions).</summary>
	InvalidCode = 1,

	/// <summary>The code is valid but the department has no active subscription yet; it is applied at checkout instead.</summary>
	NoActiveSubscription = 2,

	/// <summary>Billing could not be reached or failed; nothing was changed.</summary>
	Unavailable = 3
}

/// <summary>Body of the Billing API's ApplyDiscountCodeToDepartment call.</summary>
public class ApplyDiscountCodeInput
{
	public int DepartmentId { get; set; }

	/// <summary>The customer-facing code: a Stripe promotion code or a Paddle discount code.</summary>
	public string Code { get; set; }
}

public class ApplyDiscountCodeResult : BillingApiResponseBase
{
	public ApplyDiscountCodeData Data { get; set; }
}

public class ApplyDiscountCodeData
{
	public DiscountCodeOutcomes Outcome { get; set; }

	/// <summary>The provider's description of the discount, for the confirmation message (never a secret).</summary>
	public string Description { get; set; }
}
