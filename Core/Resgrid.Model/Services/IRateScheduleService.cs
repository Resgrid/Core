using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Invoicing;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Contractor rate schedules (Workforce &amp; Business Operations plan, C4): the schedule graph (entries, bands,
	/// premiums, policy), cloning for contract renewals, the contract → profile → department-default resolution
	/// the billing engine relies on, multiplier prefill for band authoring and JSON import/export. Callers authorize.
	/// </summary>
	public interface IRateScheduleService
	{
		Task<List<RateSchedule>> GetSchedulesForDepartmentAsync(int departmentId, bool includeInactive = false);
		/// <summary>The schedule with its entries (each with bands) and premiums; null when missing or deleted.</summary>
		Task<RateSchedule> GetScheduleByIdAsync(string rateScheduleId, int departmentId, bool includeInactive = false);
		Task<RateSchedule> SaveScheduleAsync(RateSchedule schedule, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		Task<bool> DeleteScheduleAsync(string rateScheduleId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		/// <summary>A new schedule with copies of every entry, band and premium (contract renewals, next season).</summary>
		Task<RateSchedule> CloneScheduleAsync(string rateScheduleId, int departmentId, string newName, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);

		/// <summary>Saves an entry and replaces its bands with <see cref="RateScheduleEntry.Bands"/>.</summary>
		Task<RateScheduleEntry> SaveEntryAsync(RateScheduleEntry entry, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		Task<RateScheduleEntry> GetEntryByIdAsync(string rateScheduleEntryId, int departmentId);
		Task<bool> DeleteEntryAsync(string rateScheduleEntryId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		Task<RatePremium> SavePremiumAsync(RatePremium premium, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		Task<bool> DeletePremiumAsync(string ratePremiumId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);

		/// <summary>Contract schedule → contact's billing profile default → department default (the first active schedule). Null when the department has none.</summary>
		Task<RateSchedule> GetEffectiveScheduleForContactAsync(string contactId, int departmentId, string serviceContractId = null);

		/// <summary>Draft hourly bands from a base rate and overtime multipliers; stored explicit per decision 16.</summary>
		List<RateScheduleEntryBand> PrefillHourlyBands(decimal baseRate, decimal? standbyRate, decimal overtime1Multiplier, decimal? overtime1StartHours, decimal? overtime2Multiplier, decimal? overtime2StartHours);

		Task<string> ExportScheduleJsonAsync(string rateScheduleId, int departmentId);
		Task<RateSchedule> ImportScheduleJsonAsync(int departmentId, string json, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
	}
}
