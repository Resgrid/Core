using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Invoicing;

namespace Resgrid.Services.Invoicing
{
	/// <summary>The deployment manifest (plan C4): roster with call signs, seats and certification snapshots, rendered as self-contained HTML and filed as a Manifest attachment.</summary>
	public partial class DeploymentService
	{
		public async Task<string> RenderManifestHtmlAsync(string deploymentId, int departmentId)
		{
			var deployment = await GetDeploymentByIdAsync(deploymentId, departmentId);
			if (deployment == null) throw new InvalidOperationException("deployments_not_found");
			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId);
			var units = await _unitsService.GetUnitsForDepartmentUnlimitedAsync(departmentId) ?? new List<Unit>();
			var seats = new Dictionary<int, string>();
			foreach (var unitId in deployment.Units.Where(u => u.IsActive).Select(u => u.UnitId).Distinct())
			{
				try { foreach (var role in await _unitsService.GetRolesForUnitAsync(unitId) ?? new List<UnitRole>()) seats[role.UnitRoleId] = role.Name; }
				catch (Exception ex) { Logging.LogException(ex, "Manifest seat names could not be read."); }
			}
			return RenderManifestHtml(deployment, department, units.ToDictionary(u => u.UnitId, u => u), seats);
		}

		public async Task<DeploymentAttachment> GenerateManifestAsync(string deploymentId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var html = await RenderManifestHtmlAsync(deploymentId, departmentId);
			var pdf = _pdfProvider.ConvertHtmlToPdf(html);
			if (pdf == null || pdf.Length == 0) throw new InvalidOperationException("deployments_pdf_failed");
			var attachment = new DeploymentAttachment
			{
				DeploymentId = deploymentId, DepartmentId = departmentId, AttachmentType = (int)DeploymentAttachmentTypes.Manifest,
				Name = $"Manifest {DateTime.UtcNow:yyyy-MM-dd HHmm}", FileName = $"manifest-{DateTime.UtcNow:yyyyMMdd-HHmmss}.pdf", FileType = "application/pdf", Data = pdf
			};
			return await SaveAttachmentAsync(attachment, userId, ipAddress, userAgent, cancellationToken);
		}

