using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model
{
	/// <summary>
	/// One training right of one member (author, assign-and-report or delete): every training of the department, the ones
	/// they created, or - for a group admin under "department and group admins" - the trainings assigned only to members
	/// of the group they administer.
	/// </summary>
	public class TrainingRightScope
	{
		/// <summary>Department admins, and members of the roles the permission names: every training.</summary>
		public bool ManagesAll { get; set; }

		/// <summary>The member holds the right for the trainings they created.</summary>
		public bool ManagesOwn { get; set; }

		/// <summary>A group admin's members: trainings whose assignees are all in this set. Null when not a group admin.</summary>
		public HashSet<string> AdministeredMemberUserIds { get; set; }

		public bool HoldsAny => ManagesAll || ManagesOwn || AdministeredMemberUserIds != null;

		public bool Covers(Training training, string userId)
		{
			if (training == null || string.IsNullOrWhiteSpace(userId))
				return false;

			if (ManagesAll)
				return true;

			if (ManagesOwn && string.Equals(training.CreatedByUserId, userId, StringComparison.OrdinalIgnoreCase))
				return true;

			if (AdministeredMemberUserIds != null && training.Users != null)
			{
				var assignees = training.Users.Where(x => x != null && !string.IsNullOrWhiteSpace(x.UserId) && !x.RemovedOn.HasValue).ToList();

				return assignees.Count > 0 && assignees.All(x => AdministeredMemberUserIds.Contains(x.UserId));
			}

			return false;
		}

		/// <summary>True when the member may assign this one member (a group admin only their own group's members).</summary>
		public bool CoversMember(string memberUserId)
		{
			if (ManagesAll)
				return true;
			if (AdministeredMemberUserIds != null)
				return !string.IsNullOrWhiteSpace(memberUserId) && AdministeredMemberUserIds.Contains(memberUserId);
			return ManagesOwn;
		}
	}

	/// <summary>
	/// Which of a department's trainings one member may author (edit the content and the quiz, which shows the answer key),
	/// assign and report on (assignees, audiences, due dates, extensions, exemptions, resets, the report and attempt
	/// history), and archive or delete; and which they may open. The three rights come from the Create Training (author),
	/// Assign Training (126) and Delete Training (127) permissions. Built once per request by
	/// IAuthorizationService.GetTrainingManagementScopeAsync and then asked about each training, so a list costs one
	/// permission lookup, not one per row.
	/// </summary>
	public class TrainingManagementScope
	{
		public string UserId { get; set; }

		public int DepartmentId { get; set; }

		/// <summary>The author right (Create Training). The three shortcuts below read and write it.</summary>
		public TrainingRightScope Author { get; set; } = new TrainingRightScope();

		/// <summary>The assign-and-report right (Assign Training). Null follows the author right.</summary>
		public TrainingRightScope Assign { get; set; }

		/// <summary>The archive and delete right (Delete Training). Null follows the author right.</summary>
		public TrainingRightScope Delete { get; set; }

		/// <summary>
		/// Round 7B: Record Training Completions (135). Its own row (no row: department admins only); ManagesAll for department admins and
		/// the roles it names, a group admin's own members otherwise. Never null.
		/// </summary>
		public TrainingRightScope Record { get; set; } = new TrainingRightScope();

		/// <summary>Round 7B: may record a completion for this member (never for themselves; a group admin only for their own group's members).</summary>
		public bool CanRecordFor(string memberUserId)
		{
			if (string.IsNullOrWhiteSpace(memberUserId) || Record == null || string.Equals(memberUserId.Trim(), UserId, StringComparison.OrdinalIgnoreCase))
				return false;
			if (Record.ManagesAll)
				return true;
			return Record.AdministeredMemberUserIds != null && Record.AdministeredMemberUserIds.Contains(memberUserId.Trim());
		}

		/// <summary>Round 7B: holds the recording right at all (the record panels and actions show).</summary>
		public bool CanRecordCompletions => Record != null && (Record.ManagesAll || Record.AdministeredMemberUserIds != null);

		/// <summary>
		/// Round 9B: reviews members' training hours submissions at all (the Training reviews section shows): the Record Training Completions
		/// right (135, training captains and instructors) or T1's record reviewers (Assign Training department-wide, group admins). Whose
		/// submissions exactly is ITrainingSubmissionService.GetReviewerScopeAsync.
		/// </summary>
		public bool CanReviewSubmissions => CanRecordCompletions || AssignRight.ManagesAll || AssignRight.AdministeredMemberUserIds != null;

		/// <summary>Author right: every training.</summary>
		public bool ManagesAll { get => Author.ManagesAll; set => Author.ManagesAll = value; }

		/// <summary>Author right: the member may create trainings, and so authors the ones they created.</summary>
		public bool ManagesOwn { get => Author.ManagesOwn; set => Author.ManagesOwn = value; }

		/// <summary>Author right of a group admin: the members of the group they administer.</summary>
		public HashSet<string> AdministeredMemberUserIds { get => Author.AdministeredMemberUserIds; set => Author.AdministeredMemberUserIds = value; }

		public TrainingRightScope AssignRight => Assign ?? Author;

		public TrainingRightScope DeleteRight => Delete ?? Author;

		/// <summary>A scope that manages nothing; the member still opens the trainings assigned to them.</summary>
		public static TrainingManagementScope None(string userId, int departmentId)
		{
			return new TrainingManagementScope { UserId = userId, DepartmentId = departmentId };
		}

		private bool InDepartment(Training training) =>
			training != null && !string.IsNullOrWhiteSpace(UserId) && training.DepartmentId == DepartmentId;

		/// <summary>May edit the training's content and quiz (the edit page shows the answer key).</summary>
		public bool CanAuthor(Training training) => InDepartment(training) && Author.Covers(training, UserId);

		/// <summary>May change who is assigned and when it is due, and read the report and attempt history.</summary>
		public bool CanAssign(Training training) => InDepartment(training) && AssignRight.Covers(training, UserId);

		/// <summary>May archive, restore or delete the training.</summary>
		public bool CanDelete(Training training) => InDepartment(training) && DeleteRight.Covers(training, UserId);

		/// <summary>True when the member holds any training right for the training (it shows in their manager list).</summary>
		public bool CanManage(Training training) => CanAuthor(training) || CanAssign(training) || CanDelete(training);

		/// <summary>The member may create trainings at all.</summary>
		public bool CanCreate => Author.HoldsAny;

		/// <summary>The member holds at least one training right (the officer pages and KPI tiles).</summary>
		public bool IsManager => Author.HoldsAny || AssignRight.HoldsAny || DeleteRight.HoldsAny;

		/// <summary>
		/// Training T1: manages the department's training records - the external submission queue, categories, bulk
		/// categorize, backfill, history import, settings and the department-wide reports. The assign-and-report right (Assign
		/// Training, 126) held department-wide; no separate permission (131 stays unallocated).
		/// </summary>
		public bool CanManageRecords => AssignRight.ManagesAll;

		/// <summary>
		/// Training T1: may read this member's transcript and verify their records - department-wide record managers, and a
		/// group admin (training supervisor scope) for the members of the group they administer. Every member reads their own
		/// transcript; nobody verifies their own record (ILearningRecordService.VerifyAsync refuses it).
		/// </summary>
		public bool CanReviewRecordsOf(string memberUserId)
		{
			if (string.IsNullOrWhiteSpace(memberUserId))
				return false;
			if (AssignRight.ManagesAll)
				return true;
			return AssignRight.AdministeredMemberUserIds != null && AssignRight.AdministeredMemberUserIds.Contains(memberUserId);
		}

		/// <summary>True when the training is assigned to the member (removed assignees are not).</summary>
		public bool IsAssigned(Training training)
		{
			return training?.Users != null && !string.IsNullOrWhiteSpace(UserId) && training.DepartmentId == DepartmentId &&
				training.Users.Any(x => x != null && !x.RemovedOn.HasValue && string.Equals(x.UserId, UserId, StringComparison.OrdinalIgnoreCase));
		}

		/// <summary>The member's own assignment row, if any (removed rows included, for their completion).</summary>
		public TrainingUser AssignmentOf(Training training)
		{
			if (training?.Users == null || string.IsNullOrWhiteSpace(UserId) || training.DepartmentId != DepartmentId)
				return null;

			return training.Users.FirstOrDefault(x => x != null && string.Equals(x.UserId, UserId, StringComparison.OrdinalIgnoreCase));
		}

		/// <summary>
		/// True when the member may read the training and its attachments: they manage it, or it is assigned to them, or it
		/// is open to all members. An archived training is read only by whoever manages it; a training whose scheduled start
		/// is still ahead is hidden from its assignees and the catalog (they see it once it starts).
		/// </summary>
		public bool CanOpen(Training training) => CanOpen(training, DateTime.UtcNow);

		public bool CanOpen(Training training, DateTime utcNow)
		{
			if (CanManage(training))
				return true;

			if (!InDepartment(training) || training.IsArchived || !training.HasStarted(utcNow))
				return false;

			return IsAssigned(training) || training.OpenToAllMembers;
		}

		/// <summary>
		/// True when the member may see the quiz: assignees, who answer it, and its authors. A training that is merely open to
		/// all members shows its content, not its quiz, to people it was not assigned to (their answers are not recorded).
		/// </summary>
		public bool CanOpenQuiz(Training training) => CanOpenQuiz(training, DateTime.UtcNow);

		public bool CanOpenQuiz(Training training, DateTime utcNow)
		{
			if (CanAuthor(training) || CanAssign(training))
				return true;

			return InDepartment(training) && !training.IsArchived && training.HasStarted(utcNow) && IsAssigned(training);
		}
	}
}
