using System;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Subscription
{
	/// <summary>
	/// The ADP addon purchase and management pages (plan section 17.1).
	///
	/// Modelled on <see cref="BuyAddonView"/> with one hard difference: every ADP billing action is
	/// restricted to <c>Department.ManagingUserId</c>, server-side. <see cref="IsManagingMember"/>
	/// only decides what the page draws — the controller re-checks it on every action, because a
	/// hidden button is not an authorization control.
	///
	/// The billing facts and the protection facts are deliberately kept as separate properties and
	/// never merged into one "is it on" flag. Buying the addon encrypts nothing, and cancelling
	/// decrypts nothing; only the enrollment and offboarding migrations move ciphertext, and a page
	/// that blurred the two would tell a member their data was safe before it was.
	/// </summary>
	public class AdpAddonView : BaseUserModel
	{
		public Department Department { get; set; }

		public PlanAddon PlanAddon { get; set; }

		public string PlanAddonId { get; set; }

		/// <summary>Yearly price, already formatted for the department's currency.</summary>
		public string Price { get; set; }

		/// <summary>Caller is the department's managing member — the only identity that may buy or cancel.</summary>
		public bool IsManagingMember { get; set; }

		/// <summary>The addon requires an active paid plan; a free department may look but not buy.</summary>
		public bool HasPaidPlan { get; set; }

		/// <summary>An addon row exists for the department, cancelled or not.</summary>
		public bool HasAddon { get; set; }

		/// <summary>The addon is cancelled and running out its paid period.</summary>
		public bool IsCancelled { get; set; }

		/// <summary>End of the current billing period, from the addon row.</summary>
		public DateTime? EndingOn { get; set; }

		/// <summary>Durable protection state. Disabled with an active addon means "bought, not yet enrolled".</summary>
		public DepartmentDataProtectionState ProtectionState { get; set; }

		/// <summary>What billing has been paid through, as recorded on the protection policy.</summary>
		public DateTime? PaidThroughOn { get; set; }

		/// <summary>
		/// End of the lapse grace window, when payment is outstanding. Present means "we are waiting
		/// for money and protection is running anyway until this date".
		/// </summary>
		public DateTime? GraceEndsOn { get; set; }

		/// <summary>When a scheduled offboarding migration takes effect.</summary>
		public DateTime? OffboardingEffectiveOn { get; set; }

		/// <summary>True while payment is outstanding and the grace window has not run out.</summary>
		public bool IsInGrace => GraceEndsOn.HasValue && GraceEndsOn.Value > DateTime.UtcNow;

		public string ErrorMessage { get; set; }
	}
}
