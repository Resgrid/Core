namespace Resgrid.Model
{
	/// <summary>
	/// How a department pays for the ADP addon, which is what decides how long protection survives a
	/// lapse (ADP plan section 17.3).
	///
	/// The two are not the same problem. A card that expires fails immediately and the provider's own
	/// dunning retries settle it within days. An invoiced customer on NET terms has not failed
	/// anything: no charge was ever attempted, the invoice simply is not due yet, and a purchase
	/// order can sit in accounts payable for well over a month. Applying the card-shaped grace to
	/// them would decrypt a paying customer's data while their cheque is in the post.
	/// </summary>
	public enum AdpAddonBillingMode
	{
		/// <summary>Card or wallet charged automatically by the provider; provider dunning applies.</summary>
		Automatic = 1,

		/// <summary>Invoiced on payment terms (NET30/NET45/NET60); settled by the customer's finance team.</summary>
		Invoiced = 2
	}
}