		public static string RenderManifestHtml(Deployment deployment, Department department, IReadOnlyDictionary<int, Unit> units, IReadOnlyDictionary<int, string> seatNames)
		{
			string E(string value) => WebUtility.HtmlEncode(value ?? string.Empty);
			string D(DateTime? value) => value.HasValue ? (department == null ? value.Value.ToString("yyyy-MM-dd HH:mm") + " UTC" : value.Value.TimeConverter(department).ToString("yyyy-MM-dd HH:mm")) : "—";
			var sb = new StringBuilder();
			sb.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>Resgrid | Manifest</title><style>body{font-family:Arial,Helvetica,sans-serif;font-size:12px;color:#222;margin:24px}h1{font-size:20px;margin:0 0 4px}h2{font-size:14px;margin:18px 0 6px;border-bottom:1px solid #999;padding-bottom:2px}table{border-collapse:collapse;width:100%}th,td{border:1px solid #bbb;padding:4px 6px;text-align:left;vertical-align:top}th{background:#eee}.meta td{border:none;padding:2px 12px 2px 0}.muted{color:#666}</style></head><body>");
			sb.Append("<h1>").Append(E(department?.Name)).Append(" — Deployment Manifest</h1>");
			sb.Append("<div class=\"muted\">").Append(E(deployment.Name)).Append(" · ").Append(E(((DeploymentStatuses)deployment.Status).ToString())).Append("</div>");
			sb.Append("<table class=\"meta\"><tr><td><strong>Incident #</strong> ").Append(E(deployment.IncidentNumber)).Append("</td><td><strong>Resource order #</strong> ").Append(E(deployment.ResourceOrderNumber)).Append("</td><td><strong>Request #</strong> ").Append(E(deployment.RequestNumber)).Append("</td></tr>");
			sb.Append("<tr><td><strong>Cost code</strong> ").Append(E(deployment.CostCode)).Append("</td><td><strong>Point of hire</strong> ").Append(E(deployment.PointOfHire)).Append("</td><td><strong>Window</strong> ").Append(D(deployment.StartOn)).Append(" → ").Append(D(deployment.EndOn)).Append("</td></tr></table>");

			var activeUnits = deployment.Units.Where(u => u.IsActive).ToList();
			var activePeople = deployment.Personnel.Where(p => p.IsActive).ToList();
			sb.Append("<h2>Units</h2>");
			if (activeUnits.Count == 0) sb.Append("<p class=\"muted\">No units rostered.</p>");
			else
			{
				sb.Append("<table><tr><th>Unit</th><th>Type</th><th>Call sign</th><th>Crew</th><th>Notes</th></tr>");
				foreach (var unit in activeUnits)
				{
					units.TryGetValue(unit.UnitId, out var detail);
					sb.Append("<tr><td>").Append(E(detail?.Name ?? unit.UnitName ?? unit.UnitId.ToString())).Append("</td><td>").Append(E(detail?.Type)).Append("</td><td>").Append(E(unit.CallSign)).Append("</td><td>")
					  .Append(activePeople.Count(p => p.DeploymentUnitId == unit.DeploymentUnitId)).Append("</td><td>").Append(E(unit.Notes)).Append("</td></tr>");
				}
				sb.Append("</table>");
			}

			sb.Append("<h2>Personnel</h2>");
			if (activePeople.Count == 0) sb.Append("<p class=\"muted\">No personnel rostered.</p>");
			else
			{
				sb.Append("<table><tr><th>Name</th><th>Unit</th><th>Seat</th><th>Certification</th><th>Call sign</th><th>Fill</th></tr>");
				foreach (var person in activePeople.OrderBy(p => p.DeploymentUnitId ?? "~").ThenBy(p => p.DisplayName))
				{
					var unit = activeUnits.FirstOrDefault(u => u.DeploymentUnitId == person.DeploymentUnitId);
					var unitName = unit == null ? string.Empty : (units.TryGetValue(unit.UnitId, out var detail) ? detail.Name : unit.UnitName);
					sb.Append("<tr><td>").Append(E(person.DisplayName ?? person.UserId)).Append("</td><td>").Append(E(unitName)).Append("</td><td>")
					  .Append(E(person.UnitRoleId.HasValue && seatNames.TryGetValue(person.UnitRoleId.Value, out var seat) ? seat : string.Empty)).Append("</td><td>").Append(E(person.CertificationCode)).Append("</td><td>")
					  .Append(E(person.CallSign)).Append("</td><td>").Append(E(person.RmsExternalOrderFillId)).Append("</td></tr>");
				}
				sb.Append("</table>");
			}

			var activeEquipment = deployment.Equipment.Where(e => e.IsActive).ToList();
			if (activeEquipment.Count > 0)
			{
				sb.Append("<h2>Equipment</h2><table><tr><th>Item</th><th>Unit</th><th>Issued</th><th>Notes</th></tr>");
				foreach (var item in activeEquipment)
				{
					var unit = activeUnits.FirstOrDefault(u => u.DeploymentUnitId == item.DeploymentUnitId);
					var unitName = unit == null ? string.Empty : (units.TryGetValue(unit.UnitId, out var detail) ? detail.Name : unit.UnitName);
					sb.Append("<tr><td>").Append(E(item.FreeTextName ?? item.InventoryAssetId ?? item.InventoryItemId)).Append("</td><td>").Append(E(unitName)).Append("</td><td>").Append(D(item.IssuedOn)).Append("</td><td>").Append(E(item.Notes)).Append("</td></tr>");
				}
				sb.Append("</table>");
			}

			sb.Append("<p class=\"muted\">Generated ").Append(D(DateTime.UtcNow)).Append("</p></body></html>");
			return sb.ToString();
		}
	}
}
