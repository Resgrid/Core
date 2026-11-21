using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Resgrid.Model.Billing.Api;
using Resgrid.Model.Services;

namespace Resgrid.Web.Helpers
{
	/// <summary>
	/// A discount code entered for a department, on the Subscription page or at signup when no plan picker follows. The Billing API
	/// applies it to an active subscription right away; with none yet, a valid code is held in this browser's TempData and the
	/// Subscription page carries it into checkout (the Billing API pre-applies it to the Stripe session or Paddle transaction).
	/// The outcome is left for the Subscription page's codes panel to show.
	/// </summary>
	public static class DiscountCodeEntry
	{
		/// <summary>TempData key of a valid code waiting for checkout.</summary>
		public const string PendingCodeKey = "SubscriptionPendingDiscountCode";

		/// <summary>TempData key of the codes panel's last outcome (an Addons <c>Message*</c> resource key suffix).</summary>
		public const string MessageKey = "SubscriptionCodesMessage";

		/// <summary>The longest code accepted; nothing longer is sent to billing.</summary>
		public const int MaxLength = 64;

		/// <summary>
		/// Checks <paramref name="discountCode"/> for the department and records the outcome. A new entry always replaces the code
		/// waiting for checkout, whatever its outcome: only a code still pending is carried forward.
		/// </summary>
		public static async Task<DiscountCodeOutcomes> EnterAsync(ISubscriptionsService subscriptions, ITempDataDictionary tempData, int departmentId, string discountCode)
		{
			tempData.Remove(PendingCodeKey);

			var code = discountCode?.Trim();
			if (string.IsNullOrWhiteSpace(code) || code.Length > MaxLength)
			{
				tempData[MessageKey] = "DiscountInvalid";
				return DiscountCodeOutcomes.InvalidCode;
			}

			var outcome = (await subscriptions.ApplyDiscountCodeToDepartmentAsync(departmentId, code))?.Outcome ?? DiscountCodeOutcomes.Unavailable;
			switch (outcome)
			{
				case DiscountCodeOutcomes.Applied:
					tempData[MessageKey] = "DiscountApplied";
					break;
				case DiscountCodeOutcomes.NoActiveSubscription:
					tempData[MessageKey] = "DiscountPending";
					tempData[PendingCodeKey] = code;
					break;
				case DiscountCodeOutcomes.InvalidCode:
					tempData[MessageKey] = "DiscountInvalid";
					break;
				default:
					tempData[MessageKey] = "DiscountUnavailable";
					break;
			}

			return outcome;
		}

		/// <summary>Whether the outcome left the code in effect: applied now, or held for checkout.</summary>
		public static bool Succeeded(DiscountCodeOutcomes outcome) =>
			outcome == DiscountCodeOutcomes.Applied || outcome == DiscountCodeOutcomes.NoActiveSubscription;
	}
}
