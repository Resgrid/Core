using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.Checklists;
using Resgrid.Model.Providers;
using Resgrid.Services;

namespace Resgrid.Web.Areas.User.Controllers
{
	public partial class ChecklistsController
	{
		[HttpGet]
		public Task<IActionResult> Compliance() => ComplianceView(new ChecklistReportQuery { FromUtc = DateTime.UtcNow.Date.AddDays(-30), UntilUtc = DateTime.UtcNow.Date.AddDays(1) });
		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> Compliance(ChecklistReportQuery query) => ComplianceView(query);
		private async Task<IActionResult> ComplianceView(ChecklistReportQuery query)
		{
			try { return View("Compliance", await _checklists.GetComplianceSummaryAsync(Actor, query)); }
			catch (ChecklistException ex) when (ex.StatusCode == 403 && ex.Message.StartsWith("Unlock", StringComparison.Ordinal))
			{ ViewBag.ProtectionEnforced = true; return View("Compliance", new ChecklistComplianceSummary { FromUtc = query.FromUtc, UntilUtc = query.UntilUtc, TargetType = query.TargetType, TargetId = query.TargetId, IsRedacted = true }); }
		}
		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> ComplianceCsv(ChecklistReportQuery query) => File(ChecklistReportDocuments.Csv(await _checklists.GetComplianceSummaryAsync(Actor, query)), "text/csv; charset=utf-8", "checklist-compliance.csv");
		[HttpGet]
		public IActionResult ReadinessPacket() => View("ReadinessPacket");
		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> ReadinessPacket(int callId, int lookbackDays = 30)
		{
			ViewBag.CallId = callId; ViewBag.LookbackDays = lookbackDays;
			return View("ReadinessPacket", await _checklists.GetReadinessPacketForCallAsync(Actor, callId, lookbackDays));
		}
		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> ReadinessPacketDownload(int callId, int lookbackDays, [FromServices] IPdfProvider pdf)
		{
			var manifest = await _checklists.GetReadinessPacketForCallAsync(Actor, callId, lookbackDays);
			var package = ChecklistReportDocuments.Package(manifest, pdf);
			// Revalidate the attended source after potentially slow PDF conversion.
			var current = await _checklists.GetReadinessPacketForCallAsync(Actor, callId, lookbackDays);
			ChecklistReportDocuments.EnsureStillAuthorized(manifest, current);
			using var output = new MemoryStream();
			using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
			{
				void Add(string name, byte[] data) { using var stream = zip.CreateEntry(name).Open(); stream.Write(data); }
				Add("readiness.pdf", package.Pdf); Add("manifest.json", Encoding.UTF8.GetBytes(package.ManifestJson));
				Add("sha256.txt", Encoding.UTF8.GetBytes(package.PdfSha256 + "  readiness.pdf\n" + package.ManifestSha256 + "  manifest.json\n"));
			}
			return File(output.ToArray(), "application/zip", "readiness-" + callId + ".zip");
		}
	}
}
