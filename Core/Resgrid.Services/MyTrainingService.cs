using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Certifications;
using Resgrid.Model.Helpers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// The member's My Training page (§18.3), shared by the web page and the v4 API: overdue, due-soon and in-progress
	/// assignments, recent completions, the catalog of open trainings (self-enrol when allowed) and the member's
	/// certifications that are due or pinned (Vector "To Do" parity). Opening it also catches up any dynamic assignment
	/// the member should have (a missed membership-change enqueue costs nothing more than this page view).
	/// </summary>
	public class MyTrainingService : IMyTrainingService
	{
		public const int RecentCompletionDays = 90;

		private readonly ITrainingService _trainingService;
		private readonly ITrainingAssignmentService _assignmentService;
		private readonly ICertificationService _certificationService;
		private readonly IMemberPinnedItemsRepository _pinsRepository;
		private readonly IMemberLeaveService _leaveService;

		public MyTrainingService(ITrainingService trainingService, ITrainingAssignmentService assignmentService, ICertificationService certificationService,
			IMemberPinnedItemsRepository pinsRepository, IMemberLeaveService leaveService)
		{
			_trainingService = trainingService;
			_assignmentService = assignmentService;
			_certificationService = certificationService;
			_pinsRepository = pinsRepository;
			_leaveService = leaveService;
		}

		public async Task<MyTrainingOverview> GetOverviewAsync(int departmentId, string userId, DateTime utcNow)
		{
			var overview = new MyTrainingOverview();
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(userId))
				return overview;

			try
			{
				await _assignmentService.ReevaluateMemberAsync(departmentId, userId, utcNow);
			}
			catch (Exception ex)
			{
				// The page still shows what is assigned; the worker re-evaluates on the next membership change.
				Framework.Logging.LogException(ex, $"My Training could not re-evaluate audiences for department {departmentId}");
			}

			var assignments = await _trainingService.GetAssignmentsForUserAsync(departmentId, userId, utcNow) ?? new List<TrainingAssignmentStatus>();
			foreach (var item in assignments)
			{
				switch (item.State)
				{
					case TrainingAssignmentStates.Overdue:
						overview.Overdue.Add(item);
						break;
					case TrainingAssignmentStates.Complete:
						if (item.Assignment.CompletedOn.HasValue && item.Assignment.CompletedOn.Value >= utcNow.AddDays(-RecentCompletionDays))
							overview.RecentCompletions.Add(item);
						break;
					case TrainingAssignmentStates.Scheduled:
					case TrainingAssignmentStates.Removed:
						break;
					case TrainingAssignmentStates.InProgress:
					case TrainingAssignmentStates.Failed:
						if (TrainingDueDates.IsDueWithin(item.DueUtc, utcNow, TimeSpan.FromDays(MyTrainingOverview.DueSoonDays)))
							overview.DueSoon.Add(item);
						else
							overview.InProgress.Add(item);
						break;
					default:
						if (TrainingDueDates.IsDueWithin(item.DueUtc, utcNow, TimeSpan.FromDays(MyTrainingOverview.DueSoonDays)))
							overview.DueSoon.Add(item);
						else
							overview.Other.Add(item);
						break;
				}
			}

			overview.RecentCompletions = overview.RecentCompletions.OrderByDescending(x => x.Assignment.CompletedOn).ToList();

			var assignedIds = new HashSet<int>(assignments.Where(a => !a.Assignment.RemovedOn.HasValue).Select(a => a.Training.TrainingId));
			overview.Catalog = (await _trainingService.GetAllTrainingsForDepartmentAsync(departmentId) ?? new List<Training>())
				.Where(t => t.OpenToAllMembers && !t.IsArchived && t.HasStarted(utcNow) && !assignedIds.Contains(t.TrainingId))
				.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
				.ToList();

			overview.OnLeave = TrainingDueDates.IsOnLeave(utcNow, await _leaveService.GetForMemberAsync(departmentId, userId));
			overview.Certifications = await CertificationsAsync(departmentId, userId, utcNow);
			return overview;
		}

		private async Task<List<MyTrainingCertification>> CertificationsAsync(int departmentId, string userId, DateTime utcNow)
		{
			var pinned = new HashSet<string>((await _pinsRepository.GetForMemberAsync(departmentId, userId) ?? new List<MemberPinnedItem>())
				.Where(p => p.Kind == (int)MemberPinnedItemKinds.Certification).Select(p => p.RefId), StringComparer.OrdinalIgnoreCase);

			var list = new List<MyTrainingCertification>();
			foreach (var certification in await _certificationService.GetCertificationsByUserIdAsync(userId) ?? new List<PersonnelCertification>())
			{
				if (certification == null || certification.DepartmentId != departmentId || certification.IsDeleted ||
					certification.Status == (int)PersonnelCertificationStatuses.Revoked)
					continue;

				var isPinned = pinned.Contains(certification.PersonnelCertificationId.ToString(CultureInfo.InvariantCulture));
				var expired = certification.ExpiresOn.HasValue && certification.ExpiresOn.Value < utcNow;
				var dueSoon = certification.ExpiresOn.HasValue && !expired && certification.ExpiresOn.Value <= utcNow.AddDays(MyTrainingOverview.CertificationDueDays);
				if (!isPinned && !expired && !dueSoon)
					continue;

				list.Add(new MyTrainingCertification
				{
					Certification = certification,
					Name = certification.Name,
					ExpiresOn = certification.ExpiresOn,
					Pinned = isPinned,
					IsExpired = expired,
					DueSoon = dueSoon,
					DaysLeft = certification.ExpiresOn.HasValue ? (int)Math.Floor((certification.ExpiresOn.Value - utcNow).TotalDays) : (int?)null
				});
			}

			return list.OrderByDescending(c => c.Pinned).ThenBy(c => c.ExpiresOn ?? DateTime.MaxValue).ToList();
		}

		public async Task<bool> PinCertificationAsync(int departmentId, string userId, int personnelCertificationId, bool pinned, CancellationToken cancellationToken = default)
		{
			var certification = await _certificationService.GetCertificationByIdAsync(personnelCertificationId);
			if (certification == null || certification.DepartmentId != departmentId || certification.IsDeleted ||
				!string.Equals(certification.UserId, userId, StringComparison.OrdinalIgnoreCase))
				return false;

			var refId = personnelCertificationId.ToString(CultureInfo.InvariantCulture);
			await _pinsRepository.DeleteForMemberAsync(departmentId, userId, (int)MemberPinnedItemKinds.Certification, refId, cancellationToken);

			if (pinned)
				await _pinsRepository.InsertAsync(new MemberPinnedItem
				{
					DepartmentId = departmentId,
					UserId = certification.UserId,
					Kind = (int)MemberPinnedItemKinds.Certification,
					RefId = refId,
					PinnedOn = DateTime.UtcNow
				}, cancellationToken, true);

			return true;
		}
	}
}
