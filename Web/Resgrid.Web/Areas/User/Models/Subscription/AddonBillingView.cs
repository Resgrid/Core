using System;
using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Subscription
{
	/// <summary>
	/// One add-on billing page (Readiness Pro, Business Ops, Incident Back Office, Enhanced AI). Every page shares this model and the
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

		public List<string> Notices { get; } = new List<string>();
		public List<string> Notes { get; } = new List<string>();
		public List<AddonSideNote> SideNotes { get; } = new List<AddonSideNote>();
		public List<AddonLink> Links { get; } = new List<AddonLink>();

		public bool Holds => Status != null && (Status.Active || Status.CanCancel);

		/// <summary>A completed checkout has returned but the provider webhook has not activated the add-on yet.</summary>
		public bool AwaitingActivation => Result == AddonBillingResults.Complete && Status != null && !Status.Active;

		public bool CanBuy => IsOwner && Offered && Status != null && Status.CheckoutAvailable && !Status.Active && !AwaitingActivation;

		public bool CanCancel => IsOwner && Status != null && Status.CanCancel && !Status.Cancelled;
	}

	public static class AddonBillingResults
	{
		public const string Complete = "complete";
		public const string Cancelled = "cancelled";
		public const string RenewalCancelled = "renewal-cancelled";
		public const string RenewalFailed = "renewal-failed";
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
	}

	/// <summary>A started provider checkout: a Stripe Checkout URL or a Paddle transaction id.</summary>
	public sealed record AddonCheckoutStart(string Provider, string Url, string TransactionId);

	public sealed record AddonLink(string Label, string Url, bool External = false);

	public sealed record AddonSideNote(string Title, string Text);
}
