using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Resgrid.Model
{
	/// <summary>
	/// Who may sign an "any eligible member" step (evaluator mode, plan §13 T4 item 2: "evaluator eligibility comes from a
	/// certification type or role"): a member qualifies by holding a valid certification of any listed type or by belonging to any
	/// listed personnel role. Stored on the step as JSON and checked again when the step is signed.
	/// </summary>
	public sealed class SignerEligibility
	{
		public List<int> CertificationTypeIds { get; set; } = new List<int>();

		public List<int> PersonnelRoleIds { get; set; } = new List<int>();

		/// <summary>S4 approval chains: named members who qualify (a named approver, a group's admins, a delegate or an escalation target).</summary>
		public List<string> UserIds { get; set; } = new List<string>();

		/// <summary>S4: any active department admin qualifies.</summary>
		public bool DepartmentAdmins { get; set; }

		/// <summary>S4: members who never qualify, whatever else matches (the submitter of an approval: separation of duties).</summary>
		public List<string> ExcludedUserIds { get; set; } = new List<string>();

		[JsonIgnore]
		public bool IsEmpty => (CertificationTypeIds == null || !CertificationTypeIds.Any(x => x > 0)) && (PersonnelRoleIds == null || !PersonnelRoleIds.Any(x => x > 0))
			&& (UserIds == null || !UserIds.Any(x => !string.IsNullOrWhiteSpace(x))) && !DepartmentAdmins;

		/// <summary>Whether the member is excluded by the rule (compared case-insensitively).</summary>
		public bool Excludes(string userId) =>
			!string.IsNullOrWhiteSpace(userId) && (ExcludedUserIds ?? new List<string>()).Any(x => string.Equals(x?.Trim(), userId.Trim(), StringComparison.OrdinalIgnoreCase));

		/// <summary>Whether the member is named by the rule (compared case-insensitively).</summary>
		public bool Names(string userId) =>
			!string.IsNullOrWhiteSpace(userId) && (UserIds ?? new List<string>()).Any(x => string.Equals(x?.Trim(), userId.Trim(), StringComparison.OrdinalIgnoreCase));

		/// <summary>
		/// The stored form (sorted, distinct ids), or null for an empty rule. The S4 members ("u", "a", "x") are written only when set, so
		/// an S3 rule stores exactly as before.
		/// </summary>
		public string ToJson()
		{
			if (IsEmpty)
				return null;
			var o = new JObject
			{
				["c"] = new JArray((CertificationTypeIds ?? new List<int>()).Where(x => x > 0).Distinct().OrderBy(x => x).Cast<object>().ToArray()),
				["r"] = new JArray((PersonnelRoleIds ?? new List<int>()).Where(x => x > 0).Distinct().OrderBy(x => x).Cast<object>().ToArray())
			};
			var users = Ids(UserIds);
			if (users.Length > 0)
				o["u"] = new JArray(users.Cast<object>().ToArray());
			if (DepartmentAdmins)
				o["a"] = 1;
			var excluded = Ids(ExcludedUserIds);
			if (excluded.Length > 0)
				o["x"] = new JArray(excluded.Cast<object>().ToArray());
			return o.ToString(Formatting.None);
		}

		private static string[] Ids(IEnumerable<string> ids) =>
			(ids ?? Enumerable.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase)
				.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();

		/// <summary>The rule stored on a step; null when there is none or it cannot be read (a step without a rule is not "any member").</summary>
		public static SignerEligibility Parse(string json)
		{
			if (string.IsNullOrWhiteSpace(json))
				return null;
			try
			{
				var o = JObject.Parse(json);
				var rule = new SignerEligibility
				{
					CertificationTypeIds = (o["c"] as JArray)?.Where(t => t.Type == JTokenType.Integer).Select(t => (int)t).Where(x => x > 0).Distinct().ToList() ?? new List<int>(),
					PersonnelRoleIds = (o["r"] as JArray)?.Where(t => t.Type == JTokenType.Integer).Select(t => (int)t).Where(x => x > 0).Distinct().ToList() ?? new List<int>(),
					UserIds = (o["u"] as JArray)?.Where(t => t.Type == JTokenType.String).Select(t => ((string)t).Trim()).Where(x => x.Length > 0).ToList() ?? new List<string>(),
					DepartmentAdmins = o["a"] != null && o["a"].Type == JTokenType.Integer && (int)o["a"] == 1,
					ExcludedUserIds = (o["x"] as JArray)?.Where(t => t.Type == JTokenType.String).Select(t => ((string)t).Trim()).Where(x => x.Length > 0).ToList() ?? new List<string>()
				};
				return rule.IsEmpty ? null : rule;
			}
			catch (JsonException)
			{
				return null;
			}
		}
	}

	/// <summary>One step of a multi-signer request (S3).</summary>
	public sealed class SignatureStepInput
	{
		public SignatureSignerKinds Kind { get; set; } = SignatureSignerKinds.Member;

		public SignatureSignerRoles Role { get; set; } = SignatureSignerRoles.Member;

		/// <summary>1-based. Ordered requests sign in ascending order; steps with the same order are a parallel group.</summary>
		public int Order { get; set; } = 1;

		/// <summary>A named member (member steps). Null with an <see cref="Eligibility"/> rule: any eligible member may sign.</summary>
		public string UserId { get; set; }

		public SignerEligibility Eligibility { get; set; }

		/// <summary>L1 or L2 for a member step (never below its role's floor); external steps are always L4.</summary>
		public SignatureLevels Level { get; set; } = SignatureLevels.StepUp;

		public string StepLabel { get; set; }

		// External signers (L4).
		public string ExternalName { get; set; }
		public string ExternalEmail { get; set; }
		public string ExternalPhone { get; set; }
		public SignatureSignerAuthMethods OtpChannel { get; set; } = SignatureSignerAuthMethods.EmailOtp;
	}

	/// <summary>A new multi-signer request (S3): ordered (sequential, with parallel groups) or all at once.</summary>
	public sealed class MultiSignerRequestInput
	{
		public int DepartmentId { get; set; }
		public SignatureSubjectKinds SubjectKind { get; set; }
		public string SubjectId { get; set; }
		public bool Ordered { get; set; } = true;
		/// <summary>How the request's external signers sign: by a Resgrid link (needs the owning add-on) or in person (free).</summary>
		public SignatureRequestChannels ExternalChannel { get; set; } = SignatureRequestChannels.InPerson;
		public List<SignatureStepInput> Steps { get; set; } = new List<SignatureStepInput>();
		public int ExpiresInDays { get; set; } = 14;
		public string Culture { get; set; }
		public string RequestedByUserId { get; set; }
		public string IpAddress { get; set; }
		public string UserAgent { get; set; }
	}

	/// <summary>
	/// Evaluator mode (S3 platform, consumed by T4): "evaluate a subject". The evaluator is a named member, any member the rule
	/// makes eligible, or an outside evaluator (L4; remote only where the LMS add-on is active). An optional certifying official
	/// signs after the evaluator. Every signature binds to the evaluation's content hash, which the subject's source computes.
	/// </summary>
	public sealed class EvaluationRequestInput
	{
		public int DepartmentId { get; set; }
		public SignatureSubjectKinds SubjectKind { get; set; } = SignatureSubjectKinds.SkillEvaluation;
		public string SubjectId { get; set; }
		/// <summary>Who may evaluate (certification types or roles). Required for a member evaluator.</summary>
		public SignerEligibility Eligibility { get; set; }
		/// <summary>A named evaluator (must be eligible); null with no outside evaluator: any eligible member may sign.</summary>
		public string EvaluatorUserId { get; set; }
		/// <summary>An outside evaluator (L4) instead of a member.</summary>
		public string ExternalEvaluatorName { get; set; }
		public string ExternalEvaluatorEmail { get; set; }
		public string ExternalEvaluatorPhone { get; set; }
		public SignatureRequestChannels ExternalChannel { get; set; } = SignatureRequestChannels.InPerson;
		public SignatureSignerAuthMethods OtpChannel { get; set; } = SignatureSignerAuthMethods.EmailOtp;
		/// <summary>An optional certifying official after the evaluator (a named member, or any member <see cref="CertifyingEligibility"/> allows).</summary>
		public string CertifyingOfficialUserId { get; set; }
		public SignerEligibility CertifyingEligibility { get; set; }
		public int ExpiresInDays { get; set; } = 30;
		public string Culture { get; set; }
		public string RequestedByUserId { get; set; }
		public string IpAddress { get; set; }
		public string UserAgent { get; set; }
	}

	/// <summary>A member signing their step (S3). The proof is built from server-side evidence only.</summary>
	public sealed class SignatureStepSignInput
	{
		public int DepartmentId { get; set; }
		public int SignatureRequestId { get; set; }
		/// <summary>The step; null signs the member's own step whose turn it is.</summary>
		public int? SignatureRequestSignerId { get; set; }
		public SignatureProof Proof { get; set; }
		/// <summary>The department requires a passkey for this step.</summary>
		public bool RequirePasskey { get; set; }
		/// <summary>The department step-up area (approval chains pass <c>Approvals</c>); null = signatures.</summary>
		public Security.StepUpArea? StepUpArea { get; set; }
	}

	/// <summary>One request as a member sees it (the shared panel): their step, its level and who else must sign.</summary>
	public sealed class SignatureStepView
	{
		public SignatureRequestRecord Request { get; set; }
		public List<SignatureRequestSigner> Signers { get; set; } = new List<SignatureRequestSigner>();
		/// <summary>The step the member may sign now (named for them, or an open step they are eligible for).</summary>
		public SignatureRequestSigner MyStep { get; set; }
		/// <summary>The member's step that is not their turn yet (an ordered request).</summary>
		public SignatureRequestSigner MyWaitingStep { get; set; }
		public SignatureLevels MyLevel { get; set; }
		/// <summary>Steps still unsigned other than the member's ("who else must sign").</summary>
		public List<SignatureRequestSigner> OthersToSign { get; set; } = new List<SignatureRequestSigner>();
		public string CurrentRevisionSha256 { get; set; }
		/// <summary>Why the member cannot sign now (None when <see cref="MyStep"/> is set).</summary>
		public SignatureRequestRefusals Blocked { get; set; }
		public bool RevisionChanged => !string.IsNullOrWhiteSpace(CurrentRevisionSha256) && Request != null &&
			!string.Equals(CurrentRevisionSha256, Request.ArtifactSha256, StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>A step waiting for the member ("my signatures" on My Training and home).</summary>
	public sealed class PendingSignatureItem
	{
		public int SignatureRequestId { get; set; }
		public int SignatureRequestSignerId { get; set; }
		public int SubjectKind { get; set; }
		public string SubjectId { get; set; }
		public string Title { get; set; }
		public int Role { get; set; }
		public int RequiredLevel { get; set; }
		public string StepLabel { get; set; }
		public DateTime ExpiresOn { get; set; }
		public DateTime? TurnStartedOn { get; set; }
		public string ContentCode { get; set; }
	}

	/// <summary>The ordering, role and level rules of a multi-signer request (pure, S3).</summary>
	public static class SignatureOrdering
	{
		public const int MaxSteps = 20;

		/// <summary>Whether it is the signer's turn: always in an unordered request; in an ordered one when every earlier step is signed.</summary>
		public static bool IsTurn(SignatureRequestSigner signer, IEnumerable<SignatureRequestSigner> signers, bool ordered)
		{
			if (signer == null || signer.IsDone)
				return false;
			if (!ordered)
				return true;
			return (signers ?? Enumerable.Empty<SignatureRequestSigner>())
				.Where(s => s.SignatureRequestSignerId != signer.SignatureRequestSignerId && s.SignerOrder < signer.SignerOrder)
				.All(s => s.Status == (int)SignatureSignerStatuses.Signed);
		}

		/// <summary>The unsigned steps whose turn it is (the current parallel group of an ordered request).</summary>
		public static List<SignatureRequestSigner> OpenSteps(IReadOnlyCollection<SignatureRequestSigner> signers, bool ordered) =>
			(signers ?? Array.Empty<SignatureRequestSigner>()).Where(s => IsTurn(s, signers, ordered)).OrderBy(s => s.SignerOrder).ThenBy(s => s.SignatureRequestSignerId).ToList();

		public static bool AllSigned(IReadOnlyCollection<SignatureRequestSigner> signers) =>
			signers != null && signers.Count > 0 && signers.All(s => s.Status == (int)SignatureSignerStatuses.Signed);

		/// <summary>The lowest level a role's member step may have: evaluators, certifying officials, witnesses and recipients sign at L2.</summary>
		public static SignatureLevels RoleFloor(SignatureSignerRoles role) => role switch
		{
			SignatureSignerRoles.Evaluator => SignatureLevels.StepUp,
			SignatureSignerRoles.CertifyingOfficial => SignatureLevels.StepUp,
			SignatureSignerRoles.Witness => SignatureLevels.StepUp,
			SignatureSignerRoles.Recipient => SignatureLevels.StepUp,
			_ => SignatureLevels.Attest
		};

		/// <summary>The level a step is signed at: L1/L2 for a member (never below the role's floor), L4 for an external signer.</summary>
		public static SignatureLevels StepLevel(SignatureRequestSigner signer)
		{
			if (signer == null || !signer.IsMember)
				return SignatureLevels.External;
			var requested = signer.RequiredLevel >= (int)SignatureLevels.StepUp ? SignatureLevels.StepUp : SignatureLevels.Attest;
			var floor = RoleFloor((SignatureSignerRoles)signer.Role);
			return (int)floor > (int)requested ? floor : requested;
		}

		/// <summary>What a role means by signing.</summary>
		public static SignatureIntents IntentFor(SignatureSignerRoles role) => role switch
		{
			SignatureSignerRoles.Evaluator => SignatureIntents.Evaluate,
			SignatureSignerRoles.CertifyingOfficial => SignatureIntents.Certify,
			SignatureSignerRoles.Witness => SignatureIntents.Witness,
			SignatureSignerRoles.Vendor => SignatureIntents.Accept,
			_ => SignatureIntents.Attest
		};

		/// <summary>Roles a department member can hold in a step.</summary>
		public static bool IsMemberRole(SignatureSignerRoles role) =>
			role is SignatureSignerRoles.Member or SignatureSignerRoles.Witness or SignatureSignerRoles.CertifyingOfficial or SignatureSignerRoles.Evaluator or SignatureSignerRoles.Recipient;

		/// <summary>Roles an outside person can hold (a certifying official is always a member).</summary>
		public static bool IsExternalRole(SignatureSignerRoles role) => role != SignatureSignerRoles.Member && role != SignatureSignerRoles.CertifyingOfficial;

		/// <summary>Checks a request's steps before anything is stored.</summary>
		public static SignatureRequestRefusals Validate(MultiSignerRequestInput input)
		{
			if (input == null || input.DepartmentId <= 0 || string.IsNullOrWhiteSpace(input.SubjectId) || string.IsNullOrWhiteSpace(input.RequestedByUserId))
				return SignatureRequestRefusals.InvalidRequest;
			var steps = input.Steps ?? new List<SignatureStepInput>();
			if (steps.Count == 0 || steps.Count > MaxSteps)
				return SignatureRequestRefusals.InvalidRequest;

			var members = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (var step in steps)
			{
				if (step == null || step.Order < 1 || step.Order > MaxSteps || !Enum.IsDefined(typeof(SignatureSignerRoles), step.Role))
					return SignatureRequestRefusals.InvalidRequest;

				if (step.Kind == SignatureSignerKinds.Member)
				{
					if (!IsMemberRole(step.Role) || (step.Level != SignatureLevels.Attest && step.Level != SignatureLevels.StepUp))
						return SignatureRequestRefusals.InvalidRequest;
					var named = !string.IsNullOrWhiteSpace(step.UserId);
					if (!named && (step.Eligibility == null || step.Eligibility.IsEmpty))
						return SignatureRequestRefusals.InvalidRequest;
					if (named && !members.Add(step.UserId.Trim()))
						return SignatureRequestRefusals.InvalidRequest;
				}
				else if (step.Kind == SignatureSignerKinds.External)
				{
					if (!IsExternalRole(step.Role))
						return SignatureRequestRefusals.InvalidRequest;
					if (input.ExternalChannel == SignatureRequestChannels.Remote && string.IsNullOrWhiteSpace(step.ExternalName))
						return SignatureRequestRefusals.InvalidRequest;
				}
				else
				{
					return SignatureRequestRefusals.InvalidRequest;
				}
			}

			return SignatureRequestRefusals.None;
		}
	}

	/// <summary>When worker 90 reminds a pending signer (S3): a day after their turn started, then every two days, three times at most.</summary>
	public static class SignatureReminderPolicy
	{
		public static readonly TimeSpan FirstReminderAfter = TimeSpan.FromHours(24);
		public static readonly TimeSpan Interval = TimeSpan.FromHours(48);
		public const int MaxReminders = 3;

		/// <summary>The next reminder after <paramref name="fromUtc"/>, or null when none is left before the request expires.</summary>
		public static DateTime? Next(DateTime fromUtc, int remindersSent, DateTime expiresOnUtc)
		{
			if (remindersSent < 0 || remindersSent >= MaxReminders)
				return null;
			var next = fromUtc + (remindersSent == 0 ? FirstReminderAfter : Interval);
			return next < expiresOnUtc ? next : (DateTime?)null;
		}
	}
}
