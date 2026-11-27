using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Localization.Areas.User.Trainings;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Round 10 (Omni): the member's own transcript and certificate PDFs for the v4 API, printed exactly as the web's Training records pages
	/// print them (TrainingRecordsController.TranscriptPdf and Certificate): the lines, provenance and "recorded by" texts, the hours by category,
	/// the LMS verification code with its QR and the department's default certificate template (the add-on), lapse rule 9 for a code issued
	/// before a lapse, and the L3 seal over the shared subject revision (TrainingDocuments.*RevisionSha256). The language is the request's,
	/// not the host's (the API sets no request culture).
	/// </summary>
	public class TrainingRecordDocumentService : ITrainingRecordDocumentService
	{
		private readonly ILearningRecordService _learningRecords;
		private readonly IDepartmentsService _departments;
		private readonly ITrainingCompletionRecordingService _recordings;
		private readonly ILmsAccessService _lmsAccess;
		private readonly ILmsTrainingService _lmsTraining;
		private readonly IDocumentSealingService _sealing;

		public TrainingRecordDocumentService(ILearningRecordService learningRecords, IDepartmentsService departments, ITrainingCompletionRecordingService recordings,
			ILmsAccessService lmsAccess, ILmsTrainingService lmsTraining, IDocumentSealingService sealing)
		{
			_learningRecords = learningRecords;
			_departments = departments;
			_recordings = recordings;
			_lmsAccess = lmsAccess;
			_lmsTraining = lmsTraining;
			_sealing = sealing;
		}

		private sealed class Run
		{
			public TrainingRecordDocumentRequest Request;
			public CultureInfo Culture;
			public Department Department;
			public Dictionary<string, string> Names;

			public string L(string key) => TrainingTexts.Get(key, Culture.Name);
		}

		private async Task<Run> StartAsync(TrainingRecordDocumentRequest request)
		{
			var names = (await _departments.GetAllPersonnelNamesForDepartmentAsync(request.DepartmentId) ?? new List<PersonName>())
				.Where(n => n != null && !string.IsNullOrWhiteSpace(n.UserId))
				.GroupBy(n => n.UserId, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);
			return new Run
			{
				Request = request,
				Culture = TrainingTexts.Culture(request.Culture),
				Department = await _departments.GetDepartmentByIdAsync(request.DepartmentId, false),
				Names = names
			};
		}

		private static bool Valid(TrainingRecordDocumentRequest request) =>
			request != null && request.DepartmentId > 0 && !string.IsNullOrWhiteSpace(request.UserId);

		private static bool SameUser(string a, string b) => !string.IsNullOrWhiteSpace(a) && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

		#region Transcript

		public async Task<TrainingRecordDocumentFile> GetMyTranscriptPdfAsync(TrainingRecordDocumentRequest request, DateTime? fromLocalDate, DateTime? toLocalDate,
			int? categoryId, int? source, CancellationToken cancellationToken = default)
		{
			if (!Valid(request))
				return null;

			var run = await StartAsync(request);
			var from = fromLocalDate;
			var to = toLocalDate;
			if (from.HasValue && to.HasValue && from.Value > to.Value)
				(from, to) = (to, from);

			var target = request.UserId;
			var categories = await _learningRecords.GetCategoriesAsync(request.DepartmentId, true) ?? new List<TrainingCategory>();
			var sources = source.HasValue && Enum.IsDefined(typeof(LearningRecordSourceKinds), source.Value) ? new[] { source.Value } : null;
			var records = (await _learningRecords.GetTranscriptAsync(request.DepartmentId, target, StartUtc(from, run.Department), EndUtc(to, run.Department),
					categoryId.HasValue ? new[] { categoryId.Value } : null, sources, false) ?? new List<LearningRecord>())
				.Where(r => r != null && r.DepartmentId == request.DepartmentId && SameUser(r.UserId, target))
				.OrderByDescending(r => r.CompletedOn).ThenByDescending(r => r.LearningRecordId).ToList();
			var recordings = records.Any(r => r.IsRecordedByOther)
				? await _recordings.GetForRecordsAsync(request.DepartmentId, records) ?? new Dictionary<int, TrainingCompletionRecording>()
				: new Dictionary<int, TrainingCompletionRecording>();

			var document = new TranscriptDocument
			{
				Context = Context(run),
				MemberName = run.Names.TryGetValue(target, out var name) && !string.IsNullOrWhiteSpace(name) ? name : target,
				FromUtc = StartUtc(from, run.Department),
				ToUtc = EndUtc(to, run.Department)
			};

			foreach (var record in records)
			{
				var methodKey = MethodKey(record.MethodOfTraining);
				document.Lines.Add(new TranscriptLine
				{
					CompletedOn = DocumentTime(record.CompletedOn, run.Department),
					Title = record.Title,
					CourseCode = record.CourseCode,
					Category = CategoryLabel(run, record.CategoryId, categories),
					Source = run.L(SourceKey(record.SourceKind)),
					Status = run.L(StatusKey(record.Status)),
					Hours = record.Hours,
					CeHours = record.CeHours,
					CeProvider = record.CeProvider,
					CeCourseNumber = record.CeCourseNumber,
					Method = methodKey == null ? null : run.L(methodKey),
					Instructors = InstructorsOf(record, run.Names),
					Counts = record.Counts,
					RecordedBy = RecordedByText(run, record, recordings),
					Provenance = Resgrid.Localization.Areas.User.TrainingReviews.TrainingReviewTexts.Get("Provenance." + TrainingSubmissionRules.ProvenanceOf(record), run.Culture.Name)
						?? "Provenance." + TrainingSubmissionRules.ProvenanceOf(record)
				});
			}

			foreach (var group in records.Where(r => r.Counts).GroupBy(r => CategoryLabel(run, r.CategoryId, categories)))
				document.HoursByCategory[group.Key] = group.Sum(r => r.Hours);
			document.TotalHours = records.Where(r => r.Counts).Sum(r => r.Hours);

			return await SealAsync(run, SignatureSubjectKinds.TrainingTranscript, target, TrainingDocuments.TranscriptRevisionSha256(target, document, categoryId, source),
				document.MemberName, TrainingDocuments.TranscriptHtml(document), "training-transcript-" + DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".pdf",
				cancellationToken);
		}

		#endregion Transcript

		#region Certificate

		public async Task<TrainingRecordDocumentFile> GetMyCertificatePdfAsync(TrainingRecordDocumentRequest request, int learningRecordId, CancellationToken cancellationToken = default)
		{
			if (!Valid(request) || learningRecordId <= 0)
				return null;

			var record = await _learningRecords.GetRecordAsync(request.DepartmentId, learningRecordId);
			// The member's own verified record only (reviewers print others' on the web).
			if (record == null || record.DepartmentId != request.DepartmentId || record.Status != (int)LearningRecordStatuses.Verified || !SameUser(record.UserId, request.UserId))
				return null;

			var run = await StartAsync(request);
			var categories = await _learningRecords.GetCategoriesAsync(request.DepartmentId, true) ?? new List<TrainingCategory>();
			var verifier = record.VerifiedByUserId != null && run.Names.TryGetValue(record.VerifiedByUserId, out var verifierName) && !string.IsNullOrWhiteSpace(verifierName)
				? verifierName : null;

			var document = new CertificateDocument
			{
				Context = Context(run),
				MemberName = run.Names.TryGetValue(record.UserId, out var holder) && !string.IsNullOrWhiteSpace(holder) ? holder : record.UserId,
				Course = record.Title,
				CourseCode = record.CourseCode,
				CompletedOn = DocumentTime(record.CompletedOn, run.Department),
				Hours = record.Hours,
				CeHours = record.CeHours,
				CeProvider = record.CeProvider,
				CeCourseNumber = record.CeCourseNumber,
				Category = record.CategoryId.HasValue ? CategoryLabel(run, record.CategoryId, categories) : null,
				Instructors = InstructorsOf(record, run.Names),
				IssuedBy = verifier == null ? run.Department?.Name : (run.Department?.Name + " · " + verifier)
			};

			string html = null;
			var lms = false;
			try { lms = await _lmsAccess.CanAuthorAndManageAsync(request.DepartmentId); }
			catch (Exception ex) { Logging.LogException(ex); }

			// Lapse rule 9 (R-L7): a code issued while the add-on was active keeps printing (with its QR) after a lapse; no new code, no template.
			if (!lms && !string.IsNullOrWhiteSpace(record.VerificationCode))
			{
				try
				{
					if (await _lmsAccess.IsFlagEnabledAsync(request.DepartmentId))
						Verification(document, record.VerificationCode, request);
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
				}
			}

			if (lms)
			{
				// LMS: a verification code, the public verify URL and its QR, and the department's default template. Any LMS failure prints the
				// standard certificate instead of failing the member's download.
				try
				{
					var code = await _lmsTraining.EnsureVerificationCodeAsync(request.DepartmentId, record.LearningRecordId, cancellationToken);
					if (!string.IsNullOrWhiteSpace(code))
						Verification(document, code, request);

					var template = (await _lmsTraining.GetCertificateTemplatesAsync(request.DepartmentId) ?? new List<LmsCertificateTemplate>())
						.FirstOrDefault(t => t != null && t.IsDefault && t.DepartmentId == request.DepartmentId && !string.IsNullOrWhiteSpace(t.Body));
					if (template != null)
						html = TrainingDocuments.CustomCertificateHtml(StringHelpers.SanitizeHtmlForStorage(template.Body), document);
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, "LMS certificate details could not be added; printing the standard certificate.");
					document.VerificationCode = null;
					document.VerificationUrl = null;
					document.QrImageDataUrl = null;
				}
			}

			html ??= TrainingDocuments.CertificateHtml(document);

			return await SealAsync(run, SignatureSubjectKinds.TrainingCertificate, record.LearningRecordId.ToString(CultureInfo.InvariantCulture),
				TrainingDocuments.CertificateRevisionSha256(record, document.VerificationCode), document.Course, html,
				"certificate-" + record.LearningRecordId.ToString(CultureInfo.InvariantCulture) + ".pdf", cancellationToken);
		}

		private static void Verification(CertificateDocument document, string code, TrainingRecordDocumentRequest request)
		{
			document.VerificationCode = code;
			document.VerificationUrl = (Config.SystemBehaviorConfig.ResgridBaseUrl ?? string.Empty).TrimEnd('/') + "/verify/certificate/" + Uri.EscapeDataString(code);
			try { document.QrImageDataUrl = request.QrDataUrl?.Invoke(document.VerificationUrl); }
			catch (Exception ex) { Logging.LogException(ex); }
		}

		#endregion Certificate

		#region Shared

		/// <summary>e-Signature S1 (R-S3): sealed at L3 when sealing is configured; a sealed copy of the same revision is served again.</summary>
		private async Task<TrainingRecordDocumentFile> SealAsync(Run run, SignatureSubjectKinds kind, string subjectId, string revisionSha256, string title, string html,
			string fileName, CancellationToken cancellationToken)
		{
			var outcome = await _sealing.SealHtmlAsync(new SealHtmlRequest
			{
				DepartmentId = run.Request.DepartmentId,
				SubjectKind = kind,
				SubjectId = subjectId,
				RevisionSha256 = revisionSha256,
				Title = title,
				FileName = fileName,
				Html = html,
				PageSize = PageSize(run.Culture),
				RetentionYears = null,
				LongTermSubject = true,
				DepartmentName = run.Department?.Name,
				Culture = run.Culture.Name,
				RequestedByUserId = run.Request.UserId
			}, cancellationToken);
			if (outcome?.Bytes == null || outcome.Bytes.Length == 0)
				return null;
			return new TrainingRecordDocumentFile { Bytes = outcome.Bytes, FileName = fileName, Sealed = outcome.Sealed };
		}

		private static TrainingDocumentContext Context(Run run) => new TrainingDocumentContext
		{
			DepartmentName = run.Department?.Name,
			Labels = ((IEnumerable<string>)TrainingDocuments.LabelKeys ?? Array.Empty<string>())
				.Where(k => !string.IsNullOrEmpty(k)).Distinct(StringComparer.Ordinal).ToDictionary(k => k, run.L, StringComparer.Ordinal),
			TimeZone = run.Department?.TimeZone,
			Culture = run.Culture.Name,
			RightToLeft = run.Culture.TextInfo.IsRightToLeft,
			GeneratedOnUtc = DateTime.UtcNow,
			GeneratedBy = run.Names.TryGetValue(run.Request.UserId, out var me) && !string.IsNullOrWhiteSpace(me) ? me : run.Request.UserName
		};

		/// <summary>Letter for English, A4 elsewhere (as the web pages print).</summary>
		internal static string PageSize(CultureInfo culture) =>
			string.Equals(culture?.TwoLetterISOLanguageName, "en", StringComparison.OrdinalIgnoreCase) ? "Letter" : "A4";

		/// <summary>A department-local date's start as UTC.</summary>
		internal static DateTime? StartUtc(DateTime? localDate, Department department) =>
			localDate.HasValue
				? DateTime.SpecifyKind(DateTime.SpecifyKind(localDate.Value.Date, DateTimeKind.Unspecified).DepartmentLocalToUtc(department), DateTimeKind.Utc)
				: (DateTime?)null;

		/// <summary>The end (exclusive) of a department-local date as UTC.</summary>
		internal static DateTime? EndUtc(DateTime? localDate, Department department) =>
			localDate.HasValue ? StartUtc(localDate.Value.Date.AddDays(1), department) : null;

		private static DateTime DocumentTime(DateTime utc, Department department) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).TimeConverter(department);

		// The web's LearningRecordPresentation keys (Trainings resx).
		internal static string SourceKey(int kind) =>
			Enum.IsDefined(typeof(LearningRecordSourceKinds), kind) ? "LearningSource." + (LearningRecordSourceKinds)kind : "Rec.UnknownSource";

		internal static string StatusKey(int status) =>
			"LearningStatus." + (Enum.IsDefined(typeof(LearningRecordStatuses), status) ? (LearningRecordStatuses)status : LearningRecordStatuses.Pending);

		internal static string MethodKey(int? method) =>
			method.HasValue && Enum.IsDefined(typeof(MethodsOfTraining), method.Value) ? "TrainingMethod." + (MethodsOfTraining)method.Value : null;

		private static string CategoryLabel(Run run, int? categoryId, IReadOnlyCollection<TrainingCategory> categories)
		{
			if (!categoryId.HasValue)
				return run.L("Doc.Uncategorized");
			var category = categories.FirstOrDefault(c => c.TrainingCategoryId == categoryId.Value);
			if (category == null)
				return run.L("Doc.Uncategorized");
			return category.IsDeleted ? string.Format(run.Culture, run.L("Rec.RemovedCategory"), category.Name) : category.Name;
		}

		private static string InstructorsOf(LearningRecord record, IReadOnlyDictionary<string, string> names)
		{
			if (!string.IsNullOrWhiteSpace(record.InstructorNames))
				return record.InstructorNames;
			if (string.IsNullOrWhiteSpace(record.InstructorUserIds))
				return null;
			return string.Join(", ", record.InstructorUserIds.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
				.Select(id => id.Trim()).Where(id => id.Length > 0).Select(id => names.TryGetValue(id, out var n) && !string.IsNullOrWhiteSpace(n) ? n : id));
		}

		/// <summary>Round 7B: who recorded an instructor-recorded completion (who, when, from where, the note), as the web prints it.</summary>
		private static string RecordedByText(Run run, LearningRecord record, IReadOnlyDictionary<int, TrainingCompletionRecording> recordings)
		{
			if (record == null || !record.IsRecordedByOther)
				return null;
			TrainingCompletionRecording recording = null;
			if (record.RecordedCompletionId.HasValue && recordings != null)
				recordings.TryGetValue(record.RecordedCompletionId.Value, out recording);
			var by = record.RecordedByUserId ?? recording?.RecordedByUserId;
			var name = by != null && run.Names.TryGetValue(by, out var n) && !string.IsNullOrWhiteSpace(n) ? n : by;
			var on = record.RecordedOn ?? recording?.RecordedOn;
			var text = string.Format(run.Culture, run.L("RecordCompletions.RecordedByFormat"), name,
				on.HasValue ? DateTime.SpecifyKind(on.Value, DateTimeKind.Utc).TimeConverter(run.Department).ToString("d", run.Culture) : string.Empty);
			if (recording != null)
				text += " · " + run.L("RecordCompletions.Source." + (TrainingCompletionRecordingSources)recording.SourceKind) +
					(string.IsNullOrWhiteSpace(recording.Note) ? string.Empty : " · " + recording.Note.Trim());
			return text;
		}

		#endregion Shared
	}
}
