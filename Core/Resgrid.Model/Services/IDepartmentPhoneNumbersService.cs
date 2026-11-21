using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Dedicated department phone numbers (M0323, PlanAddonTypes.DedicatedNumber). Implemented by workstream D; intake (C) reads
	/// <see cref="GetByNumberAsync"/> to route inbound SMS and voice.
	/// </summary>
	public interface IDepartmentPhoneNumbersService
	{
		/// <summary>The department's Active number, else null.</summary>
		Task<DepartmentPhoneNumber> GetActiveForDepartmentAsync(int departmentId);

		/// <summary>The department's numbers of any status, newest first.</summary>
		Task<List<DepartmentPhoneNumber>> GetAllForDepartmentAsync(int departmentId);

		/// <summary>The number (any department) that is Active or Lapsed with this E.164 value, else null.</summary>
		Task<DepartmentPhoneNumber> GetByNumberAsync(string phoneNumber);

		/// <summary>Numbers the carrier offers in the country (must be allowed for the department), optionally near an area code or containing digits.</summary>
		Task<List<AvailablePhoneNumber>> SearchAvailableAsync(int departmentId, string countryCode, string areaCode, string contains,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Plan §8 D13: numbers the carrier offers in an allowed country, by state / province, city, postal code, area code, digits or near
		/// the department's map center / address (US/CA filters are dropped elsewhere). Refuses with <see cref="DedicatedNumberException"/>
		/// (AddonRequired, CountryNotAllowed, InvalidNumber for bad input, DepartmentLocationUnknown, CarrierUnavailable).
		/// </summary>
		Task<List<AvailablePhoneNumber>> SearchAvailableAsync(int departmentId, DedicatedNumberSearch search, CancellationToken cancellationToken = default);

		/// <summary>
		/// Plan §8 D12: tells the department's active admins, right away, that a cancellation made in Resgrid will stop it paying for its
		/// dedicated number: when the add-on (or plan) ends, when texts and calls stop and when the number is released. Only when the
		/// department holds an Active or Lapsed number; an end within the hour is left to the lifecycle's "payment stopped" warning.
		/// True when at least one admin was reached. Never throws for a delivery failure.
		/// </summary>
		Task<bool> NotifyCancellationScheduledAsync(int departmentId, DedicatedNumberCancellations cancelled, DateTime? endsOnUtc,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Buys the number for the department (requires the add-on; one Active number per department), points its SMS and voice
		/// webhooks at Resgrid, ensures the SMS + Voice intake channels and mirrors it into TextToCallNumber.
		/// </summary>
		Task<DepartmentPhoneNumber> ProvisionAsync(int departmentId, string phoneNumber, string userId, CancellationToken cancellationToken = default);

		/// <summary>Turns SMS / voice intake on the department's Active number on or off.</summary>
		Task<DepartmentPhoneNumber> UpdateIntakeOptionsAsync(int departmentId, bool smsIntakeEnabled, bool voiceIntakeEnabled, string userId,
			CancellationToken cancellationToken = default);

		/// <summary>Marks the department's Active number Lapsed (add-on ended) with a release date; intake stops.</summary>
		Task MarkLapsedAsync(int departmentId, DateTime releaseAfterUtc, CancellationToken cancellationToken = default);

		/// <summary>Returns the number to the carrier and marks it Released (department deletion, cancellation after grace).</summary>
		Task<bool> ReleaseAsync(string departmentPhoneNumberId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Worker 80 (plan §8 D10/D12): for each department holding a number, records T0 when it is first seen to have stopped paying,
		/// warns its admins (stopped, then before release), lapses the number at T0 + LapseDelayHours, releases it at T0 + ReleaseGraceDays,
		/// and restores it when payment resumes before release. Unknown billing changes nothing. Returns numbers whose status changed.
		/// </summary>
		Task<int> ProcessLifecycleAsync(DateTime nowUtc, CancellationToken cancellationToken = default);

		/// <summary>
		/// Puts the department's Lapsed number back in service once the add-on is active again (before it was released): Active, release
		/// date cleared, intake channels re-enabled and TextToCallNumber restored. Null when there is no Lapsed number to restore.
		/// </summary>
		Task<DepartmentPhoneNumber> ReactivateAsync(int departmentId, string userId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Department deletion: returns numbers read before the department's rows were purged to the carrier (best effort, logged).
		/// Only held numbers with a carrier id are released. Returns how many the carrier released.
		/// </summary>
		Task<int> ReleaseAtCarrierAsync(IReadOnlyCollection<DepartmentPhoneNumber> numbers, CancellationToken cancellationToken = default);
	}

	/// <summary>Who may buy / holds the Dedicated Number add-on (workstream D).</summary>
	public interface IDedicatedNumberAccessService
	{
		/// <summary>The department's access: billing relationship (active paid entity plan), offer, active add-on, provider, countries.</summary>
		Task<DedicatedNumberAccess> GetAccessAsync(int departmentId);

		/// <summary>True when the department holds an active Dedicated Number add-on period (uncached Billing API read).</summary>
		Task<bool> HasActiveAddonAsync(int departmentId);

		/// <summary>
		/// The add-on state for the number lifecycle: true active, false known inactive, null unknown (billing not configured, the catalog
		/// row missing or the Billing API unreachable). The lifecycle never lapses or releases a number on null.
		/// </summary>
		Task<bool?> GetActiveAddonStateAsync(int departmentId);

		/// <summary>
		/// Plan §8 D10, for the number lifecycle: the add-on state and, only while the add-on is active, whether the plan is an active paid
		/// entity plan. A plan that cannot be read is unknown, never "stopped".
		/// </summary>
		Task<DedicatedNumberPaymentState> GetPaymentStateAsync(int departmentId);
	}
}
