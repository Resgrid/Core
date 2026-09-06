using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Services.Records;
using Resgrid.Web.Areas.User.Models.Records;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Areas.User.Controllers
{
	[Area("User")]
	[Authorize(Policy = ResgridResources.Record_View)]
	[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
	public class RecordEvidenceController : SecureBaseController
	{
		private readonly IRecordEvidenceSelectionService _selection;
		private readonly IRecordsEvidenceService _evidence;
		private readonly IRecordsService _records;
		public RecordEvidenceController(IRecordEvidenceSelectionService selection, IRecordsEvidenceService evidence, IRecordsService records)
		{ _selection = selection; _evidence = evidence; _records = records; }

		[HttpGet]
		public async Task<IActionResult> Index(string recordId, RmsRecordKind recordKind, int page = 0)
		{
			if (page < 0 || page > int.MaxValue / 50) return BadRequest();
			try
			{
				await _selection.GetContextAsync(DepartmentId, UserId, recordId, recordKind);
				var artifacts = await _evidence.GetHistoryAsync(DepartmentId, recordId, page * 50, 51);
				await AuditAsync(recordId, null, RmsAccessAuditAction.Read, "Evidence history viewed");
				var context = await _selection.GetContextAsync(DepartmentId, UserId, recordId, recordKind);
				var model = new RecordEvidenceView { Context = context, Page = page, HasMore = artifacts.Count > 50, Message = TempData["EvidenceMessage"] as string };
				foreach (var artifact in artifacts.Take(50).Where(a => a.DepartmentId == DepartmentId && a.RecordId == recordId && a.RecordKind == (int)recordKind && !a.DeletedOn.HasValue))
				{
					if (artifact.Classification != (int)RmsEvidenceClassification.Unrestricted && !context.CanViewRestricted)
						model.Artifacts.Add(new RecordEvidenceArtifactView { Withheld = true });
					else model.Artifacts.Add(new RecordEvidenceArtifactView { Id = artifact.RmsEvidenceArtifactId, Title = artifact.Title,
						Reason = artifact.CaptureReason, Checksum = artifact.Checksum, SourceVersion = artifact.SourceVersion,
						CapturedOn = artifact.CapturedOn, Items = artifact.SourceItemCount, RevisionId = artifact.RevisionId, Superseded = !artifact.IsCurrent });
				}
				return View(model);
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
			catch (ArgumentException ex) { return BadRequest(ex.Message); }
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Record_Create)]
		public async Task<IActionResult> Select(string recordId, RmsRecordKind recordKind, RmsEvidenceKind sourceKind = RmsEvidenceKind.RunCardActivation, string channelId = null, long afterSequence = 0)
		{
			try
			{
				await _selection.GetContextAsync(DepartmentId, UserId, recordId, recordKind);
				await AuditAsync(recordId, null, RmsAccessAuditAction.Read, "Evidence source selection: " + sourceKind);
				var selection = await _selection.GetAsync(DepartmentId, UserId, recordId, recordKind, sourceKind, channelId, afterSequence);
				return View(new RecordEvidenceSelectionView { Selection = selection, Input = new RecordEvidenceForm {
					RecordId = recordId, RecordKind = recordKind, SourceKind = sourceKind, RowVersion = selection.Context.RowVersion,
					StartUtc = selection.Context.StartUtc, EndUtc = selection.Context.EndUtc } });
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
			catch (ArgumentException ex) { return BadRequest(ex.Message); }
			catch (RecordConcurrencyException) { return Conflict("The report changed while loading sources. Reload the evidence selection."); }
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_Create)]
		public async Task<IActionResult> Capture(RecordEvidenceForm input, CancellationToken cancellationToken)
		{
			if (input == null || !input.RowVersion.HasValue) return StatusCode(428, "Reload the evidence form before capturing.");
			if (!ModelState.IsValid) return BadRequest("Enter a capture reason of at most 500 characters and valid selection dates.");
			try
			{
				var context = await _selection.GetContextAsync(DepartmentId, UserId, input.RecordId, input.RecordKind);
				if (!context.CanCapture) return Forbid();
				if (context.RowVersion != input.RowVersion.Value) throw new RecordConcurrencyException(input.RecordId, input.RowVersion.Value, context.RowVersion);
				if (input.SourceKind == RmsEvidenceKind.TrackingFix && input.UnitIds?.Count is not > 0
					|| input.SourceKind == RmsEvidenceKind.CertificationSnapshot && input.UserIds?.Count is not > 0
					|| input.SourceKind == RmsEvidenceKind.ChatPromotion && input.SourceIds?.Count is not > 0) return BadRequest("Select at least one source item.");
				if (input.SourceKind == RmsEvidenceKind.TrackingFix && (!input.StartUtc.HasValue || !input.EndUtc.HasValue)) return BadRequest("Enter both UTC tracking times.");
				if (input.SourceKind == RmsEvidenceKind.CertificationSnapshot && !input.EndUtc.HasValue) return BadRequest("Enter the UTC incident time for certification validity.");
				await _evidence.CaptureAsync(new RecordEvidenceCaptureRequest { DepartmentId = DepartmentId, CapturedByUserId = UserId,
					RecordId = input.RecordId, RecordKind = input.RecordKind, Kind = input.SourceKind, ExpectedRowVersion = input.RowVersion,
					CallId = context.CallId, CaptureReason = input.CaptureReason, OriginClient = RmsOriginClient.Web,
					CoverageStart = Utc(input.StartUtc), CoverageEnd = Utc(input.EndUtc), UnitIds = input.UnitIds, UserIds = input.UserIds, SourceIds = input.SourceIds }, context.CanViewRestricted, cancellationToken);
				TempData["EvidenceMessage"] = "Evidence captured. Review the manifest before signing the report.";
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
			catch (RecordConcurrencyException) { TempData["EvidenceMessage"] = "The draft changed. Review the evidence history, then reload the selection before capturing again."; }
			catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException) { TempData["EvidenceMessage"] = ex.Message; }
			return RedirectToAction(nameof(Index), new { recordId = input.RecordId, recordKind = input.RecordKind });
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Record_Export)]
		public async Task<IActionResult> Manifest(string recordId, RmsRecordKind recordKind, string artifactId)
		{
			try
			{
				var context = await _selection.GetContextAsync(DepartmentId, UserId, recordId, recordKind);
				var artifact = await _evidence.GetAsync(DepartmentId, artifactId);
				RequireArtifact(context, artifact);
				await AuditAsync(recordId, artifact.RevisionId, RmsAccessAuditAction.Export, "Evidence manifest " + artifactId);
				artifact = await _evidence.GetAsync(DepartmentId, artifactId);
				context = await _selection.GetContextAsync(DepartmentId, UserId, recordId, recordKind);
				RequireArtifact(context, artifact);
				Response.Headers.CacheControl = "no-store";
				return File(Encoding.UTF8.GetBytes(artifact.ManifestJson), "application/json", "evidence.json");
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
			catch (ArgumentException ex) { return BadRequest(ex.Message); }
			catch (InvalidOperationException ex) { return Problem(ex.Message, statusCode: 409); }
		}

		private void RequireArtifact(RecordEvidenceContext context, RmsEvidenceArtifact artifact)
		{
			if (!context.CanExport || artifact == null || artifact.DepartmentId != DepartmentId || artifact.RecordId != context.RecordId || artifact.RecordKind != (int)context.RecordKind || artifact.DeletedOn.HasValue
				|| (artifact.Classification != (int)RmsEvidenceClassification.Unrestricted && !context.CanViewRestricted)) throw new UnauthorizedAccessException();
			if (string.IsNullOrWhiteSpace(artifact.ManifestJson) || artifact.Checksum != RecordSnapshotSerializer.Checksum(artifact.ManifestJson)) throw new InvalidOperationException("Evidence failed its integrity check.");
		}
		private Task AuditAsync(string id, string revision, RmsAccessAuditAction action, string purpose) =>
			_records.RecordAccessAsync(DepartmentId, UserId, id, revision, action, purpose, IpAddressHelper.GetRequestIP(Request, true));
		private static DateTime? Utc(DateTime? value) => value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : null;
	}
}
