using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Helpers;

namespace Resgrid.Web.Areas.User.Models.Departments
{
	/// <summary>Department → Dedicated Number: the add-on state, the department's number and a carrier search.</summary>
	public sealed class DedicatedNumberView
	{
		/// <summary>Radius choices for a search near the department's address, in miles.</summary>
		public static readonly IReadOnlyList<int> DistanceOptions = new[] { 10, 25, 50, 100, 250 };

		public DedicatedNumberAccess Access { get; init; } = new DedicatedNumberAccess { AllowedCountries = Array.Empty<string>() };

		/// <summary>The department, for its time zone; null shows UTC dates.</summary>
		public Department Department { get; init; }

		public DepartmentPhoneNumber Active { get; init; }

		public DepartmentPhoneNumber Lapsed { get; init; }

		/// <summary>The Active number's intake stop and release dates once its department stopped paying (plan §8 D10); null while paying.</summary>
		public DedicatedNumberReleaseSchedule ActiveSchedule { get; init; }

		/// <summary>The Lapsed number's release date and the warnings sent.</summary>
		public DedicatedNumberReleaseSchedule LapsedSchedule { get; init; }

		public DepartmentPhoneNumber Provisioning { get; init; }

		/// <summary>The latest failed purchase when it is newer than every held number (shown once, until a number is held).</summary>
		public DepartmentPhoneNumber LastFailure { get; init; }

		/// <summary>The Enhanced AI add-on and Ai.VoiceIntake are both on, so the voice intake switch has an effect.</summary>
		public bool VoiceIntakeAvailable { get; init; }

		public bool Searched { get; init; }

		public string Country { get; init; }

		public string AreaCode { get; init; }

		public string Contains { get; init; }

		/// <summary>State or province code (United States / Canada).</summary>
		public string Region { get; init; }

		public string City { get; init; }

		public string PostalCode { get; init; }

		/// <summary>Search near the department's map center or address.</summary>
		public bool Near { get; init; }

		/// <summary>Radius in miles for <see cref="Near"/>.</summary>
		public int Distance { get; init; } = DedicatedNumberSearch.DefaultDistanceMiles;

		public IReadOnlyList<AvailablePhoneNumber> Results { get; init; } = Array.Empty<AvailablePhoneNumber>();

		/// <summary>Localized outcome of the last action (success), or null.</summary>
		public string Message { get; init; }

		/// <summary>Localized refusal of the last action, or null.</summary>
		public string Error { get; init; }

		/// <summary>The member is the department's managing member, the only one who may buy the add-on (set only while it can be bought).</summary>
		public bool IsOwner { get; init; }

		/// <summary>The Billing API's offer for the managing member (provider, price, whether checkout is open); null when not asked or unavailable.</summary>
		public DedicatedNumberBillingStatus Billing { get; init; }

		/// <summary>"complete" or "cancelled" when a checkout started on this page came back here; otherwise null.</summary>
		public string CheckoutResult { get; init; }

		/// <summary>How many times the page has refreshed itself waiting for the payment to activate the add-on.</summary>
		public int CheckoutAttempt { get; init; }

		/// <summary>A checkout came back paid but the provider's webhook has not activated the add-on yet.</summary>
		public bool AwaitingActivation => CheckoutResult == "complete" && !Access.HasActiveAddon;

		/// <summary>The managing member can start a checkout right here.</summary>
		public bool CanBuyHere => IsOwner && Billing != null && Billing.CheckoutAvailable && !Billing.Active && !Access.HasActiveAddon && !AwaitingActivation;

		/// <summary>The department holds no number in any live state, so it may search for one.</summary>
		public bool CanSearch => Access.HasActiveAddon && Active == null && Lapsed == null && Provisioning == null;

		/// <summary>The add-on is still paid, so the stop came from the plan (it is no longer an active paid entity plan).</summary>
		public bool PlanStopped => Access.HasActiveAddon && !Access.HasBillingRelationship;

		/// <summary>The allowed countries that take the US/Canada filters (state / province, postal code, area code, near).</summary>
		public IReadOnlyList<string> NanpCountries => (Access.AllowedCountries ?? Array.Empty<string>()).Where(DedicatedNumberSearch.IsNanp).ToList();

		/// <summary>The configured days-before-release warning stages, largest first, with their flags.</summary>
		public IReadOnlyList<(DedicatedNumberReleaseWarnings Stage, int Days)> WarningStages =>
			DedicatedNumberReleaseSchedule.DayStages(DedicatedNumberAddonConfig.ReleaseWarningDays);

		/// <summary>A date in the department's time zone, in the viewer's language.</summary>
		public string LocalDate(DateTime utc) => (Department == null ? utc : utc.TimeConverter(Department)).ToString("D", CultureInfo.CurrentCulture);

		/// <summary>A date and time in the department's time zone, in the viewer's language.</summary>
		public string LocalDateTime(DateTime utc) => (Department == null ? utc : utc.TimeConverter(Department)).ToString("f", CultureInfo.CurrentCulture);

		/// <summary>The country's name in the viewer's language, else the code.</summary>
		public static string CountryName(string code)
		{
			if (string.IsNullOrWhiteSpace(code))
				return "";
			try
			{
				return new RegionInfo(code).DisplayName;
			}
			catch (ArgumentException)
			{
				return code;
			}
		}
	}
}
