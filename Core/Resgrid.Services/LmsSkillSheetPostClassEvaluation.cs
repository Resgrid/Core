using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Lms;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// The post-class skill-sheet evaluation (§13 T4 items 1 and 2): at sign-off a session whose post-class evaluation is a skill sheet
	/// (PostClassEvaluationRef = the sheet id) opens one crew evaluation of that sheet for the attendees, with the signing instructor as the
	/// evaluator. The evaluation carries the class's transcript session key, so the skill record and the class record count once (R-T2).
	/// </summary>
	public class LmsSkillSheetPostClassEvaluation : ILmsPostClassEvaluation
	{
		private readonly ILmsPostClassEvaluationHook _skills;

		public LmsSkillSheetPostClassEvaluation(ILmsPostClassEvaluationHook skills)
		{
			_skills = skills;
		}

		public LmsClassPostEvaluationKinds Kind => LmsClassPostEvaluationKinds.SkillSheet;

		public async Task<int> StartAsync(LmsActor signer, LmsClassSession session, IReadOnlyCollection<string> attendeeUserIds, CancellationToken cancellationToken = default)
		{
			if (signer == null || session == null || attendeeUserIds == null || attendeeUserIds.Count == 0
				|| !int.TryParse(session.PostClassEvaluationRef, NumberStyles.None, CultureInfo.InvariantCulture, out var sheetId) || sheetId <= 0)
				return 0;

			var members = attendeeUserIds.Where(u => !string.IsNullOrWhiteSpace(u)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			if (members.Count == 0)
				return 0;

			var evaluation = await _skills.StartPostClassEvaluationAsync(new LmsPostClassEvaluationRequest
			{
				DepartmentId = session.DepartmentId,
				ClassSessionRef = LmsClassRules.SessionKey(session),
				LmsSkillSheetId = sheetId,
				EvaluatorUserId = signer.UserId,
				MemberUserIds = members,
				RequestedByUserId = signer.UserId,
				Location = session.Location
			}, cancellationToken);

			return evaluation == null ? 0 : members.Count;
		}
	}
}
