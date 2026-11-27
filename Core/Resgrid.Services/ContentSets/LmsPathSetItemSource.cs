using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Lms;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.ContentSets
{
	/// <summary>
	/// LMS learning paths in a course set (Training T5, round 10 A3). LMS-type, like courses. A path is ordered steps over courses and
	/// trainings, so its department-course and training steps must be items of the same set (<see cref="GetDependenciesAsync"/>, checked when
	/// the publisher adds the path) and the subscriber's path points at the subscriber's own managed copies of them; Academy course steps stay
	/// as they are (every department sees the same system course). Paths have no versions: the "version" is a hash of the name, description
	/// and steps, so a publisher's edit queues an update like a new version. The certification a path issues is a department id and is not
	/// carried. Paths are copied after the set's courses and trainings; a path whose step has no copy yet fails with "path_step_missing" and
	/// is filled in by a later sync. Each copy keeps a ContentSet attribution (subject LmsPath) and is locked in the path editor.
	/// </summary>
	public class LmsPathSetItemSource : IContentSetItemSource, IContentSetItemDependencies
	{
		public const string ProblemNotFound = "not_found";
		public const string ProblemStepMissing = "path_step_missing";

		private readonly ILmsPathsRepository _paths;
		private readonly ILmsPathStepsRepository _steps;
		private readonly ILmsCoursesRepository _courses;
		private readonly ITrainingService _trainings;
		private readonly IContentSetCopiesRepository _copies;
		private readonly IContentAttributionsRepository _attributions;

		public LmsPathSetItemSource(ILmsPathsRepository paths, ILmsPathStepsRepository steps, ILmsCoursesRepository courses, ITrainingService trainings,
			IContentSetCopiesRepository copies, IContentAttributionsRepository attributions)
		{
			_paths = paths;
			_steps = steps;
			_courses = courses;
			_trainings = trainings;
			_copies = copies;
			_attributions = attributions;
		}

		public ContentSetItemKinds Kind => ContentSetItemKinds.LmsPath;

		public bool NeedsLms => true;

		public ContentSetKinds SetKind => ContentSetKinds.Training;

		private async Task<LmsPath> OwnPathAsync(int departmentId, int lmsPathId)
		{
			var path = departmentId <= 0 || lmsPathId <= 0 ? null : await _paths.GetVisibleByIdAsync(departmentId, lmsPathId);
			return path == null || path.IsSystem || path.DepartmentId != departmentId ? null : path;
		}

		private async Task<List<LmsPathStep>> StepsAsync(int lmsPathId) =>
			((await _steps.GetForPathsAsync(new[] { lmsPathId })) ?? new List<LmsPathStep>()).Where(s => s.LmsPathId == lmsPathId).OrderBy(s => s.SortOrder).ToList();

		/// <summary>A step that names one of the department's own courses or trainings (it must travel with the path); Academy steps do not.</summary>
		public static bool IsLocalStep(LmsPathStep step) =>
			step != null && (step.Kind == (int)LmsPathStepKinds.Training || (step.Kind == (int)LmsPathStepKinds.Course && string.IsNullOrWhiteSpace(step.TargetSystemKey)));

		public static ContentSetItemKinds StepItemKind(LmsPathStep step) =>
			step.Kind == (int)LmsPathStepKinds.Training ? ContentSetItemKinds.Training : ContentSetItemKinds.LmsCourse;

		/// <summary>A positive, stable "version" of a path: its name, description and steps (targets as the publisher names them).</summary>
		public static int ContentVersion(LmsPath path, IEnumerable<LmsPathStep> steps)
		{
			if (path == null)
				return 0;
			var canonical = new StringBuilder();
			canonical.Append(path.Name ?? string.Empty).Append('\u001f').Append(path.Description ?? string.Empty);
			foreach (var step in (steps ?? Enumerable.Empty<LmsPathStep>()).OrderBy(s => s.SortOrder))
				canonical.Append('\u001e').Append(string.Join("|", step.SortOrder.ToString(CultureInfo.InvariantCulture), step.Kind.ToString(CultureInfo.InvariantCulture),
					step.TargetRefId.ToString(CultureInfo.InvariantCulture), step.TargetSystemKey ?? string.Empty, step.PrerequisiteOrders ?? string.Empty,
					step.ElectiveGroup?.ToString(CultureInfo.InvariantCulture) ?? string.Empty, step.ElectiveRequired.ToString(CultureInfo.InvariantCulture)));
			using var sha = SHA256.Create();
			var value = BitConverter.ToInt32(sha.ComputeHash(Encoding.UTF8.GetBytes(canonical.ToString())), 0) & int.MaxValue;
			return value == 0 ? 1 : value;
		}

		public async Task<List<ContentSetCandidate>> GetCandidatesAsync(int publisherDepartmentId) =>
			((await _paths.GetVisibleAsync(publisherDepartmentId)) ?? new List<LmsPath>())
				.Where(p => p != null && p.DepartmentId == publisherDepartmentId && !p.IsSystem && !p.IsDeleted).OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
				.Select(p => new ContentSetCandidate { Kind = Kind, ItemId = p.LmsPathId, Title = p.Name, NeedsLms = true }).ToList();

		public async Task<ContentSetSourceItem> DescribeAsync(int publisherDepartmentId, int itemId)
		{
			var path = await OwnPathAsync(publisherDepartmentId, itemId);
			if (path == null)
				return null;
			return new ContentSetSourceItem
			{
				Kind = Kind,
				ItemId = path.LmsPathId,
				Title = path.Name,
				VersionId = ContentVersion(path, await StepsAsync(path.LmsPathId)),
				EffectiveOn = path.UpdatedOn ?? path.CreatedOn,
				Problem = path.IsDeleted ? ProblemNotFound : null
			};
		}

		public Task<ContentSetSourceItem> DescribeVersionAsync(int publisherDepartmentId, int itemId, int versionId) => DescribeAsync(publisherDepartmentId, itemId);

		public async Task<List<KeyValuePair<ContentSetItemKinds, int>>> GetDependenciesAsync(int publisherDepartmentId, int itemId)
		{
			var path = await OwnPathAsync(publisherDepartmentId, itemId);
			if (path == null)
				return new List<KeyValuePair<ContentSetItemKinds, int>>();
			return (await StepsAsync(path.LmsPathId)).Where(IsLocalStep)
				.Select(s => new KeyValuePair<ContentSetItemKinds, int>(StepItemKind(s), s.TargetRefId)).Distinct().ToList();
		}

		/// <summary>The subscriber's steps: department targets remapped onto its managed copies from this publisher; null when one has no copy yet.</summary>
		private async Task<List<LmsPathStep>> MapStepsAsync(ContentSetCopyContext context, int localPathId)
		{
			var copies = new Dictionary<(ContentSetItemKinds, int), int>();
			foreach (var kind in new[] { ContentSetItemKinds.LmsCourse, ContentSetItemKinds.Training })
				foreach (var copy in ((await _copies.GetManagedForDepartmentAsync(context.SubscriberDepartmentId, kind)) ?? new List<ContentSetCopy>())
					.Where(c => c.PublisherDepartmentId == context.Set.DepartmentId && c.IsManaged).OrderBy(c => c.ContentSetCopyId))
					if (!copies.ContainsKey((kind, copy.SourceItemId)))
						copies[(kind, copy.SourceItemId)] = copy.LocalItemId;

			var mapped = new List<LmsPathStep>();
			foreach (var step in await StepsAsync(context.Source.ItemId))
			{
				var target = step.TargetRefId;
				if (IsLocalStep(step) && !copies.TryGetValue((StepItemKind(step), step.TargetRefId), out target))
					return null;
				mapped.Add(new LmsPathStep
				{
					LmsPathId = localPathId,
					DepartmentId = context.SubscriberDepartmentId,
					SortOrder = step.SortOrder,
					Kind = step.Kind,
					TargetRefId = target,
					TargetSystemKey = step.TargetSystemKey,
					PrerequisiteOrders = step.PrerequisiteOrders,
					ElectiveGroup = step.ElectiveGroup,
					ElectiveRequired = step.ElectiveRequired
				});
			}

			return LmsPathRules.Validate(mapped) == null ? mapped : null;
		}

		public async Task<ContentSetCopyOutcome> CreateCopyAsync(ContentSetCopyContext context, CancellationToken cancellationToken = default)
		{
			var source = await OwnPathAsync(context.Set.DepartmentId, context.Source.ItemId);
			if (source == null || source.IsDeleted)
				return ContentSetCopyOutcome.Fail(ProblemNotFound);
			if (await MapStepsAsync(context, 0) == null)
				return ContentSetCopyOutcome.Fail(ProblemStepMissing);

			var now = DateTime.UtcNow;
			var path = await _paths.InsertAsync(new LmsPath
			{
				DepartmentId = context.SubscriberDepartmentId,
				Name = source.Name,
				Description = source.Description,
				CreatedOn = now,
				CreatedByUserId = context.UserId
			}, cancellationToken, true);
			await _steps.ReplaceAsync(context.SubscriberDepartmentId, path.LmsPathId, await MapStepsAsync(context, path.LmsPathId), cancellationToken);
			await ContentSetAttribution.KeepAsync(_attributions, context, ContentAttributionSubjects.LmsPath, path.LmsPathId, cancellationToken);
			return new ContentSetCopyOutcome { Succeeded = true, LocalItemId = path.LmsPathId };
		}

		public async Task<ContentSetCopyOutcome> ApplyVersionAsync(ContentSetCopyContext context, CancellationToken cancellationToken = default)
		{
			var source = await OwnPathAsync(context.Set.DepartmentId, context.Source.ItemId);
			var local = await OwnPathAsync(context.SubscriberDepartmentId, context.Copy.LocalItemId);
			if (source == null || source.IsDeleted || local == null || local.IsDeleted)
				return ContentSetCopyOutcome.Fail(ProblemNotFound);
			var steps = await MapStepsAsync(context, local.LmsPathId);
			if (steps == null)
				return ContentSetCopyOutcome.Fail(ProblemStepMissing);

			local.Name = source.Name;
			local.Description = source.Description;
			local.UpdatedOn = DateTime.UtcNow;
			local.UpdatedByUserId = context.UserId;
			await _paths.SaveOrUpdateAsync(local, cancellationToken, true);
			await _steps.ReplaceAsync(context.SubscriberDepartmentId, local.LmsPathId, steps, cancellationToken);
			await ContentSetAttribution.KeepAsync(_attributions, context, ContentAttributionSubjects.LmsPath, local.LmsPathId, cancellationToken);
			return new ContentSetCopyOutcome { Succeeded = true, LocalItemId = local.LmsPathId };
		}

		/// <summary>A path as HTML (name, description, then each step's title in order), the text the inbox redline compares.</summary>
		private async Task<string> OutlineAsync(int departmentId, LmsPath path)
		{
			if (path == null)
				return string.Empty;
			var steps = await StepsAsync(path.LmsPathId);
			var courseIds = steps.Where(s => s.Kind == (int)LmsPathStepKinds.Course).Select(s => s.TargetRefId).Distinct().ToList();
			var courses = courseIds.Count == 0 ? new List<LmsCourse>() : (await _courses.GetVisibleByIdsAsync(departmentId, courseIds)) ?? new List<LmsCourse>();
			var html = new StringBuilder("<h2>").Append(WebUtility.HtmlEncode(path.Name ?? string.Empty)).Append("</h2>");
			if (!string.IsNullOrWhiteSpace(path.Description))
				html.Append("<p>").Append(WebUtility.HtmlEncode(path.Description)).Append("</p>");
			html.Append("<ol>");
			foreach (var step in steps)
			{
				string title;
				if (step.Kind == (int)LmsPathStepKinds.Course)
					title = courses.FirstOrDefault(c => c.LmsCourseId == step.TargetRefId)?.Title;
				else
				{
					var training = await _trainings.GetTrainingRowByIdAsync(step.TargetRefId);
					title = training != null && training.DepartmentId == departmentId ? training.Name : null;
				}
				html.Append("<li>").Append(WebUtility.HtmlEncode(title ?? "#" + step.TargetRefId.ToString(CultureInfo.InvariantCulture))).Append("</li>");
			}

			return html.Append("</ol>").ToString();
		}

		public async Task<string> GetLocalHtmlAsync(int subscriberDepartmentId, int localItemId) =>
			await OutlineAsync(subscriberDepartmentId, await OwnPathAsync(subscriberDepartmentId, localItemId));

		public async Task<(string Html, List<string> Dropped)> PreviewAsync(ContentSetCopyContext context) =>
			context?.Source == null ? (string.Empty, new List<string>())
				: (await OutlineAsync(context.Set.DepartmentId, await OwnPathAsync(context.Set.DepartmentId, context.Source.ItemId)), new List<string>());

		public async Task<bool> LocalExistsAsync(int subscriberDepartmentId, int localItemId)
		{
			var path = await OwnPathAsync(subscriberDepartmentId, localItemId);
			return path != null && !path.IsDeleted;
		}
	}
}
