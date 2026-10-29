using System;
using System.Collections.Generic;
using System.Linq;
using Resgrid.Config;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Subscription
{
	public enum AddonBillingPeriod
	{
		Monthly,
		Yearly
	}

	/// <summary>
	/// One purchasable add-on as the Subscription page and the add-on billing pages present it. Copy lives in the Addons resource
	/// under <c>{Key}Title</c>, <c>{Key}Tagline</c>, <c>{Key}Feature1..n</c> and <c>{Key}Note</c>; prices mirror resgrid.com/pricing.
	/// </summary>
	public sealed class AddonDefinition
	{
		public PlanAddonTypes Type { get; init; }
		public string Key { get; init; }
		/// <summary>Font Awesome 4 icon class.</summary>
		public string Icon { get; init; }
		/// <summary>Public feature page under <see cref="UrlsConfig.HomeUrl"/>/features/.</summary>
		public string Slug { get; init; }
		/// <summary>Operator toggle that decides whether the add-on is offered to a department; null when it is always offered.</summary>
		public string FeatureFlag { get; init; }
		public AddonBillingPeriod Period { get; init; }
		/// <summary>Priced per 10-user pack rather than per department.</summary>
		public bool PerUserPack { get; init; }
		public int FeatureCount { get; init; }
		public Func<decimal> StripeAmount { get; init; }
		public Func<decimal> PaddleAmount { get; init; }

		public string TitleKey => Key + "Title";
		public string TaglineKey => Key + "Tagline";
		public string NoteKey => Key + "Note";
		public IEnumerable<string> FeatureKeys => Enumerable.Range(1, FeatureCount).Select(i => Key + "Feature" + i);
		public string LearnMoreUrl => (UrlsConfig.HomeUrl ?? "https://resgrid.com").TrimEnd('/') + "/features/" + Slug;
		/// <summary>The price the active payment provider charges: Paddle (EUR, EU cluster) or Stripe (USD).</summary>
		public decimal Price => AddonCatalog.UsesPaddle ? PaddleAmount() : StripeAmount();
	}

	/// <summary>The add-ons in the order resgrid.com/pricing lists them.</summary>
	public static class AddonCatalog
	{
		public static readonly IReadOnlyList<AddonDefinition> All = new[]
		{
			new AddonDefinition
			{
				Type = PlanAddonTypes.PTT, Key = "Ptt", Icon = "fa-microphone", Slug = "push-to-talk", Period = AddonBillingPeriod.Monthly, PerUserPack = true,
				FeatureCount = 5, StripeAmount = () => PaymentProviderConfig.PttAddonStripeMonthlyAmount, PaddleAmount = () => PaymentProviderConfig.PttAddonPaddleMonthlyAmount
			},
			new AddonDefinition
			{
				Type = PlanAddonTypes.ReadinessPro, Key = "ReadinessPro", Icon = "fa-wrench", Slug = "readiness-pro", FeatureFlag = FeatureFlagKeys.MaintenanceWorkOrders,
				Period = AddonBillingPeriod.Monthly, FeatureCount = 5, StripeAmount = () => ReadinessProConfig.StripeMonthlyAmount, PaddleAmount = () => ReadinessProConfig.PaddleMonthlyAmount
			},
			new AddonDefinition
			{
				Type = PlanAddonTypes.BusinessOperations, Key = "BusinessOps", Icon = "fa-briefcase", Slug = "business-ops", FeatureFlag = FeatureFlagKeys.BusinessOperations,
				Period = AddonBillingPeriod.Monthly, FeatureCount = 5, StripeAmount = () => BusinessOperationsAddonConfig.StripeMonthlyAmount, PaddleAmount = () => BusinessOperationsAddonConfig.PaddleMonthlyAmount
			},
			new AddonDefinition
			{
				Type = PlanAddonTypes.IncidentBackOffice, Key = "IncidentBackOffice", Icon = "fa-sitemap", Slug = "incident-back-office", FeatureFlag = FeatureFlagKeys.IncidentBackOffice,
				Period = AddonBillingPeriod.Monthly, FeatureCount = 6, StripeAmount = () => IncidentBackOfficeAddonConfig.StripeMonthlyAmount, PaddleAmount = () => IncidentBackOfficeAddonConfig.PaddleMonthlyAmount
			},
			new AddonDefinition
			{
				Type = PlanAddonTypes.EnhancedAi, Key = "EnhancedAi", Icon = "fa-magic", Slug = "enhanced-ai", FeatureFlag = FeatureFlagKeys.AiEnhanced,
				Period = AddonBillingPeriod.Monthly, FeatureCount = 6, StripeAmount = () => AiAddonConfig.StripeMonthlyAmount, PaddleAmount = () => AiAddonConfig.PaddleMonthlyAmount
			},
			new AddonDefinition
			{
				Type = PlanAddonTypes.ADP, Key = "Adp", Icon = "fa-shield", Slug = "advanced-data-protection", FeatureFlag = FeatureFlagKeys.DepartmentProtectedDataEnrollment,
				Period = AddonBillingPeriod.Yearly, FeatureCount = 5, StripeAmount = () => AdpAddonConfig.StripeYearlyAmount, PaddleAmount = () => AdpAddonConfig.PaddleYearlyAmount
			}
		};

		public static AddonDefinition Get(PlanAddonTypes type) => All.First(x => x.Type == type);

		/// <summary>Add-on checkout and currency follow the cluster's active payment provider, exactly as the Billing API prices them.</summary>
		public static bool UsesPaddle => PaymentProviderConfig.IsPaddleActive();

		public static string CurrencySymbol => UsesPaddle ? "€" : "$";
	}

	/// <summary>Whether one add-on is offered to, and held by, the current department.</summary>
	public sealed class AddonAvailability
	{
		public AddonDefinition Definition { get; init; }
		/// <summary>The add-on's operator toggle is on for the department (always true when it has none).</summary>
		public bool Offered { get; init; }
		/// <summary>The department holds a current, paid add-on interval.</summary>
		public bool Owned { get; init; }
		public bool Cancelled { get; init; }
		public DateTime? EndsOn { get; init; }

		/// <summary>
		/// Cards are shown to buy only when the toggle is on and the department is on a paid plan; a held add-on is always shown so it
		/// can still be managed or cancelled after its toggle is switched off.
		/// </summary>
		public bool IsVisible(bool hasPaidPlan) => Owned || (Offered && hasPaidPlan);
	}
}
