using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>BBP / infectious-disease exposure cases (§13b WL1, M0372).</summary>
	public interface IRmsExposureIncidentsRepository : IRepository<RmsExposureIncident>
	{
		Task<RmsExposureIncident> GetByIdForDepartmentAsync(int departmentId, string incidentId);

		/// <summary>Every case of one member, newest first.</summary>
		Task<IEnumerable<RmsExposureIncident>> GetForUserAsync(int departmentId, string userId);

		/// <summary>The department's cases, newest first; open ones only unless <paramref name="includeClosed"/>.</summary>
		Task<IEnumerable<RmsExposureIncident>> GetForDepartmentAsync(int departmentId, bool includeClosed);

		/// <summary>The cases that follow one WL0 exposure entry.</summary>
		Task<IEnumerable<RmsExposureIncident>> GetForExposureAsync(int departmentId, string responderExposureId);

		/// <summary>Cases across departments whose Ryan White answer is overdue and whose officers were not told yet (worker 91).</summary>
		Task<IEnumerable<RmsExposureIncident>> GetOverdueNotificationsAsync(DateTime utcNow, int take);

		/// <summary>Stamps the overdue notice, only while nobody else has: the claim that keeps two runs from sending it twice.</summary>
		Task<bool> ClaimOverdueNoticeAsync(int departmentId, string incidentId, DateTime sentOn, CancellationToken cancellationToken = default);

		/// <summary>Departments holding a case that happened before <paramref name="cutoff"/> (retention candidates).</summary>
		Task<IEnumerable<int>> GetDepartmentsWithCasesBeforeAsync(DateTime cutoff, int take);

		Task<IEnumerable<RmsExposureIncident>> GetBeforeAsync(int departmentId, DateTime cutoff, int take);

		Task<int> DeleteForDepartmentAsync(int departmentId, string incidentId, CancellationToken cancellationToken = default);
	}

	/// <summary>Member injuries with the OSHA 300/301 facts (§13b WL1, M0373).</summary>
	public interface IRmsMemberInjuriesRepository : IRepository<RmsMemberInjury>
	{
		Task<RmsMemberInjury> GetByIdForDepartmentAsync(int departmentId, string injuryId);

		Task<IEnumerable<RmsMemberInjury>> GetForUserAsync(int departmentId, string userId);

		/// <summary>The department's open injuries (any duty status), oldest first.</summary>
		Task<IEnumerable<RmsMemberInjury>> GetOpenForDepartmentAsync(int departmentId);

		/// <summary>Every injury of one OSHA year, by case number.</summary>
		Task<IEnumerable<RmsMemberInjury>> GetForYearAsync(int departmentId, int year);

		/// <summary>The highest case number used in the year (0 when none).</summary>
		Task<int> GetMaxCaseNumberAsync(int departmentId, int year);

		/// <summary>Open injuries across departments whose expected return passed and whose review was not asked for since (worker 91).</summary>
		Task<IEnumerable<RmsMemberInjury>> GetDueReturnReviewsAsync(DateTime utcNow, int take);

		/// <summary>Stamps the review notice while it still holds <paramref name="expected"/> (the claim).</summary>
		Task<bool> ClaimReturnReviewAsync(int departmentId, string injuryId, DateTime? expected, DateTime sentOn, CancellationToken cancellationToken = default);

		/// <summary>Departments holding an injury of a year before <paramref name="beforeYear"/> (retention candidates).</summary>
		Task<IEnumerable<int>> GetDepartmentsWithInjuriesBeforeYearAsync(int beforeYear, int take);

		Task<IEnumerable<RmsMemberInjury>> GetBeforeYearAsync(int departmentId, int beforeYear, int take);

		Task<int> DeleteForDepartmentAsync(int departmentId, string injuryId, CancellationToken cancellationToken = default);
	}

	/// <summary>The duty status history of injuries (§13b WL1, M0373).</summary>
	public interface IRmsInjuryDutyPeriodsRepository : IRepository<RmsInjuryDutyPeriod>
	{
		Task<IEnumerable<RmsInjuryDutyPeriod>> GetForInjuryAsync(int departmentId, string injuryId);

		Task<IEnumerable<RmsInjuryDutyPeriod>> GetForInjuriesAsync(int departmentId, IEnumerable<string> injuryIds);

		Task<int> DeleteForInjuryAsync(int departmentId, string injuryId, CancellationToken cancellationToken = default);
	}

	/// <summary>NFPA 1582 medical clearances (§13b WL1, M0374).</summary>
	public interface IRmsMedicalClearancesRepository : IRepository<RmsMedicalClearance>
	{
		Task<RmsMedicalClearance> GetByIdForDepartmentAsync(int departmentId, string clearanceId);

		Task<IEnumerable<RmsMedicalClearance>> GetForUserAsync(int departmentId, string userId);

		/// <summary>Every clearance of the department that was not voided (the latest per member is the current one).</summary>
		Task<IEnumerable<RmsMedicalClearance>> GetActiveForDepartmentAsync(int departmentId);

		Task<IEnumerable<int>> GetDepartmentsWithClearancesBeforeAsync(DateTime cutoff, int take);

		Task<IEnumerable<RmsMedicalClearance>> GetBeforeAsync(int departmentId, DateTime cutoff, int take);

		Task<int> DeleteForDepartmentAsync(int departmentId, string clearanceId, CancellationToken cancellationToken = default);
	}
}
