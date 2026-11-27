using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Model.Lms;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Round 9A: the member's certification issuance ledger in their GDPR export ("certification_issuances.json"): every automatic issue or
	/// renewal of their certifications (the source, the dates before and after, the learning records behind it) and any void that took one back.
	/// Registered as an <see cref="IGdprExportSection"/> so the baseline export never references LMS types (§17.3). Own rows only.
	/// </summary>
	public class LmsCertificationIssuanceGdprSection : IGdprExportSection
	{
		private const int MaxRows = 500;
		private readonly ILmsCertificationIssuancesRepository _issuances;

		public LmsCertificationIssuanceGdprSection(ILmsCertificationIssuancesRepository issuances)
		{
			_issuances = issuances;
		}

		public string EntryName => "certification_issuances.json";

		public async Task<object> BuildAsync(string userId, int departmentId)
		{
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(userId))
				return new List<object>();
			return (await _issuances.GetRecentForUserAsync(departmentId, userId, MaxRows) ?? new List<LmsCertificationIssuance>())
				.Where(i => i.DepartmentId == departmentId && string.Equals(i.UserId, userId, StringComparison.OrdinalIgnoreCase))
				.Select(i => new
				{
					i.LmsCertificationIssuanceId,
					i.PersonnelCertificationId,
					i.DepartmentCertificationTypeId,
					Source = ((LmsCertificationIssueSources)i.Source).ToString(),
					i.SourceRefId,
					Action = ((LmsCertificationIssueActions)i.Action).ToString(),
					i.PreviousExpiresOn,
					i.NewExpiresOn,
					i.BasisRecordIds,
					i.CompletedOn,
					i.IssuedOn,
					State = ((LmsCertificationIssuanceStates)i.State).ToString(),
					i.RolledBackOn,
					i.RolledBackByUserId,
					i.RollbackReason
				}).ToList();
		}
	}
}
