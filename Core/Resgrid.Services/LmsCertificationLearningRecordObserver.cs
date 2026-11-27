using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Lms;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Round 9A: the LMS certification engine's ear on the transcript (the baseline <see cref="ILearningRecordObserver"/> seam, §17.3).
	/// <list type="bullet">
	/// <item>Voided records: each member's live issuances that rest on them are re-evaluated (rolled back unless another completion still
	/// covers them). This runs whatever the add-on state: it only ever undoes what the engine itself did.</item>
	/// <item>Records that now count: when the department has an active requirement set (a cheap read, before any billing call) and the LMS
	/// add-on is active (one gate read per write, never in a sweep), the members' sets are evaluated and issue or renew when complete.</item>
	/// </list>
	/// External learners (synthetic "xl-" ids) hold no personnel certifications and are skipped.
	/// </summary>
	public class LmsCertificationLearningRecordObserver : ILearningRecordObserver
	{
		private readonly ILmsCertificationIssuanceService _issuance;
		private readonly ILmsCertificationRequirementService _requirements;
		private readonly ILmsCertificationRequirementSetsRepository _sets;
		private readonly ILmsGateService _gate;

		public LmsCertificationLearningRecordObserver(ILmsCertificationIssuanceService issuance, ILmsCertificationRequirementService requirements,
			ILmsCertificationRequirementSetsRepository sets, ILmsGateService gate)
		{
			_issuance = issuance;
			_requirements = requirements;
			_sets = sets;
			_gate = gate;
		}

		public async Task OnLearningRecordsChangedAsync(LearningRecordChange change, CancellationToken cancellationToken = default)
		{
			if (change == null || change.DepartmentId <= 0 || change.IsEmpty)
				return;

			foreach (var member in (change.Voided ?? new List<LearningRecord>()).Where(r => r?.UserId != null && !LmsExternalLearnerIds.IsExternal(r.UserId))
				         .GroupBy(r => r.UserId, StringComparer.OrdinalIgnoreCase))
				await _issuance.OnRecordsVoidedAsync(change.DepartmentId, member.Key, member.Select(r => r.LearningRecordId).ToList(), change.ActingUserId, cancellationToken);

			var written = (change.Written ?? new List<LearningRecord>()).Where(r => r?.UserId != null && !LmsExternalLearnerIds.IsExternal(r.UserId))
				.Select(r => r.UserId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			if (written.Count == 0 || !await _sets.HasActiveSetsAsync(change.DepartmentId))
				return;
			var gate = await _gate.GetGateAsync(change.DepartmentId);
			if (gate == null || !gate.CanAuthor)
				return;
			await _requirements.EvaluateMembersAsync(change.DepartmentId, written, change.ActingUserId, DateTime.UtcNow, cancellationToken);
		}
	}
}
