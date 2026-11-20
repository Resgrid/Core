using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Newtonsoft.Json;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Models.IntakeChannels;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// Department → Intake Channels (call-intake-and-ai-destinations-plan.md, M0323): the addresses whose messages become pending
	/// calls (SMS / voice on the dedicated number, an intake email address, a linked Slack / Teams / Telegram / Discord / Google Chat
	/// chat, a WhatsApp sender, a signed webhook). Department administrators manage them; anyone who can see calls can read where a
	/// pending call came from (CallSources). Channels can be set up before the Intake.Channels rollout reaches the department.
	/// </summary>
	[Area("User")]
	[Authorize]
	[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
	public class IntakeChannelsController : SecureBaseController
	{
		private readonly IIntakeChannelsService _channels;
		private readonly IIntakeService _intake;
		private readonly ICallsService _calls;
		private readonly IFeatureToggleService _flags;
		private readonly IEventAggregator _events;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.Department.IntakeChannels> _localizer;

		public IntakeChannelsController(IIntakeChannelsService channels, IIntakeService intake, ICallsService calls, IFeatureToggleService flags,
			IEventAggregator events, IStringLocalizer<Resgrid.Localization.Areas.User.Department.IntakeChannels> localizer)
		{
			_channels = channels;
			_intake = intake;
			_calls = calls;
			_flags = flags;
			_events = events;
			_localizer = localizer;
		}

		/// <summary>Channel types an admin creates here, in display order (voice and SMS come from the dedicated number too).</summary>
		public static readonly IntakeChannelTypes[] CreatableTypes =
		{
			IntakeChannelTypes.Email, IntakeChannelTypes.Webhook, IntakeChannelTypes.Slack, IntakeChannelTypes.Teams, IntakeChannelTypes.Telegram,
			IntakeChannelTypes.Discord, IntakeChannelTypes.GoogleChat, IntakeChannelTypes.WhatsApp, IntakeChannelTypes.Sms, IntakeChannelTypes.Voice
		};

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> Index()
		{
			var now = DateTime.UtcNow;
			var counts = await _intake.GetMonthlyChannelCountsAsync(DepartmentId, now) ?? new Dictionary<string, int>();
			var voiceCount = 0;
			var channels = await _channels.GetChannelsForDepartmentAsync(DepartmentId);
			if (channels.Any(c => c.ChannelType == (int)IntakeChannelTypes.Voice))
				voiceCount = await _intake.GetMonthlyCountAsync(DepartmentId, IntakeChannelTypes.Voice, now);

			var model = new IntakeChannelsIndexView
			{
				FeatureEnabled = await _flags.IsEnabledAsync(FeatureFlagKeys.IntakeChannels, DepartmentId),
				Message = TempData?["IntakeChannelsMessage"] as string,
				ErrorMessage = TempData?["IntakeChannelsError"] as string
			};

			foreach (var channel in channels)
			{
				model.Channels.Add(new IntakeChannelRow
				{
					IntakeChannelId = channel.IntakeChannelId,
					Name = channel.Name,
					ChannelType = (IntakeChannelTypes)channel.ChannelType,
					Address = channel.ChannelType == (int)IntakeChannelTypes.Webhook ? null : channel.Address,
					ProcessingMode = (IntakeProcessingModes)channel.ProcessingMode,
					IsEnabled = channel.IsEnabled,
					ThisMonth = channel.ChannelType == (int)IntakeChannelTypes.Voice ? voiceCount : counts.GetValueOrDefault(channel.IntakeChannelId)
				});
			}

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> Edit(string id, int? type)
		{
			var model = new IntakeChannelEditView();
			if (!string.IsNullOrWhiteSpace(id))
			{
				var channel = await _channels.GetChannelByIdAsync(DepartmentId, id);
				if (channel == null)
					return NotFound();

				model = ToView(channel);
			}
			else
			{
				model.ChannelType = type.HasValue && CreatableTypes.Contains((IntakeChannelTypes)type.Value) ? type.Value : (int)IntakeChannelTypes.Email;
			}

			return View(await WithLookupsAsync(model));
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> Edit(IntakeChannelEditView model, CancellationToken cancellationToken)
		{
			if (model == null)
				return BadRequest();

			IntakeChannel before = null;
			if (!model.IsNew)
			{
				before = await _channels.GetChannelByIdAsync(DepartmentId, model.IntakeChannelId);
				if (before == null)
					return NotFound();
				before = Snapshot(before);
			}

			IntakeChannel saved;
			try
			{
				saved = await _channels.SaveChannelAsync(new IntakeChannel
				{
					IntakeChannelId = model.IsNew ? null : model.IntakeChannelId,
					DepartmentId = DepartmentId,
					Name = model.Name,
					ChannelType = model.ChannelType,
					Address = model.Address,
					ProcessingMode = model.ProcessingMode,
					TemplateFormat = model.TemplateFormat,
					AllowedSenders = model.AllowedSenders,
					DefaultPriority = model.DefaultPriority,
					DefaultCallTypeId = model.DefaultCallTypeId,
					SendAcknowledgement = model.SendAcknowledgement,
					IsEnabled = model.IsEnabled
				}, UserId, cancellationToken);
			}
			catch (IntakeChannelValidationException ex)
			{
				model.ErrorMessage = ErrorText(ex.Code);
				return View(await WithLookupsAsync(model));
			}

			Audit(before, saved);

			// A new webhook gets its secret now, shown on this response only (never through a redirect or TempData cookie).
			if (model.IsNew && saved.ChannelType == (int)IntakeChannelTypes.Webhook)
			{
				var secret = await _channels.RotateWebhookSecretAsync(DepartmentId, saved.IntakeChannelId, UserId, cancellationToken);
				return View("Webhook", await WebhookViewAsync(saved.IntakeChannelId, secret));
			}

			TempData["IntakeChannelsMessage"] = _localizer["Saved"].Value;
			return RedirectToAction(nameof(Index));
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> Webhook(string id)
		{
			var model = await WebhookViewAsync(id, null);
			return model == null ? NotFound() : View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> RotateSecret(string id, CancellationToken cancellationToken)
		{
			var channel = await _channels.GetChannelByIdAsync(DepartmentId, id);
			if (channel == null || channel.ChannelType != (int)IntakeChannelTypes.Webhook)
				return NotFound();

			var secret = await _channels.RotateWebhookSecretAsync(DepartmentId, id, UserId, cancellationToken);
			Audit(Snapshot(channel), Snapshot(channel), "WebhookSecretRotated");
			return View("Webhook", await WebhookViewAsync(id, secret));
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> SetEnabled(string id, bool enabled, CancellationToken cancellationToken)
		{
			var before = await _channels.GetChannelByIdAsync(DepartmentId, id);
			if (before == null)
				return NotFound();

			before = Snapshot(before);
			try
			{
				var saved = await _channels.SetEnabledAsync(DepartmentId, id, enabled, UserId, cancellationToken);
				Audit(before, saved);
				TempData["IntakeChannelsMessage"] = _localizer["Saved"].Value;
			}
			catch (IntakeChannelValidationException ex)
			{
				TempData["IntakeChannelsError"] = ErrorText(ex.Code);
			}

			return RedirectToAction(nameof(Index));
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
		{
			var before = await _channels.GetChannelByIdAsync(DepartmentId, id);
			if (before == null)
				return NotFound();

			before = Snapshot(before);
			if (await _channels.DeleteChannelAsync(DepartmentId, id, UserId, cancellationToken))
			{
				Audit(before, null);
				TempData["IntakeChannelsMessage"] = _localizer["Deleted"].Value;
			}

			return RedirectToAction(nameof(Index));
		}

		/// <summary>
		/// Where these calls came from, for the pending calls list: { "callId": "via Text message (SMS): Hospital line" }. Calls of other
		/// departments and calls that did not come from intake are left out.
		/// </summary>
		[HttpGet]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<IActionResult> CallSources(string callIds)
		{
			var ids = (callIds ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries)
				.Select(v => int.TryParse(v.Trim(), out var id) ? id : 0).Where(id => id > 0).Distinct().Take(200).ToList();
			var sources = await _intake.GetCallSourcesAsync(DepartmentId, ids);
			return Json(sources.ToDictionary(s => s.Key.ToString(), s => SourceLabel(_localizer, s.Value)));
		}

		/// <summary>"via Text message (SMS): Hospital line" (or "via …" when the channel has no name).</summary>
		public static string SourceLabel(IStringLocalizer localizer, IntakeCallSource source)
		{
			if (source == null)
				return null;

			var type = localizer["Type." + ((IntakeChannelTypes)source.ChannelType)].Value;
			return string.IsNullOrWhiteSpace(source.ChannelName) ? localizer["ViaType", type].Value : localizer["Via", type, source.ChannelName].Value;
		}

		/// <summary>The public webhook URL for a channel token (the API host serves it).</summary>
		public static string WebhookUrl(string token) =>
			(Config.SystemBehaviorConfig.ResgridApiBaseUrl ?? string.Empty).TrimEnd('/') + "/api/v4/Intake/Webhook/" + token;

		private async Task<IntakeWebhookView> WebhookViewAsync(string id, string secret)
		{
			var channel = await _channels.GetChannelByIdAsync(DepartmentId, id);
			if (channel == null || channel.ChannelType != (int)IntakeChannelTypes.Webhook)
				return null;

			return new IntakeWebhookView
			{
				IntakeChannelId = channel.IntakeChannelId,
				Name = channel.Name,
				Url = WebhookUrl(channel.Address),
				Secret = secret,
				HasSecret = !string.IsNullOrWhiteSpace(channel.WebhookSecretProtected),
				ToleranceSeconds = Config.IntakeConfig.WebhookTimestampToleranceSeconds
			};
		}

		private async Task<IntakeChannelEditView> WithLookupsAsync(IntakeChannelEditView model)
		{
			model.EmailDomain = Config.InboundEmailConfig.DispatchDomain;
			model.Priorities = (await _calls.GetActiveCallPrioritiesForDepartmentAsync(DepartmentId) ?? new List<DepartmentCallPriority>())
				.Where(p => p != null && !p.IsDeleted).OrderBy(p => p.Sort)
				.Select(p => new KeyValuePair<int, string>(p.DepartmentCallPriorityId, p.Name)).ToList();
			model.CallTypes = (await _calls.GetCallTypesForDepartmentAsync(DepartmentId) ?? new List<CallType>())
				.Where(t => t != null && !string.IsNullOrWhiteSpace(t.Type)).OrderBy(t => t.Type)
				.Select(t => new KeyValuePair<int, string>(t.CallTypeId, t.Type)).ToList();
			model.TemplateFormats = Enum.GetValues(typeof(CallEmailTypes)).Cast<CallEmailTypes>()
				.Where(f => f != CallEmailTypes.AI)
				.Select(f => new KeyValuePair<int, string>((int)f, f.ToString())).ToList();
			return model;
		}

		private static IntakeChannelEditView ToView(IntakeChannel channel) => new IntakeChannelEditView
		{
			IntakeChannelId = channel.IntakeChannelId,
			Name = channel.Name,
			ChannelType = channel.ChannelType,
			Address = channel.ChannelType == (int)IntakeChannelTypes.Webhook ? null : channel.Address,
			ProcessingMode = channel.ProcessingMode,
			TemplateFormat = channel.TemplateFormat,
			AllowedSenders = channel.AllowedSenders?.Replace(",", ", "),
			DefaultPriority = channel.DefaultPriority,
			DefaultCallTypeId = channel.DefaultCallTypeId,
			SendAcknowledgement = channel.SendAcknowledgement,
			IsEnabled = channel.IsEnabled
		};

		private string ErrorText(string code)
		{
			switch (code)
			{
				case "InvalidAddress":
					return _localizer["Error.InvalidAddress"].Value;
				case "AddressInUse":
					return _localizer["Error.AddressInUse"].Value;
				case "NameRequired":
					return _localizer["Error.NameRequired"].Value;
				default:
					return _localizer["Error.Invalid"].Value;
			}
		}

		/// <summary>The channel as audited: everything but the webhook secret.</summary>
		private static IntakeChannel Snapshot(IntakeChannel channel)
		{
			if (channel == null)
				return null;

			var copy = JsonConvert.DeserializeObject<IntakeChannel>(JsonConvert.SerializeObject(channel));
			copy.WebhookSecretProtected = string.IsNullOrWhiteSpace(channel.WebhookSecretProtected) ? null : "(set)";
			return copy;
		}

		private void Audit(IntakeChannel before, IntakeChannel after, string change = null)
		{
			_events.SendMessage(new AuditEvent
			{
				DepartmentId = DepartmentId,
				UserId = UserId,
				Type = AuditLogTypes.DepartmentSettingsChanged,
				Before = before == null ? null : JsonConvert.SerializeObject(new { IntakeChannel = Snapshot(before) }),
				After = after == null ? null : JsonConvert.SerializeObject(new { IntakeChannel = Snapshot(after), Change = change }),
				Successful = true,
				IpAddress = IpAddressHelper.GetRequestIP(Request, true),
				ServerName = Environment.MachineName,
				UserAgent = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}"
			});
		}
	}
}
