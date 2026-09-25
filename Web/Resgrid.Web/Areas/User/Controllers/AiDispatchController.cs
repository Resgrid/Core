using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Resgrid.Model;
using Resgrid.Model.AiDispatch;
using Resgrid.Model.Events;
using Resgrid.Model.Helpers;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Models.AiDispatch;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// Department-admin AI dispatch settings and activity viewer (enhanced-ai-addon-plan.md §4). Hidden (404) unless the host has AI
	/// dispatch enabled and the department is in the Ai.Enhanced / Dispatch.AiTemplate rollout. Settings can be saved before the
	/// add-on is bought; nothing runs until status is Running.
	/// </summary>
	[Area("User"), Authorize(Policy = ResgridResources.Department_Update), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
	public sealed class AiDispatchController : SecureBaseController
	{
		private readonly IAiDispatchAdminService _admin;
		private readonly IDepartmentsService _departments;
		private readonly IEventAggregator _events;

		public AiDispatchController(IAiDispatchAdminService admin, IDepartmentsService departments, IEventAggregator events)
		{
			_admin = admin;
			_departments = departments;
			_events = events;
		}

		private async Task<AiDispatchStatus> VisibleStatusAsync()
		{
			var status = await _admin.GetStatusAsync(DepartmentId);
			return status.HostEnabled && status.RolledOut ? status : null;
		}

		[HttpGet]
		public async Task<IActionResult> Index(bool saved = false, CancellationToken cancellationToken = default)
		{
			var status = await VisibleStatusAsync();
			if (status == null)
				return NotFound();
			var model = await BuildAsync(status, await _admin.GetSettingsAsync(DepartmentId, cancellationToken), cancellationToken);
			model.Saved = saved;
			return View(model);
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> Index(AiDispatchSettingsView input, CancellationToken cancellationToken)
		{
			var status = await VisibleStatusAsync();
			if (status == null)
				return NotFound();
			if (input == null)
				return BadRequest();

			var before = await _admin.GetSettingsAsync(DepartmentId, cancellationToken);
			var settings = new DepartmentAiDispatchConfig
			{
				MinimumConfidence = input.MinimumConfidencePercent.HasValue ? input.MinimumConfidencePercent.Value / 100m : null,
				SenderAllowlist = input.SenderAllowlist, MonthlyTokenCap = input.MonthlyTokenCap, AuditRetentionDays = input.AuditRetentionDays,
				FillCallType = input.FillCallType, FillAddress = input.FillAddress, FillContact = input.FillContact, FillIncidentNumber = input.FillIncidentNumber,
				RenamePlaceholder = input.RenamePlaceholder, AddSummaryNote = input.AddSummaryNote, FlagRelatedCalls = input.FlagRelatedCalls
			};
			var errors = await _admin.SaveSettingsAsync(DepartmentId, settings, input.Revision, UserId, cancellationToken);
			if (errors.Count > 0)
			{
				// A conflict reloads what the other admin saved so it can be reviewed; a validation error keeps what was typed.
				var model = errors.Contains("Conflict")
					? await BuildAsync(status, await _admin.GetSettingsAsync(DepartmentId, cancellationToken), cancellationToken)
					: await BuildAsync(status, settings, cancellationToken, input.Revision);
				if (!errors.Contains("Conflict"))
					model.MinimumConfidencePercent = input.MinimumConfidencePercent;
				model.Errors = errors.ToList();
				return View(model);
			}

			_events.SendMessage(new AuditEvent
			{
				DepartmentId = DepartmentId, UserId = UserId, Type = AuditLogTypes.AiDispatchSettingsUpdated,
				Before = JsonConvert.SerializeObject(Snapshot(before)), After = JsonConvert.SerializeObject(Snapshot(settings)), Successful = true,
				IpAddress = IpAddressHelper.GetRequestIP(Request, true), ServerName = Environment.MachineName,
				UserAgent = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}"
			});
			return RedirectToAction(nameof(Index), new { saved = true });
		}

		[HttpGet]
		public async Task<IActionResult> Activity(int take = 100, CancellationToken cancellationToken = default)
		{
			var status = await VisibleStatusAsync();
			if (status == null)
				return NotFound();
			take = take is 50 or 100 or 200 ? take : 100;
			var items = await _admin.GetAuditAsync(DepartmentId, take, cancellationToken);
			// Totals come from the department's whole 30-day audit, not from the page of rows the list shows.
			var recent = await _admin.GetRecentOutcomeCountsAsync(DepartmentId, 30, cancellationToken);
			return View(new AiDispatchActivityView
			{
				Status = status, Department = await _departments.GetDepartmentByIdAsync(DepartmentId), Items = items, Take = take,
				EnrichedLast30Days = recent.GetValueOrDefault(AiDispatchOutcomes.Applied),
				SkippedLast30Days = recent.Where(c => c.Key != AiDispatchOutcomes.Applied && c.Key != AiDispatchOutcomes.InProgress).Sum(c => c.Value),
				MonthlyUsage = await _admin.GetMonthlyUsageAsync(DepartmentId, cancellationToken),
				MonthlyTokenCap = (await _admin.GetSettingsAsync(DepartmentId, cancellationToken)).MonthlyTokenCap
			});
		}

		private async Task<AiDispatchSettingsView> BuildAsync(AiDispatchStatus status, DepartmentAiDispatchConfig settings, CancellationToken cancellationToken, long? revision = null)
		{
			var department = await _departments.GetDepartmentByIdAsync(DepartmentId);
			return new AiDispatchSettingsView
			{
				Status = status,
				MinimumConfidencePercent = settings.MinimumConfidence.HasValue ? (int)Math.Round(settings.MinimumConfidence.Value * 100m) : null,
				SenderAllowlist = settings.SenderAllowlist, MonthlyTokenCap = settings.MonthlyTokenCap, AuditRetentionDays = settings.AuditRetentionDays,
				FillCallType = settings.FillCallType, FillAddress = settings.FillAddress, FillContact = settings.FillContact, FillIncidentNumber = settings.FillIncidentNumber,
				RenamePlaceholder = settings.RenamePlaceholder, AddSummaryNote = settings.AddSummaryNote, FlagRelatedCalls = settings.FlagRelatedCalls,
				Revision = revision ?? settings.Revision,
				MonthlyUsage = await _admin.GetMonthlyUsageAsync(DepartmentId, cancellationToken),
				DefaultConfidencePercent = (int)Math.Round(AiDispatchSettingsPolicy.EffectiveMinimumConfidence(null, Config.AiDispatchConfig.MinimumConfidence) * 100),
				UpdatedOnLocal = settings.UpdatedOnUtc.HasValue ? settings.UpdatedOnUtc.Value.TimeConverter(department) : null
			};
		}

		// What an audit reviewer needs to see changed; the allowlist is sender addresses, never message content.
		private static object Snapshot(DepartmentAiDispatchConfig s) => new
		{
			s.MinimumConfidence, s.SenderAllowlist, s.MonthlyTokenCap, s.AuditRetentionDays, s.FillCallType, s.FillAddress, s.FillContact,
			s.FillIncidentNumber, s.RenamePlaceholder, s.AddSummaryNote, s.FlagRelatedCalls
		};
	}
}
