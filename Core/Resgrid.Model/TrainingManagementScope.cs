using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model
{
	/// <summary>
	/// Which of a department's trainings one member may manage (edit, see the answer key and the report, reset a member,
	/// delete) and which they may open. Built once per request by IAuthorizationService.GetTrainingManagementScopeAsync
	/// and then asked about each training, so a list of trainings costs one permission lookup, not one per row.
	/// </summary>
	public class TrainingManagementScope
	{
		public string UserId { get; set; }

		public int DepartmentId { get; set; }

		/// <summary>Department admins, and members of the roles the Create Training permission names: every training.</summary>
		public bool ManagesAll { get; set; }

		/// <summary>The member may create trainings, and so manages the ones they created.</summary>
		public bool ManagesOwn { get; set; }

		/// <summary>
		/// For a group admin under "department and group admins": the members of the group they administer. They manage a
		/// training whose assignees are all in this set. Null when the member administers no group.
		/// </summary>
		public HashSet<string> AdministeredMemberUserIds { get; set; }

		/// <summary>A scope that manages nothing; the member still opens the trainings assigned to them.</summary>
		public static TrainingManagementScope None(string userId, int departmentId)
		{
			return new TrainingManagementScope { UserId = userId, DepartmentId = departmentId };
		}

		/// <summary>True when the member may edit, report on, reset and delete the training.</summary>
		public bool CanManage(Training training)
		{
			if (training == null || string.IsNullOrWhiteSpace(UserId) || training.DepartmentId != DepartmentId)
				return false;

			if (ManagesAll)
				return true;

			if (ManagesOwn && string.Equals(training.CreatedByUserId, UserId, StringComparison.OrdinalIgnoreCase))
				return true;

			if (AdministeredMemberUserIds != null && training.Users != null)
			{
				var assignees = training.Users.Where(x => x != null && !string.IsNullOrWhiteSpace(x.UserId)).ToList();

				return assignees.Count > 0 && assignees.All(x => AdministeredMemberUserIds.Contains(x.UserId));
			}

			return false;
		}

		/// <summary>True when the training is assigned to the member.</summary>
		public bool IsAssigned(Training training)
		{
			return training?.Users != null && !string.IsNullOrWhiteSpace(UserId) && training.DepartmentId == DepartmentId &&
				training.Users.Any(x => x != null && string.Equals(x.UserId, UserId, StringComparison.OrdinalIgnoreCase));
		}

		/// <summary>
		/// True when the member may read the training and its attachments: it is assigned to them, they manage it, or the
		/// training is open to all members of the department.
		/// </summary>
		public bool CanOpen(Training training)
		{
			if (IsAssigned(training) || CanManage(training))
				return true;

			return training != null && training.OpenToAllMembers && !string.IsNullOrWhiteSpace(UserId) && training.DepartmentId == DepartmentId;
		}

		/// <summary>
		/// True when the member may see the quiz: assignees, who answer it, and managers. A training that is merely open to
		/// all members shows its content, not its quiz, to people it was not assigned to (their answers are not recorded).
		/// </summary>
		public bool CanOpenQuiz(Training training)
		{
			return IsAssigned(training) || CanManage(training);
		}
	}
}
