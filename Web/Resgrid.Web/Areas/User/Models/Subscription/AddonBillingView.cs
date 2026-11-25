using System;
using System.Collections.Generic;
using Resgrid.Model;
using Resgrid.Model.Billing;

namespace Resgrid.Web.Areas.User.Models.Subscription
{
	/// <summary>
	/// One add-on billing page (Readiness Pro, Business Ops, Incident Back Office, Enhanced AI, Dedicated Number). Every page shares this model and the
	/// _AddonBilling partial so they read and behave the same; the controller adds the add-on's own notes and links.
	/// </summary>
	public sealed class AddonBillingView
	{
		public AddonDefinition Addon { get; init; }
		public string Title { get; init; }
		public string Tagline { get; init; }
		public IReadOnlyList<string> Features { get; init; } = Array.Empty<string>();
		public string Note { get; init; }

		/// <summary>The department's managing member: the only identity that may buy or cancel.</summary>
		public bool IsOwner { get; init; }

		/// <summary>Billing status is shown to the managing member and to members who may manage the subscription.</summary>
		public bool CanViewBilling { get; init; }

		/// <summary>The add-on's operator toggle is on for the department.</summary>
		public bool Offered { get; init; }

		/// <summary>Null when billing is unavailable or the viewer may not see it.</summary>
		public AddonBillingStatusView Status { get; init; }

		/// <summary>The outcome a provider return URL reported: <c>complete</c> or <c>cancelled</c>; renewal outcomes too.</summary>
		public string Result { get; init; }

		/// <summary>How many times the page has refreshed itself while waiting for a completed checkout to reconcile.</summary>
		public int Attempt { get; init; }

		/// <summary>The pack quantity section of a pack add-on (the LMS); null for a flat add-on.</summary>
		public AddonPackView Packs { get; set; }

		public List<string> Notices { get; } = new List<string>();
		public List<string> Notes { get; } = new List<string>();
		public List<AddonSideNote> SideNotes { get; } = new List<AddonSideNote>();
		public List<AddonLink> Links { get; } = new List<AddonLink>();

		public bool Holds => Status != null && (Status.Active || Status.CanCancel);

		/// <summary>A completed checkout has returned but the provider webhook has not activated the add-on yet.</summary>
		public bool AwaitingActivation => Result == AddonBillingResults.Complete && Status != null && !Status.Active;

		public bool CanBuy => IsOwner && Offered && Status != null && Status.CheckoutAvailable && !Status.Active && !AwaitingActivation;

		/// <summary>The managing member may change the pack quantity of a running pack add-on.</summary>
		public bool CanChangePacks => IsOwner && Packs != null && Status != null && Status.Active && !Status.Cancelled;

		public bool CanCancel => IsOwner && Status != null && Status.CanCancel && !Status.Cancelled;
	}

	public static class AddonBillingResults
	{
		public const string Complete = "complete";
		public const string Cancelled = "cancelled";
		public const string RenewalCancelled = "renewal-cancelled";
		public const string RenewalFailed = "renewal-failed";
		/// <summary>Packs were added and invoiced at once (prorated).</summary>
		public const string PacksAdded = "packs-added";
		/// <summary>A pack decrease was scheduled for the next billing date.</summary>
		public const string PacksScheduled = "packs-scheduled";
		/// <summary>The pack change could not be made.</summary>
		public const string PacksFailed = "packs-failed";
		/// <summary>A decrease would leave fewer seats than LMS-enabled members.</summary>
		public const string PacksBelowSeats = "packs-below-seats";

		/// <summary>The outcomes a billing page shows; anything else in the query string is ignored.</summary>
		public static bool IsKnown(string result) => result is Complete or Cancelled or RenewalCancelled or RenewalFailed or PacksAdded or PacksScheduled or PacksFailed or PacksBelowSeats;
	}

	/// <summary>
	/// The pack quantity section of a pack add-on's billing page: packs in force (and scheduled), seats in use, the graduated tiers
	/// and next month's price. Prices come from the same <see cref="PackPriceSchedule"/> the Billing API's graduated price mirrors.
	/// </summary>
	public sealed class AddonPackView
	{
		public PackPriceSchedule Schedule { get; init; }
		public string CurrencySymbol { get; init; }

		/// <summary>Packs in force now (0 before the first purchase).</summary>
		public int Packs { get; init; }

		/// <summary>Packs from the next billing date when a decrease is scheduled.</summary>
		public int? ScheduledPacks { get; init; }

