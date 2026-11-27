using System;
using System.Collections.Generic;
using System.Linq;
using Resgrid.Model;
using Resgrid.Model.Lms;

namespace Resgrid.Web.Areas.User.Models.LmsSkills
{
	// Trainings → Skills (Training T4 part 2, LMS add-on): view models of LmsSkillsController. LMS layer (§17.3): only LMS pages use them.

	/// <summary>What every Skills page shows: the gate, the person's rights, names and the one-time notice.</summary>
	public abstract class LmsSkillPageView
	{
		/// <summary>The add-on is active: new sheets, evaluations and task books may be made (lapse rule 3).</summary>
		public bool CanAuthor { get; set; }
		public bool IsLapsed { get; set; }
		/// <summary>LMS manager (Manage Courses or department admin): authoring and every evaluation and task book.</summary>
		public bool IsManager { get; set; }
		public string UserId { get; set; }
		public string Message { get; set; }
		public string Error { get; set; }
		public Dictionary<string, string> Names { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		public Func<DateTime?, string> Date { get; set; } = d => d?.ToShortDateString();
		public Func<DateTime?, string> When { get; set; } = d => d?.ToString("g");

		public string NameOf(string userId) => userId != null && Names.TryGetValue(userId, out var name) && !string.IsNullOrWhiteSpace(name) ? name : userId;
	}

	public sealed class LmsSkillsHomeView : LmsSkillPageView
	{
		public List<LmsSkillEvaluation> MyOpenEvaluations { get; set; } = new List<LmsSkillEvaluation>();
		public List<PendingSignatureItem> MySignatures { get; set; } = new List<PendingSignatureItem>();
		public List<LmsSkillEvaluation> MyResults { get; set; } = new List<LmsSkillEvaluation>();
		public List<LmsTaskBook> MyTaskBooks { get; set; } = new List<LmsTaskBook>();
		public int Sheets { get; set; }
		public int OpenTaskBooks { get; set; }
	}

	public sealed class LmsSkillSheetsView : LmsSkillPageView
	{
		public List<LmsSkillSheet> Sheets { get; set; } = new List<LmsSkillSheet>();
		public Dictionary<int, LmsSkillSheetVersion> Published { get; set; } = new Dictionary<int, LmsSkillSheetVersion>();
		/// <summary>Sheets the member may evaluate on.</summary>
		public HashSet<int> CanEvaluate { get; set; } = new HashSet<int>();
		public IReadOnlyList<LmsSkillSheetTemplate> Templates { get; set; } = new List<LmsSkillSheetTemplate>();
	}

	/// <summary>An option for an eligibility picker (a certification type or a personnel role).</summary>
	public sealed class LmsSkillChoice
	{
		public int Id { get; set; }
		public string Name { get; set; }
	}

	public sealed class LmsSkillSheetView : LmsSkillPageView
	{
		public LmsSkillSheet Sheet { get; set; }
		public LmsSkillSheetVersion Draft { get; set; }
		public LmsSkillSheetVersion Published { get; set; }
		public List<LmsSkillSheetVersion> Versions { get; set; } = new List<LmsSkillSheetVersion>();
		public LmsSkillSheetContent DraftContent { get; set; }
		public List<string> DraftErrors { get; set; } = new List<string>();
		public List<TrainingCategory> Categories { get; set; } = new List<TrainingCategory>();
		public List<LmsSkillChoice> CertificationTypes { get; set; } = new List<LmsSkillChoice>();
		public List<LmsSkillChoice> Roles { get; set; } = new List<LmsSkillChoice>();
		public SignerEligibility EvaluatorRule { get; set; }
		public SignerEligibility NextSignerRule { get; set; }
		public bool CanEvaluate { get; set; }
	}

	public sealed class LmsSkillStartView : LmsSkillPageView
	{
		public List<LmsSkillSheet> Sheets { get; set; } = new List<LmsSkillSheet>();
		public int? SheetId { get; set; }
		public LmsTaskBook TaskBook { get; set; }
		public LmsTaskBookTask Task { get; set; }
		public List<KeyValuePair<string, string>> Members { get; set; } = new List<KeyValuePair<string, string>>();
	}

