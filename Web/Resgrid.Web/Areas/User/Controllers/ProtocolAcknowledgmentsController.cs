using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Helpers;
using Resgrid.WebCore.Areas.User.Models.Protocols;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// Protocols P2 (free unless noted): a member's acknowledgments and the read-and-sign page (the notification deep link), the
	/// officer compliance grid with CSV and PDF, per-protocol acknowledgment settings, the priority bulletin composer and its
	/// countersign queue, and the LMS proof-of-compliance report (fails closed without the add-on). Round 5F (LMS): the question-bank
	/// acknowledgment quiz (questions without a key, scored on the server), its settings, the micro-learning selection and the member's
	/// weekly questions, all through IProtocolLearningExtensions (baseline shapes only, §17.3).
	/// </summary>
	[Area("User")]
	[Resgrid.Web.Helpers.DepartmentLocalTime]
	public class ProtocolAcknowledgmentsController : SecureBaseController
	{
		private readonly IProtocolAccessService _access;
		private readonly IProtocolAcknowledgmentService _acknowledgments;
		private readonly IProtocolLibraryService _library;
		private readonly IProtocolsService _protocolsService;
		private readonly IDepartmentGroupsService _groups;
		private readonly IPersonnelRolesService _roles;
		private readonly IDepartmentsService _departments;
		private readonly ITrainingService _trainings;
		private readonly ILmsAccessService _lms;
		private readonly IProtocolLearningExtensions _learningExtensions;
		private readonly IPdfProvider _pdf;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.Protocols.Protocols> _localizer;

		public ProtocolAcknowledgmentsController(IProtocolAccessService access, IProtocolAcknowledgmentService acknowledgments, IProtocolLibraryService library,
			IProtocolsService protocolsService, IDepartmentGroupsService groups, IPersonnelRolesService roles, IDepartmentsService departments, ITrainingService trainings,
			ILmsAccessService lms, IProtocolLearningExtensions learningExtensions, IPdfProvider pdf,
			IStringLocalizer<Resgrid.Localization.Areas.User.Protocols.Protocols> localizer)
		{
			_access = access;
			_acknowledgments = acknowledgments;
			_library = library;
			_protocolsService = protocolsService;
			_groups = groups;
			_roles = roles;
			_departments = departments;
			_trainings = trainings;
			_lms = lms;
			_learningExtensions = learningExtensions;
			_pdf = pdf;
			_localizer = localizer;
		}

		private string Text(string key) => _localizer?[key]?.Value ?? key;

		private Task<ProtocolReader> ReaderAsync() => _access.GetReaderAsync(DepartmentId, UserId);

		private static bool ManagesAcknowledgments(ProtocolReader reader) =>
			reader != null && reader.IsCurrentMember && (reader.IsDepartmentAdmin || reader.CanManageAcknowledgments || reader.CanApprove);

		private void Shell(ProtocolReader reader, string section)
		{
			ViewData["Section"] = section;
			ViewData["CanCreate"] = reader.MayAuthor;
			ViewData["IsManager"] = reader.IsManager;
			ViewData["IsAdmin"] = reader.IsDepartmentAdmin;
			ViewData["ManagesAcknowledgments"] = ManagesAcknowledgments(reader);
			ViewData["CanIssueBulletins"] = reader.MayApprove;
		}

		#region Member

		[HttpGet]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> Mine(bool all = false)
		{
			var reader = await ReaderAsync();
			Shell(reader, "Acknowledgments");
			var items = await _acknowledgments.GetMyAcknowledgmentsAsync(DepartmentId, UserId, all, DateTime.UtcNow);
			return View(new MyAcknowledgmentsModel
			{
				Outstanding = items.Where(i => ProtocolAcknowledgmentRules.IsOutstanding(i.Acknowledgment)).ToList(),
				Completed = items.Where(i => !ProtocolAcknowledgmentRules.IsOutstanding(i.Acknowledgment)).ToList(),
				ShowCompleted = all,
				Message = TempData?["ProtocolMessage"] as string,
				Error = TempData?["ProtocolError"] as string
			});
		}

		/// <summary>The read-and-sign page (the notification deep link). Opening it is what "the member must open the version" records.</summary>
		[HttpGet]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> Read(int versionId, CancellationToken cancellationToken = default)
		{
			var reader = await ReaderAsync();
			var version = await _library.GetVersionAsync(DepartmentId, versionId);
			var protocol = version == null ? null : await _protocolsService.GetProtocolHeaderByIdAsync(version.DispatchProtocolId);
			if (version == null || protocol == null || protocol.DepartmentId != DepartmentId || !reader.IsCurrentMember)
				return NotFound();

			var row = await _acknowledgments.GetForVersionAndUserAsync(DepartmentId, versionId, UserId);
			var audience = await _library.GetAudienceAsync(DepartmentId, protocol.DispatchProtocolId);
			var readable = ProtocolAccessRules.CanReadVersion(reader, protocol, audience, version) ||
				(row != null && ProtocolAcknowledgmentRules.IsAcknowledgeable(protocol, version));
			if (!readable)
				return NotFound();

			if (row != null && row.IsOpen)
				row = await _acknowledgments.MarkOpenedAsync(DepartmentId, UserId, versionId, DateTime.UtcNow, cancellationToken) ?? row;

			Shell(reader, "Acknowledgments");
			var settings = await _library.GetSettingsAsync(DepartmentId);
			var model = new ReadAndSignModel
			{
				Protocol = protocol,
				Version = version,
				Acknowledgment = row,
				Statement = ProtocolSignatureStatements.ReadAndSign(protocol, version, Text("ReadAndSignStatement")),
				RequiredLevel = (int)SignaturePolicy.RequiredLevel(SignatureUse.ReadAndSign, settings.ReadAndSignRequiresStepUp ? (int)SignatureLevels.StepUp : (int?)null),
				CanSign = row != null && row.IsOpen && ProtocolAcknowledgmentRules.IsAcknowledgeable(protocol, version),
				Outline = version.IsDocument ? new List<ProtocolContentAnchor>() : ProtocolContentDocument.OutlineFor(version.ContentJson, version.ContentHtml),
				Message = TempData?["ProtocolMessage"] as string,
				Error = TempData?["ProtocolError"] as string
			};

			if (row?.QuizTrainingId != null)
			{
				var training = await _trainings.GetTrainingByIdAsync(row.QuizTrainingId.Value);
				model.QuizTrainingName = training?.DepartmentId == DepartmentId ? training.Name : null;
				model.QuizPassed = (await _trainings.GetTrainingUsersForUserAsync(UserId) ?? new List<TrainingUser>())
					.Any(t => t.TrainingId == row.QuizTrainingId.Value && t.Passed == true && !t.RemovedOn.HasValue);
			}

			// LMS: the question-bank quiz. Shown to a member who owes the acknowledgment when the protocol asks for one and either the add-on
			// is active (it will be asked at signing) or the signature is already waiting for it (rule 4: it stays takeable).
			if (row != null && ProtocolAcknowledgmentRules.IsOutstanding(row))
			{
				var bank = await _learningExtensions.GetBankQuizStateAsync(DepartmentId, protocol.DispatchProtocolId, versionId, UserId);
				if (bank != null && bank.Configured && (row.Status == (int)ProtocolAcknowledgmentStatuses.AwaitingQuiz || await _lms.CanAuthorAndManageAsync(DepartmentId)))
					model.BankQuiz = bank;
			}

			return View(model);
		}

		/// <summary>The question-bank quiz of a version the member owes (LMS): questions and options only; the key never leaves the server.</summary>
		[HttpGet]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> Quiz(int versionId, CancellationToken cancellationToken = default)
		{
			var reader = await ReaderAsync();
			Shell(reader, "Acknowledgments");
			var quiz = await _learningExtensions.StartBankQuizAsync(reader, versionId, CultureInfo.CurrentUICulture.Name, cancellationToken);
			if (quiz.Error != ProtocolLearningErrors.None)
			{
				TempData["ProtocolError"] = Text("QuizError_" + quiz.Error);
				return RedirectToAction("Read", new { versionId });
			}

			return View(new ProtocolQuizModel { Quiz = quiz });
		}

		/// <summary>Scores an attempt on the server. Answers arrive as q{questionId} = option key(s).</summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> Quiz(int attemptId, int versionId, IFormCollection form, CancellationToken cancellationToken = default)
		{
			var reader = await ReaderAsync();
			var answers = new List<ProtocolQuizAnswer>();
			foreach (var key in form?.Keys ?? new List<string>())
			{
				if (key.Length < 2 || key[0] != 'q' || !int.TryParse(key.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out var questionId))
					continue;
				answers.Add(new ProtocolQuizAnswer { QuestionId = questionId, Keys = form[key].Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).ToList() });
			}

			var result = await _learningExtensions.SubmitBankQuizAsync(reader, attemptId, answers, cancellationToken);
			if (result.Error == ProtocolLearningErrors.NotFound || result.Error == ProtocolLearningErrors.NotAuthorized)
				return NotFound();

			Shell(reader, "Acknowledgments");
			return View(new ProtocolQuizModel { Result = result, Error = result.Error == ProtocolLearningErrors.None ? null : Text("QuizError_" + result.Error) });
		}

		/// <summary>The member's weekly micro-learning questions (LMS), newest first; each is answered once.</summary>
		[HttpGet]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> Weekly(int? id = null)
		{
			var reader = await ReaderAsync();
			if (!reader.IsCurrentMember)
				return Unauthorized();
			Shell(reader, "Acknowledgments");
			return View(new ProtocolWeeklyModel
			{
				Items = await _learningExtensions.GetMyMicroLearningAsync(DepartmentId, UserId, 12),
				FocusId = id,
				Message = TempData?["ProtocolMessage"] as string,
				Error = TempData?["ProtocolError"] as string
			});
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> AnswerWeekly(int id, List<string> keys, CancellationToken cancellationToken = default)
		{
			var reader = await ReaderAsync();
			if (!reader.IsCurrentMember)
				return Unauthorized();
			var item = await _learningExtensions.AnswerMicroLearningAsync(DepartmentId, UserId, id, keys ?? new List<string>(), cancellationToken);
			if (item.Error == ProtocolLearningErrors.NotFound)
				return NotFound();
			if (item.Error == ProtocolLearningErrors.InvalidInput)
				TempData["ProtocolError"] = Text("WeeklyChooseAnswer");
			else if (item.Error == ProtocolLearningErrors.None)
				TempData["ProtocolMessage"] = Text(item.Correct == true ? "WeeklyCorrect" : "WeeklyIncorrect");
			return RedirectToAction("Weekly", new { id });
		}

		/// <summary>Read-and-sign at L1 (the signed-in session), bound to the version's content hash.</summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> Sign(int versionId, string contentHash, CancellationToken cancellationToken = default) =>
			await SignCoreAsync(versionId, contentHash, SessionProof(), cancellationToken);

		/// <summary>Read-and-sign at L2, for departments that raised read-and-sign above the floor (Protocols → Settings).</summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		[Resgrid.Web.Attributes.RequiresRecentTwoFactor(RequireForOperation = true)]
		public async Task<IActionResult> SignWithStepUp(int versionId, string contentHash, CancellationToken cancellationToken = default) =>
			await SignCoreAsync(versionId, contentHash, SessionProof(), cancellationToken);

		private SignatureProof SessionProof()
		{
			var proof = SignatureProof.SessionOnly(UserId, IpAddressHelper.GetRequestIP(Request, true), Request?.Headers["User-Agent"].ToString());
			// Server-side step-up evidence of this session, when the step-up action set it.
			proof.StepUpVerifiedOnUtc = HttpContext?.Items["mfa_verified_at"] as DateTime?;
			return proof;
		}

		private async Task<IActionResult> SignCoreAsync(int versionId, string contentHash, SignatureProof proof, CancellationToken cancellationToken)
		{
			var reader = await ReaderAsync();
			var version = await _library.GetVersionAsync(DepartmentId, versionId);
			var protocol = version == null ? null : await _protocolsService.GetProtocolHeaderByIdAsync(version.DispatchProtocolId);
			var statement = protocol == null ? null : ProtocolSignatureStatements.ReadAndSign(protocol, version, Text("ReadAndSignStatement"));

			var result = await _acknowledgments.AcknowledgeAsync(reader, versionId, contentHash, proof, false, statement, cancellationToken);
			if (result.Succeeded)
			{
				TempData["ProtocolMessage"] = Text(result.Outcome == ProtocolAcknowledgeOutcomes.AwaitingQuiz ? "AcknowledgedAwaitingQuizMessage" : "AcknowledgedMessage");
				return RedirectToAction("Mine");
			}

			TempData["ProtocolError"] = Text("AcknowledgeOutcome" + result.Outcome);
			if (result.Outcome == ProtocolAcknowledgeOutcomes.ReadNewerVersion && result.CurrentVersionId.HasValue)
				return RedirectToAction("Read", new { versionId = result.CurrentVersionId.Value });
			return RedirectToAction("Read", new { versionId });
		}

		#endregion Member

		#region Compliance

		[HttpGet]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> Compliance(int? protocolId = null, int? versionId = null, int? groupId = null)
		{
			var reader = await ReaderAsync();
			if (!ManagesAcknowledgments(reader))
				return Unauthorized();

			Shell(reader, "Compliance");
			return View(new ProtocolComplianceModel
			{
				Grid = await _acknowledgments.GetComplianceAsync(reader, protocolId, versionId, groupId, DateTime.UtcNow),
				Groups = (await _groups.GetAllGroupsForDepartmentAsync(DepartmentId) ?? new List<DepartmentGroup>()).OrderBy(g => g.Name).ToList(),
				Protocols = (await _protocolsService.GetProtocolSummariesForDepartmentAsync(DepartmentId) ?? new List<DispatchProtocol>())
					.Where(p => !p.IsDeleted && p.CurrentVersionId.HasValue).OrderBy(p => p.Code, StringComparer.OrdinalIgnoreCase).ToList(),
				LmsActive = await _lms.CanAuthorAndManageAsync(DepartmentId),
				Message = TempData?["ProtocolMessage"] as string
			});
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> ComplianceCsv(int? protocolId = null, int? versionId = null, int? groupId = null)
		{
			var reader = await ReaderAsync();
			if (!ManagesAcknowledgments(reader))
				return Unauthorized();

			var grid = await _acknowledgments.GetComplianceAsync(reader, protocolId, versionId, groupId, DateTime.UtcNow);
			var csv = _acknowledgments.ToCsv(grid);
			return File(new UTF8Encoding(true).GetBytes(csv), "text/csv", $"protocol-acknowledgments-{DateTime.UtcNow:yyyyMMdd}.csv");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> CompliancePdf(int? protocolId = null, int? versionId = null, int? groupId = null)
		{
			var reader = await ReaderAsync();
			if (!ManagesAcknowledgments(reader))
				return Unauthorized();

			var grid = await _acknowledgments.GetComplianceAsync(reader, protocolId, versionId, groupId, DateTime.UtcNow);
			var html = ComplianceHtml(grid);
			return File(_pdf.ConvertHtmlToPdf(html), "application/pdf", $"protocol-acknowledgments-{DateTime.UtcNow:yyyyMMdd}.pdf");
		}

		/// <summary>The printable compliance report (every value encoded; no markup from stored text).</summary>
		private string ComplianceHtml(ProtocolComplianceGrid grid)
		{
			string E(string value) => WebUtility.HtmlEncode(value ?? string.Empty);
			var html = new StringBuilder();
			html.Append("<html><head><meta charset=\"utf-8\"><style>body{font-family:Arial,sans-serif;font-size:11px}table{border-collapse:collapse;width:100%}")
				.Append("th,td{border:1px solid #ccc;padding:4px;text-align:left}th{background:#f3f3f4}</style></head><body>");
			html.Append("<h2>").Append(E(Text("ComplianceHeader"))).Append("</h2><p>").Append(E(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))).Append(" UTC</p>");

			if (grid.Protocol == null)
			{
				html.Append("<table><tr><th>").Append(E(Text("CodeLabel"))).Append("</th><th>").Append(E(Text("NameLabel"))).Append("</th><th>")
					.Append(E(Text("VersionLabel"))).Append("</th><th>").Append(E(Text("AssignedLabel"))).Append("</th><th>").Append(E(Text("AcknowledgedLabel")))
					.Append("</th><th>").Append(E(Text("OverdueLabel"))).Append("</th><th>%</th></tr>");
				foreach (var s in grid.Summaries)
					html.Append("<tr><td>").Append(E(s.Code)).Append("</td><td>").Append(E(s.Name)).Append("</td><td>").Append(E(s.VersionLabel)).Append("</td><td>")
						.Append(s.Assigned).Append("</td><td>").Append(s.Acknowledged).Append("</td><td>").Append(s.Overdue).Append("</td><td>").Append(s.Percent).Append("</td></tr>");
				html.Append("</table>");
			}
			else
			{
				html.Append("<h3>").Append(E(grid.Protocol.Code + " " + grid.Protocol.Name)).Append(" v").Append(E(grid.Version?.Label)).Append("</h3>");
				html.Append("<table><tr><th>").Append(E(Text("MemberLabel"))).Append("</th><th>").Append(E(Text("GroupLabel"))).Append("</th><th>")
					.Append(E(Text("StateLabel"))).Append("</th><th>").Append(E(Text("DueLabel"))).Append("</th><th>").Append(E(Text("SignedLabel"))).Append("</th></tr>");
				foreach (var r in grid.Rows)
					html.Append("<tr><td>").Append(E(r.Name)).Append("</td><td>").Append(E(r.GroupName)).Append("</td><td>").Append(E(Text("AckState_" + r.StateKey)))
						.Append("</td><td>").Append(E(r.Acknowledgment.DueOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))).Append("</td><td>")
						.Append(E(r.Acknowledgment.SignedOn?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))).Append("</td></tr>");
				html.Append("</table>");
			}

			html.Append("</body></html>");
			return html.ToString();
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> Settings(int id)
		{
			var reader = await ReaderAsync();
			if (!ManagesAcknowledgments(reader) && !reader.MayAuthor)
				return Unauthorized();

			var protocol = await _protocolsService.GetProtocolHeaderByIdAsync(id);
			if (protocol == null || protocol.DepartmentId != DepartmentId || protocol.IsDeleted)
				return NotFound();

			Shell(reader, "Compliance");
			return View(await SettingsModelAsync(protocol, null));
		}

		private async Task<ProtocolAcknowledgmentSettingsModel> SettingsModelAsync(DispatchProtocol protocol, string error) => new ProtocolAcknowledgmentSettingsModel
		{
			ProtocolId = protocol.DispatchProtocolId,
			DueDays = protocol.AckDueDays,
			BundleOrder = protocol.AckBundleOrder,
			QuizTrainingId = protocol.AckTrainingId,
			StandardsTags = protocol.StandardsTags,
			Protocol = protocol,
			Trainings = (await _trainings.GetAllTrainingsForDepartmentAsync(DepartmentId) ?? new List<Training>())
				.Where(t => !t.IsArchived && t.HasQuiz).OrderBy(t => t.Name).ToList(),
			LmsActive = await _lms.CanAuthorAndManageAsync(DepartmentId),
			Error = error
		}.WithLearning(await _learningExtensions.GetSettingsAsync(DepartmentId, protocol.DispatchProtocolId), await _learningExtensions.GetBankChoicesAsync(DepartmentId));

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> Settings(ProtocolAcknowledgmentSettingsModel model, CancellationToken cancellationToken = default)
		{
			var reader = await ReaderAsync();
			var result = await _acknowledgments.SaveSettingsAsync(reader, model.ProtocolId, model.DueDays, model.BundleOrder, model.QuizTrainingId, model.StandardsTags, cancellationToken);
			if (result.Succeeded && await _lms.CanAuthorAndManageAsync(DepartmentId))
			{
				// LMS (round 5F): the question-bank quiz and micro-learning, saved with the free settings when the add-on is active.
				var learning = await _learningExtensions.SaveSettingsAsync(reader, new ProtocolLearningSettings
				{
					ProtocolId = model.ProtocolId,
					QuestionBankId = model.LearningBankId > 0 ? model.LearningBankId : null,
					QuizRequired = model.LearningQuizRequired,
					QuestionCount = model.LearningQuestionCount,
					PassMark = model.LearningPassMark,
					Tags = model.LearningTags,
					MicroLearning = model.LearningMicro
				}, cancellationToken);
				if (learning != ProtocolLearningErrors.None)
				{
					var stored = await _protocolsService.GetProtocolHeaderByIdAsync(model.ProtocolId);
					if (stored == null || stored.DepartmentId != DepartmentId)
						return NotFound();
					Shell(reader, "Compliance");
					return View(await SettingsModelAsync(stored, Text("LearningSettingsError_" + learning)));
				}
			}
			if (result.Succeeded)
			{
				TempData["ProtocolMessage"] = Text("AckSettingsSavedMessage");
				return RedirectToAction("View", "Protocols", new { area = "User", id = model.ProtocolId });
			}

			if (result.Error == ProtocolLifecycleErrors.NotAuthorized)
				return Unauthorized();
			var protocol = await _protocolsService.GetProtocolHeaderByIdAsync(model.ProtocolId);
			if (protocol == null || protocol.DepartmentId != DepartmentId)
				return NotFound();

			Shell(reader, "Compliance");
			return View(await SettingsModelAsync(protocol, Text("AckSettingsInvalidTraining")));
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> Standards()
		{
			var reader = await ReaderAsync();
			if (!ManagesAcknowledgments(reader))
				return Unauthorized();

			Shell(reader, "Compliance");
			var report = await _acknowledgments.GetStandardsReportAsync(reader, DateTime.UtcNow);
			var lmsActive = report != null;
			var model = new ProtocolStandardsModel
			{
				Report = report,
				LmsActive = lmsActive,
				// One gate answer per request: the report already asked it.
				MicroLearningAvailable = lmsActive,
				BankQuizzesAvailable = lmsActive,
				Message = TempData?["ProtocolMessage"] as string,
				Error = TempData?["ProtocolError"] as string
			};
			if (lmsActive)
			{
				model.Protocols = (await _protocolsService.GetAllProtocolsForDepartmentAsync(DepartmentId) ?? new List<DispatchProtocol>())
					.Where(p => p.DepartmentId == DepartmentId && !p.IsDeleted && p.IsPublished && !p.IsBulletin)
					.OrderBy(p => p.Code, StringComparer.CurrentCultureIgnoreCase).ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
				model.MicroLearningIds = new HashSet<int>(await _learningExtensions.GetMicroLearningProtocolIdsAsync(DepartmentId));
			}
			return View(model);
		}

		/// <summary>Selects the protocols of the weekly micro-learning (LMS; officers who manage acknowledgments).</summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> MicroLearning(List<int> protocolIds, CancellationToken cancellationToken = default)
		{
			var reader = await ReaderAsync();
			if (!ManagesAcknowledgments(reader))
				return Unauthorized();
			var selected = await _learningExtensions.ScheduleMicroLearningAsync(reader, protocolIds ?? new List<int>(), DateTime.UtcNow, cancellationToken);
			if (selected < 0)
				TempData["ProtocolError"] = Text("LearningLmsOnly");
			else
				TempData["ProtocolMessage"] = string.Format(Text("MicroLearningSavedMessage"), selected);
			return RedirectToAction("Standards");
		}

		#endregion Compliance

		#region Priority bulletins

		[HttpGet]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> Bulletins()
		{
			var reader = await ReaderAsync();
			if (!reader.IsManager)
				return Unauthorized();

			Shell(reader, "Bulletins");
			var time = new DepartmentTime(await _departments.GetDepartmentByIdAsync(DepartmentId, false));
			return View(await BulletinsModelAsync(reader, new ProtocolBulletinsModel
			{
				ExpiresOn = time.Input(DateTime.UtcNow.AddDays(7))
			}));
		}

		private async Task<ProtocolBulletinsModel> BulletinsModelAsync(ProtocolReader reader, ProtocolBulletinsModel model)
		{
			model.Items = await _acknowledgments.GetBulletinsAsync(reader, DateTime.UtcNow);
			model.CanIssue = reader.MayApprove;
			foreach (var group in await _groups.GetAllGroupsForDepartmentAsync(DepartmentId) ?? new List<DepartmentGroup>())
				model.Groups[group.DepartmentGroupId] = group.Name;
			foreach (var role in await _roles.GetRolesForDepartmentAsync(DepartmentId) ?? new List<PersonnelRole>())
				model.Roles[role.PersonnelRoleId] = role.Name;
			foreach (var person in await _departments.GetAllPersonnelNamesForDepartmentAsync(DepartmentId) ?? new List<PersonName>())
				if (person != null && !string.IsNullOrWhiteSpace(person.UserId))
					model.Names[person.UserId] = person.Name?.Trim();
			model.Message ??= TempData?["ProtocolMessage"] as string;
			model.Error ??= TempData?["ProtocolError"] as string;
			return model;
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Protocol_Approve)]
		public async Task<IActionResult> IssueBulletin(ProtocolBulletinsModel model, CancellationToken cancellationToken = default)
		{
			var reader = await ReaderAsync();
			var audience = new List<ContentAudienceRule>();
			audience.AddRange((model.AudienceGroups ?? new List<int>()).Where(g => g > 0).Select(g => new ContentAudienceRule(ContentAudienceKinds.Group, g.ToString(CultureInfo.InvariantCulture))));
			audience.AddRange((model.AudienceRoles ?? new List<int>()).Where(r => r > 0).Select(r => new ContentAudienceRule(ContentAudienceKinds.Role, r.ToString(CultureInfo.InvariantCulture))));

			var time = new DepartmentTime(await _departments.GetDepartmentByIdAsync(DepartmentId, false));
			var expires = string.IsNullOrWhiteSpace(model.ExpiresOn) ? (DateTime?)null : time.Parse(model.ExpiresOn);
			var result = expires.HasValue
				? await _acknowledgments.IssueBulletinAsync(reader, new ProtocolBulletinRequest
				{
					Title = model.Title,
					ContentHtml = model.ContentHtml,
					Audience = audience,
					ExpiresOnUtc = expires.Value,
					MustRead = model.MustRead
				}, cancellationToken)
				: ProtocolLifecycleResult.Fail(ProtocolLifecycleErrors.InvalidInput);

			if (result.Succeeded)
			{
				TempData["ProtocolMessage"] = Text("BulletinIssuedMessage");
				return RedirectToAction("Bulletins");
			}

			if (result.Error == ProtocolLifecycleErrors.NotAuthorized)
				return Unauthorized();

			Shell(reader, "Bulletins");
			model.Error = result.Error == ProtocolLifecycleErrors.InvalidInput ? Text("BulletinInvalidMessage") : Text("LifecycleError" + result.Error);
			return View("Bulletins", await BulletinsModelAsync(reader, model));
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Protocol_Approve)]
		[Resgrid.Web.Attributes.RequiresRecentTwoFactor(RequireForOperation = true)]
		public async Task<IActionResult> Countersign(int id, CancellationToken cancellationToken = default)
		{
			var result = await _acknowledgments.CountersignBulletinAsync(await ReaderAsync(), id, SessionProof(), cancellationToken);
			TempData[result.Succeeded ? "ProtocolMessage" : "ProtocolError"] = result.Succeeded ? Text("BulletinCountersignedMessage") : Text("LifecycleError" + result.Error);
			return RedirectToAction("Bulletins");
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Protocol_Approve)]
		public async Task<IActionResult> WithdrawBulletin(int id, string reason, CancellationToken cancellationToken = default)
		{
			var result = await _acknowledgments.WithdrawBulletinAsync(await ReaderAsync(), id, reason, cancellationToken);
			TempData[result.Succeeded ? "ProtocolMessage" : "ProtocolError"] = result.Succeeded ? Text("BulletinWithdrawnMessage") : Text("LifecycleError" + result.Error);
			return RedirectToAction("Bulletins");
		}

		#endregion Priority bulletins
	}
}