		/// <summary>LMS-enabled active members; a decrease never goes below the packs that seat them.</summary>
		public int EnabledSeats { get; init; }
		public int MinimumPacks { get; init; }

		/// <summary>The quantity the page proposes: the packs in force, or the packs that seat the enabled members for a first purchase.</summary>
		public int ProposedPacks { get; init; }

		public DateTime? NextBillingUtc { get; init; }

		public int PurchasedSeats => Schedule == null ? 0 : Schedule.SeatsFor(Packs);
		public int BillingPacks => ScheduledPacks ?? Packs;
		public decimal CurrentMonthly => Schedule?.MonthlyTotal(Packs) ?? 0m;
		public decimal NextMonthly => Schedule?.MonthlyTotal(BillingPacks) ?? 0m;
	}

	/// <summary>The four Billing API add-on statuses share one shape; this is it, so the page is written once.</summary>
	public sealed class AddonBillingStatusView
	{
		public string Provider { get; init; }
		public string Currency { get; init; }
		public decimal Amount { get; init; }
		public bool CheckoutAvailable { get; init; }
		public bool Active { get; init; }
		public bool CanCancel { get; init; }
		public bool Cancelled { get; init; }
		public DateTime? PaidThroughUtc { get; init; }

		public static AddonBillingStatusView From(ReadinessProBillingStatus s) => s == null ? null : new AddonBillingStatusView
		{
			Provider = s.Provider, Currency = s.Currency, Amount = s.MonthlyAmount, CheckoutAvailable = s.CheckoutAvailable, Active = s.Active,
			CanCancel = s.CanCancel, Cancelled = s.Cancelled, PaidThroughUtc = s.PaidThroughUtc
		};

		public static AddonBillingStatusView From(BusinessOperationsBillingStatus s) => s == null ? null : new AddonBillingStatusView
		{
			Provider = s.Provider, Currency = s.Currency, Amount = s.MonthlyAmount, CheckoutAvailable = s.CheckoutAvailable, Active = s.Active,
			CanCancel = s.CanCancel, Cancelled = s.Cancelled, PaidThroughUtc = s.PaidThroughUtc
		};

		public static AddonBillingStatusView From(IncidentBackOfficeBillingStatus s) => s == null ? null : new AddonBillingStatusView
		{
			Provider = s.Provider, Currency = s.Currency, Amount = s.MonthlyAmount, CheckoutAvailable = s.CheckoutAvailable, Active = s.Active,
			CanCancel = s.CanCancel, Cancelled = s.Cancelled, PaidThroughUtc = s.PaidThroughUtc
		};

		public static AddonBillingStatusView From(AiBillingStatus s) => s == null ? null : new AddonBillingStatusView
		{
			Provider = s.Provider, Currency = s.Currency, Amount = s.MonthlyAmount, CheckoutAvailable = s.CheckoutAvailable, Active = s.Active,
			CanCancel = s.CanCancel, Cancelled = s.Cancelled, PaidThroughUtc = s.PaidThroughUtc
		};

		public static AddonBillingStatusView From(DedicatedNumberBillingStatus s) => s == null ? null : new AddonBillingStatusView
		{
			Provider = s.Provider, Currency = s.Currency, Amount = s.MonthlyAmount, CheckoutAvailable = s.CheckoutAvailable, Active = s.Active,
			CanCancel = s.CanCancel, Cancelled = s.Cancelled, PaidThroughUtc = s.PaidThroughUtc
		};

		/// <summary>The LMS is sold only with a paid base plan (§25.4a item 9): without one, checkout is not offered.</summary>
		public static AddonBillingStatusView From(LmsBillingStatus s) => s == null ? null : new AddonBillingStatusView
		{
			Provider = s.Provider, Currency = s.Currency, Amount = s.MonthlyAmount, CheckoutAvailable = s.CheckoutAvailable && s.HasEntityPlan, Active = s.Active,
			CanCancel = s.CanCancel, Cancelled = s.Cancelled, PaidThroughUtc = s.PaidThroughUtc
		};
	}

	/// <summary>A started provider checkout: a Stripe Checkout URL or a Paddle transaction id.</summary>
	public sealed record AddonCheckoutStart(string Provider, string Url, string TransactionId);

	public sealed record AddonLink(string Label, string Url, bool External = false);

	public sealed record AddonSideNote(string Title, string Text);
}
