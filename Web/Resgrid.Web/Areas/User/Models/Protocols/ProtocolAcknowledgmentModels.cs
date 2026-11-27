using System;
using System.Collections.Generic;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.WebCore.Areas.User.Models.Protocols
{
	/// <summary>"My acknowledgments" (P2): what the member owes, bulletins first, then the onboarding bundle's order.</summary>
	public class MyAcknowledgmentsModel
	{
		public List<ProtocolAcknowledgmentItem> Outstanding { get; set; } = new List<ProtocolAcknowledgmentItem>();
		public List<ProtocolAcknowledgmentItem> Completed { get; set; } = new List<ProtocolAcknowledgmentItem>();
		public bool ShowCompleted { get; set; }
		public string Message { get; set; }
		public string Error { get; set; }
	}

	/// <summary>The read-and-sign page of one version.</summary>
	public class ReadAndSignModel
	{
		public DispatchProtocol Protocol { get; set; }
		public ProtocolVersion Version { get; set; }
		public ProtocolAcknowledgment Acknowledgment { get; set; }
		public string Statement { get; set; }
		public int RequiredLevel { get; set; } = (int)SignatureLevels.Attest;
		public bool CanSign { get; set; }
		public string QuizTrainingName { get; set; }
		public bool QuizPassed { get; set; }

		/// <summary>The LMS question-bank quiz the version's acknowledgment asks for (round 5F); null when none applies.</summary>
		public ProtocolBankQuizState BankQuiz { get; set; }
		public List<ProtocolContentAnchor> Outline { get; set; } = new List<ProtocolContentAnchor>();
		public string Message { get; set; }
		public string Error { get; set; }
	}

	/// <summary>The officer compliance grid (free; CSV free, PDF free).</summary>
	public class ProtocolComplianceModel
	{
		public ProtocolComplianceGrid Grid { get; set; } = new ProtocolComplianceGrid();
		public List<DepartmentGroup> Groups { get; set; } = new List<DepartmentGroup>();
		public List<DispatchProtocol> Protocols { get; set; } = new List<DispatchProtocol>();
		public bool LmsActive { get; set; }
		public string Message { get; set; }
	}

	/// <summary>Per-protocol acknowledgment settings.</summary>
	public class ProtocolAcknowledgmentSettingsModel
	{
		public int ProtocolId { get; set; }
		public int? DueDays { get; set; }
		public int BundleOrder { get; set; }
		public int? QuizTrainingId { get; set; }
		public string StandardsTags { get; set; }

		// LMS (round 5F): the question-bank acknowledgment quiz and the weekly micro-learning.
		public int? LearningBankId { get; set; }
		public bool LearningQuizRequired { get; set; }
		public int LearningQuestionCount { get; set; } = 5;
		public int LearningPassMark { get; set; } = 80;
		public string LearningTags { get; set; }
		public bool LearningMicro { get; set; }

		public DispatchProtocol Protocol { get; set; }
		public List<Training> Trainings { get; set; } = new List<Training>();
		public List<ProtocolQuestionBankChoice> Banks { get; set; } = new List<ProtocolQuestionBankChoice>();

		/// <summary>Fills the LMS learning fields from the stored settings and the bank choices.</summary>
		public ProtocolAcknowledgmentSettingsModel WithLearning(ProtocolLearningSettings learning, List<ProtocolQuestionBankChoice> banks)
		{
			if (learning != null)
			{
				LearningBankId = learning.QuestionBankId;
				LearningQuizRequired = learning.QuizRequired;
				LearningQuestionCount = learning.QuestionCount;
				LearningPassMark = learning.PassMark;
				LearningTags = learning.Tags;
				LearningMicro = learning.MicroLearning;
			}
			Banks = banks ?? new List<ProtocolQuestionBankChoice>();
			return this;
		}

		public bool LmsActive { get; set; }
		public string Error { get; set; }
	}

	/// <summary>An acknowledgment quiz drawn from a question bank (LMS): the questions without a key, or the scored result.</summary>
	public class ProtocolQuizModel
	{
		public ProtocolQuizView Quiz { get; set; }
		public ProtocolQuizResult Result { get; set; }
		public string Error { get; set; }
	}

	/// <summary>The member's weekly micro-learning questions (LMS).</summary>
	public class ProtocolWeeklyModel
	{
		public List<ProtocolMicroLearningItem> Items { get; set; } = new List<ProtocolMicroLearningItem>();
		public int? FocusId { get; set; }
		public string Message { get; set; }
		public string Error { get; set; }
	}

	/// <summary>The bulletin composer and the countersign queue (R-P7).</summary>
	public class ProtocolBulletinsModel
	{
		public string Title { get; set; }
		public string ContentHtml { get; set; }
		public bool MustRead { get; set; } = true;
		public string ExpiresOn { get; set; }
		public List<int> AudienceGroups { get; set; } = new List<int>();
		public List<int> AudienceRoles { get; set; } = new List<int>();

		public List<ProtocolBulletinItem> Items { get; set; } = new List<ProtocolBulletinItem>();
		public Dictionary<int, string> Groups { get; set; } = new Dictionary<int, string>();
		public Dictionary<int, string> Roles { get; set; } = new Dictionary<int, string>();
		public Dictionary<string, string> Names { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		public bool CanIssue { get; set; }
		public string Message { get; set; }
		public string Error { get; set; }
	}

	/// <summary>The LMS proof-of-compliance report by standards tag.</summary>
	public class ProtocolStandardsModel
	{
		public ProtocolStandardsReport Report { get; set; }
		public bool LmsActive { get; set; }
		public bool MicroLearningAvailable { get; set; }
		public bool BankQuizzesAvailable { get; set; }

		/// <summary>Published protocols an officer can select for the weekly micro-learning, and the ones selected.</summary>
		public List<DispatchProtocol> Protocols { get; set; } = new List<DispatchProtocol>();
		public HashSet<int> MicroLearningIds { get; set; } = new HashSet<int>();
		public string Message { get; set; }
		public string Error { get; set; }
	}

	/// <summary>A new document-backed protocol (one file).</summary>
	public class ProtocolDocumentUploadModel
	{
		public int Kind { get; set; } = (int)ProtocolKinds.Policy;
		public int? CategoryId { get; set; }
		public string Title { get; set; }
		public bool RequiresAcknowledgment { get; set; } = true;

		public List<ProtocolCategory> Categories { get; set; } = new List<ProtocolCategory>();
		public string Error { get; set; }
	}

	/// <summary>A new upload into a protocol's draft (a new version).</summary>
	public class ProtocolDocumentVersionModel
	{
		public DispatchProtocol Protocol { get; set; }
		public ProtocolVersion Current { get; set; }
		public ProtocolVersion Draft { get; set; }
		public ProtocolVersionFile CurrentFile { get; set; }
		public string Error { get; set; }
	}

	/// <summary>Bulk upload: the form and the per-file result list.</summary>
	public class ProtocolBulkUploadModel
	{
		public int Kind { get; set; } = (int)ProtocolKinds.Sog;
		public int? CategoryId { get; set; }
		public bool FoldersAsCategories { get; set; } = true;

		public List<ProtocolCategory> Categories { get; set; } = new List<ProtocolCategory>();
		public List<ProtocolBulkUploadItem> Results { get; set; }
		public string Error { get; set; }
	}
}