	public sealed class LmsSkillRunView : LmsSkillPageView
	{
		public LmsSkillEvaluationView Evaluation { get; set; }
		public List<string> NextSignerCandidates { get; set; } = new List<string>();
		public bool NextSignerRequired { get; set; }
		public bool AllowExternalEvaluator { get; set; }
		/// <summary>The engine's submission errors (shown beside the steps).</summary>
		public List<string> SubmitErrors { get; set; } = new List<string>();
	}

	public sealed class LmsSkillEvaluationDetailView : LmsSkillPageView
	{
		public LmsSkillEvaluationView Evaluation { get; set; }
		public SignatureRequestRecord Request { get; set; }
		public List<SignatureRequestSigner> Signers { get; set; } = new List<SignatureRequestSigner>();
		public bool CanVoid { get; set; }
		public bool IsEvaluator { get; set; }
	}

	public sealed class LmsSkillEvaluationsView : LmsSkillPageView
	{
		public List<LmsSkillEvaluation> Evaluations { get; set; } = new List<LmsSkillEvaluation>();
		public Dictionary<int, List<LmsSkillEvaluationMember>> Members { get; set; } = new Dictionary<int, List<LmsSkillEvaluationMember>>();
		public List<LmsSkillSheet> Sheets { get; set; } = new List<LmsSkillSheet>();
		public int? SheetId { get; set; }
		public int? Status { get; set; }
		public int Passed => Evaluations.Count(e => e.Status == (int)LmsSkillEvaluationStatuses.Completed && e.Result == (int)LmsSkillEvaluationResults.Passed);
		public int Failed => Evaluations.Count(e => e.Status == (int)LmsSkillEvaluationStatuses.Completed && e.Result == (int)LmsSkillEvaluationResults.Failed);
		public int Open => Evaluations.Count(e => e.Status == (int)LmsSkillEvaluationStatuses.InProgress || e.Status == (int)LmsSkillEvaluationStatuses.AwaitingSignature);
	}

	public sealed class LmsTaskBooksView : LmsSkillPageView
	{
		public List<LmsTaskBook> Mine { get; set; } = new List<LmsTaskBook>();
		public List<LmsTaskBook> All { get; set; } = new List<LmsTaskBook>();
		public List<LmsTaskBookDefinition> Definitions { get; set; } = new List<LmsTaskBookDefinition>();
		public List<KeyValuePair<string, string>> Members { get; set; } = new List<KeyValuePair<string, string>>();
	}

	public sealed class LmsTaskBookDetailView : LmsSkillPageView
	{
		public LmsTaskBookView Book { get; set; }
		public bool CanEvaluateTasks { get; set; }
		public List<string> FinalEvaluatorCandidates { get; set; } = new List<string>();
		public List<string> CertifyingCandidates { get; set; } = new List<string>();
	}

	public sealed class LmsTaskBookDefinitionsView : LmsSkillPageView
	{
		public List<LmsTaskBookDefinition> Definitions { get; set; } = new List<LmsTaskBookDefinition>();
		public IReadOnlyList<LmsTaskBookTemplate> Templates { get; set; } = new List<LmsTaskBookTemplate>();
	}

	public sealed class LmsTaskBookDefinitionView : LmsSkillPageView
	{
		public LmsTaskBookDefinition Definition { get; set; }
		public List<LmsTaskBookTask> Tasks { get; set; } = new List<LmsTaskBookTask>();
		public List<LmsSkillSheet> Sheets { get; set; } = new List<LmsSkillSheet>();
		public List<LmsSkillChoice> CertificationTypes { get; set; } = new List<LmsSkillChoice>();
		public List<LmsSkillChoice> Roles { get; set; } = new List<LmsSkillChoice>();
		public SignerEligibility EvaluatorRule { get; set; }
		public SignerEligibility FinalEvaluatorRule { get; set; }
		public SignerEligibility CertifyingRule { get; set; }
	}

	public sealed class LmsSkillSettingsView : LmsSkillPageView
	{
		public LmsSkillSettings Settings { get; set; }
	}
}
