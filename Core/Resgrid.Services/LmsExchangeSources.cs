using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Lms;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>An LMS skill sheet as the community exchange carries it (the sheet's published content and its grading; no eligibility rules).</summary>
	public sealed class LmsSkillSheetExchangePayload
	{
		public const string FormatName = "resgrid-skill-sheet";

		[JsonProperty("format")] public string Format { get; set; } = FormatName;
		[JsonProperty("version")] public int Version { get; set; } = 1;
		[JsonProperty("code")] public string Code { get; set; }
		[JsonProperty("title")] public string Title { get; set; }
		[JsonProperty("description")] public string Description { get; set; }
		[JsonProperty("kind")] public int Kind { get; set; }
		[JsonProperty("allowCrewEvaluation")] public bool AllowCrewEvaluation { get; set; }
		[JsonProperty("content")] public LmsSkillSheetContent Content { get; set; }
		[JsonProperty("passThreshold")] public decimal PassThreshold { get; set; }
		[JsonProperty("defaultMinutes")] public int DefaultMinutes { get; set; }
		[JsonProperty("timeLimitSeconds")] public int TimeLimitSeconds { get; set; }

		public static LmsSkillSheetExchangePayload Parse(string json)
		{
			try
			{
				var payload = string.IsNullOrWhiteSpace(json) ? null : JsonConvert.DeserializeObject<LmsSkillSheetExchangePayload>(json, new JsonSerializerSettings { MaxDepth = 32 });
				return payload != null && payload.Format == FormatName && !string.IsNullOrWhiteSpace(payload.Title) ? payload : null;
			}
			catch (JsonException)
			{
				return null;
			}
		}
	}

	/// <summary>An LMS lesson plan as the community exchange carries it (its published content; competency links are the department's own and are left out).</summary>
	public sealed class LmsLessonPlanExchangePayload
	{
		public const string FormatName = "resgrid-lesson-plan";

		[JsonProperty("format")] public string Format { get; set; } = FormatName;
		[JsonProperty("version")] public int Version { get; set; } = 1;
		[JsonProperty("title")] public string Title { get; set; }
		[JsonProperty("content")] public LmsLessonPlanContent Content { get; set; }

		public static LmsLessonPlanExchangePayload Parse(string json)
		{
			try
			{
				var payload = string.IsNullOrWhiteSpace(json) ? null : JsonConvert.DeserializeObject<LmsLessonPlanExchangePayload>(json, new JsonSerializerSettings { MaxDepth = 32 });
				return payload != null && payload.Format == FormatName && !string.IsNullOrWhiteSpace(payload.Title) ? payload : null;
			}
			catch (JsonException)
			{
				return null;
			}
		}
	}

	/// <summary>
	/// LMS skill sheets on the free community exchange (decision 7c). Sharing a published sheet needs the right to manage courses (or department
	/// admin); importing lands it as the department's own draft sheet, which needs the LMS add-on (the LMS gate's author check, §13a.4).
	/// </summary>
	public class LmsSkillSheetExchangeSource : IExchangeItemSource
	{
		private readonly ILmsSkillSheetService _sheets;
		private readonly ILmsGateService _gate;
		private readonly IContentAttributionsRepository _attributions;

		public LmsSkillSheetExchangeSource(ILmsSkillSheetService sheets, ILmsGateService gate, IContentAttributionsRepository attributions)
		{
			_sheets = sheets;
			_gate = gate;
			_attributions = attributions;
		}

		public ExchangeItemKinds Kind => ExchangeItemKinds.SkillSheet;

		public bool NeedsLms => true;

		/// <summary>The picker shows to training managers; the LMS right to manage courses is checked when the item is prepared.</summary>
		public bool CanSubmit(ExchangeActor actor) => actor != null && actor.IsCurrentMember && (actor.IsDepartmentAdmin || actor.CanCreateTrainings);

		private async Task<bool> MayManageAsync(ExchangeActor actor)
		{
			var lms = await _gate.GetActorAsync(actor.DepartmentId, actor.UserId);
			return lms != null && lms.MayManage;
		}

		public async Task<bool> CanImportAsync(ExchangeActor actor)
		{
			if (actor == null || !actor.IsCurrentMember)
				return false;
			var lms = await _gate.GetActorAsync(actor.DepartmentId, actor.UserId);
			return lms != null && lms.MayManage && lms.Gate != null && lms.Gate.CanAuthor;
		}

		public async Task<List<ExchangeSourceItem>> GetSubmittableAsync(ExchangeActor actor)
		{
			return (await _sheets.GetSheetsAsync(actor.DepartmentId) ?? new List<LmsSkillSheet>())
				.Where(s => s.DepartmentId == actor.DepartmentId)
				.OrderBy(s => s.Title, StringComparer.CurrentCultureIgnoreCase)
				.Select(s => new ExchangeSourceItem
				{
					ItemKind = (int)ExchangeItemKinds.SkillSheet,
					SourceRef = s.LmsSkillSheetId.ToString(CultureInfo.InvariantCulture),
					Title = (string.IsNullOrWhiteSpace(s.Code) ? string.Empty : s.Code + " ") + s.Title,
					Blocked = s.CurrentVersionId.HasValue ? ExchangeErrors.None : ExchangeErrors.NotPublished
				}).ToList();
		}

		public async Task<ExchangePreparedItem> PrepareAsync(ExchangeActor actor, string sourceRef)
		{
			if (!int.TryParse(sourceRef, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) || id <= 0)
				return new ExchangePreparedItem { Error = ExchangeErrors.NotFound };
			if (!await MayManageAsync(actor))
				return new ExchangePreparedItem { Error = ExchangeErrors.NotAuthorized };
			var sheet = await _sheets.GetSheetAsync(actor.DepartmentId, id);
			if (sheet == null || sheet.DepartmentId != actor.DepartmentId)
				return new ExchangePreparedItem { Error = ExchangeErrors.NotFound };
			var version = await _sheets.GetPublishedVersionAsync(actor.DepartmentId, id);
			if (version == null)
				return new ExchangePreparedItem { Error = ExchangeErrors.NotPublished };

			var scrubber = new ExchangeScrubber(actor.DepartmentName);
			if (!string.IsNullOrWhiteSpace(sheet.EvaluatorEligibilityJson) || !string.IsNullOrWhiteSpace(sheet.NextSignerEligibilityJson) || sheet.CategoryId.HasValue)
				scrubber.Flag(ExchangeReviewFlagKinds.ConfigurationRemoved);
			var content = LmsSkillSheetContent.Parse(version.ContentJson) ?? new LmsSkillSheetContent();
			foreach (var step in content.Steps ?? new List<LmsSkillStep>())
			{
				step.Label = scrubber.Text(step.Label);
				step.Help = scrubber.Text(step.Help);
			}

			var payload = new LmsSkillSheetExchangePayload
			{
				Code = sheet.Code,
				Title = scrubber.Text(sheet.Title),
				Description = scrubber.Text(sheet.Description),
				Kind = sheet.Kind,
				AllowCrewEvaluation = sheet.AllowCrewEvaluation,
				Content = content,
				PassThreshold = version.PassThreshold,
				DefaultMinutes = version.DefaultMinutes,
				TimeLimitSeconds = version.TimeLimitSeconds
			};
			return new ExchangePreparedItem
			{
				ItemKind = (int)ExchangeItemKinds.SkillSheet,
				SourceRef = id.ToString(CultureInfo.InvariantCulture),
				Title = payload.Title,
				Summary = payload.Description,
				VersionLabel = version.Major.ToString(CultureInfo.InvariantCulture) + "." + version.Minor.ToString(CultureInfo.InvariantCulture),
				PayloadJson = JsonConvert.SerializeObject(payload, Formatting.None),
				Flags = scrubber.Flags
			};
		}

		public string PreviewHtml(string payloadJson)
		{
			var payload = LmsSkillSheetExchangePayload.Parse(payloadJson);
			if (payload == null)
				return string.Empty;
			var html = new StringBuilder();
			if (!string.IsNullOrWhiteSpace(payload.Description))
				html.Append("<p>").Append(WebUtility.HtmlEncode(payload.Description)).Append("</p>");
			html.Append("<ol class=\"rgx-steps\">");
			foreach (var step in payload.Content?.Steps ?? new List<LmsSkillStep>())
				html.Append("<li>").Append(WebUtility.HtmlEncode(step?.Label ?? string.Empty)).Append(step != null && step.Critical ? " <strong>(!)</strong>" : string.Empty).Append("</li>");
			html.Append("</ol>");
			return html.ToString();
		}

		public async Task<int?> ImportAsync(ExchangeActor actor, ExchangeListing listing, ContentAttribution attribution, CancellationToken cancellationToken = default)
		{
			var payload = LmsSkillSheetExchangePayload.Parse(listing.PayloadJson);
			var lms = await _gate.GetActorAsync(actor.DepartmentId, actor.UserId);
			if (payload == null || lms == null)
				return null;

			var sheet = await _sheets.CreateAsync(lms, new LmsSkillSheetInput
			{
				Code = payload.Code,
				Title = payload.Title,
				Description = payload.Description,
				Kind = Enum.IsDefined(typeof(LmsSkillSheetKinds), payload.Kind) ? (LmsSkillSheetKinds)payload.Kind : default,
				AllowCrewEvaluation = payload.AllowCrewEvaluation
			}, cancellationToken);
			if (sheet == null)
				return null;

			await _sheets.SaveDraftAsync(lms, sheet.LmsSkillSheetId, new LmsSkillSheetDraftInput
			{
				Content = payload.Content ?? new LmsSkillSheetContent(),
				PassThreshold = payload.PassThreshold > 0 ? payload.PassThreshold : 100m,
				DefaultMinutes = payload.DefaultMinutes > 0 ? payload.DefaultMinutes : 30,
				TimeLimitSeconds = Math.Max(0, payload.TimeLimitSeconds)
			}, cancellationToken);

			await KeepAttributionAsync(_attributions, attribution, ContentAttributionSubjects.SkillSheet, sheet.LmsSkillSheetId, actor, cancellationToken);
			return sheet.LmsSkillSheetId;
		}

		internal static async Task KeepAttributionAsync(IContentAttributionsRepository attributions, ContentAttribution attribution, ContentAttributionSubjects subject, int id,
			ExchangeActor actor, CancellationToken cancellationToken)
		{
			try
			{
				var row = attribution.CopyFor(subject, id, actor.UserId, DateTime.UtcNow);
				row.DepartmentId = actor.DepartmentId;
				await attributions.SaveForSubjectAsync(row, cancellationToken);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "LMS exchange attribution could not be kept.");
			}
		}
	}

	/// <summary>
	/// LMS lesson plans on the free community exchange (decision 7c). Sharing a published plan needs the class management right (department admin,
	/// Manage Courses or Assign Training); importing lands it as the department's own draft plan, which needs the LMS add-on.
	/// </summary>
	public class LmsLessonPlanExchangeSource : IExchangeItemSource
	{
		private readonly ILmsLessonPlanService _plans;
		private readonly ILmsGateService _gate;
		private readonly IContentAttributionsRepository _attributions;

		public LmsLessonPlanExchangeSource(ILmsLessonPlanService plans, ILmsGateService gate, IContentAttributionsRepository attributions)
		{
			_plans = plans;
			_gate = gate;
			_attributions = attributions;
		}

		public ExchangeItemKinds Kind => ExchangeItemKinds.LessonPlan;

		public bool NeedsLms => true;

		/// <summary>The picker shows to training managers; the class management right (admin, Manage Courses, Assign Training) is checked when prepared.</summary>
		public bool CanSubmit(ExchangeActor actor) => actor != null && actor.IsCurrentMember && (actor.IsDepartmentAdmin || actor.CanCreateTrainings);

		private async Task<bool> MayManageAsync(ExchangeActor actor)
		{
			var lms = await _gate.GetActorAsync(actor.DepartmentId, actor.UserId);
			return lms != null && lms.IsCurrentMember && (lms.IsDepartmentAdmin || lms.CanManageCourses || lms.CanAssignTraining);
		}

		public async Task<bool> CanImportAsync(ExchangeActor actor)
		{
			if (actor == null || !actor.IsCurrentMember || !await MayManageAsync(actor))
				return false;
			var lms = await _gate.GetActorAsync(actor.DepartmentId, actor.UserId);
			return lms?.Gate != null && lms.Gate.CanAuthor;
		}

		public async Task<List<ExchangeSourceItem>> GetSubmittableAsync(ExchangeActor actor)
		{
			return (await _plans.GetPlansAsync(actor.DepartmentId) ?? new List<LmsLessonPlan>())
				.Where(p => p.DepartmentId == actor.DepartmentId && !p.IsDeleted)
				.OrderBy(p => p.Title, StringComparer.CurrentCultureIgnoreCase)
				.Select(p => new ExchangeSourceItem
				{
					ItemKind = (int)ExchangeItemKinds.LessonPlan,
					SourceRef = p.LmsLessonPlanId.ToString(CultureInfo.InvariantCulture),
					Title = p.Title,
					Blocked = p.CurrentVersionId.HasValue ? ExchangeErrors.None : ExchangeErrors.NotPublished
				}).ToList();
		}

		public async Task<ExchangePreparedItem> PrepareAsync(ExchangeActor actor, string sourceRef)
		{
			if (!int.TryParse(sourceRef, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) || id <= 0)
				return new ExchangePreparedItem { Error = ExchangeErrors.NotFound };
			if (!await MayManageAsync(actor))
				return new ExchangePreparedItem { Error = ExchangeErrors.NotAuthorized };
			var plan = await _plans.GetPlanAsync(actor.DepartmentId, id);
			if (plan == null || plan.DepartmentId != actor.DepartmentId || plan.IsDeleted)
				return new ExchangePreparedItem { Error = ExchangeErrors.NotFound };
			if (!plan.CurrentVersionId.HasValue)
				return new ExchangePreparedItem { Error = ExchangeErrors.NotPublished };
			var version = await _plans.GetVersionAsync(actor.DepartmentId, plan.CurrentVersionId.Value);
			if (version == null)
				return new ExchangePreparedItem { Error = ExchangeErrors.NotPublished };

			var scrubber = new ExchangeScrubber(actor.DepartmentName);
			var content = LmsLessonPlanContent.Parse(version.ContentJson);
			if (content.CompetencyItemIds != null && content.CompetencyItemIds.Count > 0)
				scrubber.Flag(ExchangeReviewFlagKinds.ConfigurationRemoved);
			content.CompetencyItemIds = new List<int>();
			List<string> Clean(List<string> items) => (items ?? new List<string>()).Select(scrubber.Text).ToList();
			content.Objectives = Clean(content.Objectives);
			content.Materials = Clean(content.Materials);
			content.Props = Clean(content.Props);
			content.PreBriefChecklist = Clean(content.PreBriefChecklist);
			content.Roles = Clean(content.Roles);
			content.SafetyPlan = scrubber.Text(content.SafetyPlan);
			content.Notes = scrubber.Text(content.Notes);

			var payload = new LmsLessonPlanExchangePayload { Title = scrubber.Text(version.Title ?? plan.Title), Content = content };
			return new ExchangePreparedItem
			{
				ItemKind = (int)ExchangeItemKinds.LessonPlan,
				SourceRef = id.ToString(CultureInfo.InvariantCulture),
				Title = payload.Title,
				Summary = content.Objectives.Count > 0 ? string.Join("; ", content.Objectives.Take(3)) : null,
				VersionLabel = version.Major.ToString(CultureInfo.InvariantCulture) + "." + version.Minor.ToString(CultureInfo.InvariantCulture),
				PayloadJson = JsonConvert.SerializeObject(payload, Formatting.None),
				Flags = scrubber.Flags
			};
		}

		public string PreviewHtml(string payloadJson)
		{
			var payload = LmsLessonPlanExchangePayload.Parse(payloadJson);
			if (payload?.Content == null)
				return string.Empty;
			var html = new StringBuilder();
			void List(IEnumerable<string> items)
			{
				var list = (items ?? Enumerable.Empty<string>()).Where(i => !string.IsNullOrWhiteSpace(i)).ToList();
				if (list.Count == 0)
					return;
				html.Append("<ul>");
				foreach (var item in list)
					html.Append("<li>").Append(WebUtility.HtmlEncode(item)).Append("</li>");
				html.Append("</ul>");
			}
			List(payload.Content.Objectives);
			List(payload.Content.Materials);
			if (!string.IsNullOrWhiteSpace(payload.Content.SafetyPlan))
				html.Append("<p>").Append(WebUtility.HtmlEncode(payload.Content.SafetyPlan)).Append("</p>");
			return html.ToString();
		}

		public async Task<int?> ImportAsync(ExchangeActor actor, ExchangeListing listing, ContentAttribution attribution, CancellationToken cancellationToken = default)
		{
			var payload = LmsLessonPlanExchangePayload.Parse(listing.PayloadJson);
			if (payload == null)
				return null;
			var content = payload.Content ?? new LmsLessonPlanContent();
			content.CompetencyItemIds = new List<int>();
			var plan = await _plans.CreateAsync(actor.DepartmentId, payload.Title, content, actor.UserId, cancellationToken);
			if (plan == null)
				return null;
			await LmsSkillSheetExchangeSource.KeepAttributionAsync(_attributions, attribution, ContentAttributionSubjects.LessonPlan, plan.LmsLessonPlanId, actor, cancellationToken);
			return plan.LmsLessonPlanId;
		}
	}
}
