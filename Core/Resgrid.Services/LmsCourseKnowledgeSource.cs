using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Lms;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services.Knowledge;

namespace Resgrid.Services
{
	/// <summary>
	/// LMS courses in the K1 knowledge corpus (K3, protocols-training-knowledge-plan §16.1 "Courses"), in the LMS layer so baseline code never
	/// sees a course type (§17.3). What is indexed: the department's published course's version in force — its blocks lessons per block
	/// (P3 block anchors b-{key}, or the heading anchors of html lessons), video transcripts, document lesson titles and the protocol
	/// versions embedded as lessons (unless that protocol is "no AI" or licensed). Each chunk's anchor names its lesson
	/// (<see cref="KnowledgeLinks.CourseAnchor"/>). Never read: assessments and their question banks (answer keys, explanations), attempts,
	/// enrollments' progress, uploaded learner files, survey answers or any other learner data; Academy system courses are the Academy
	/// corpus's. Indexing needs the LMS flag (no billing call, so worker 89 may build it); reading needs the add-on and the reader's own rule:
	/// enrolled in the course, or the course is in their catalog (self-enrollable and its audience, or a course manager). Publish, retire and
	/// audience changes queue a reindex (<see cref="IKnowledgeIndexQueue"/>). Also the "Ask this course" version source: the version the
	/// learner takes, built for them at request time.
	/// </summary>
	public sealed class LmsCourseKnowledgeSource : IKnowledgeCorpusSource, IKnowledgeVersionContentSource
	{
		private static readonly KnowledgeSourceKinds[] SourceKinds = { KnowledgeSourceKinds.Course };

		private readonly ILmsAccessService _access;
		private readonly ILmsGateService _gate;
		private readonly ILmsCoursesRepository _courses;
		private readonly ILmsCourseVersionsRepository _versions;
		private readonly ILmsCourseModulesRepository _modules;
		private readonly ILmsLessonsRepository _lessons;
		private readonly ILmsCourseAudiencesRepository _audiences;
		private readonly ILmsEnrollmentsRepository _enrollments;
		private readonly IProtocolVersionsRepository _protocolVersions;
		private readonly IKnowledgeIndexRepository _index;

		public LmsCourseKnowledgeSource(ILmsAccessService access, ILmsGateService gate, ILmsCoursesRepository courses, ILmsCourseVersionsRepository versions,
			ILmsCourseModulesRepository modules, ILmsLessonsRepository lessons, ILmsCourseAudiencesRepository audiences, ILmsEnrollmentsRepository enrollments,
			IProtocolVersionsRepository protocolVersions, IKnowledgeIndexRepository index)
		{
			_access = access;
			_gate = gate;
			_courses = courses;
			_versions = versions;
			_modules = modules;
			_lessons = lessons;
			_audiences = audiences;
			_enrollments = enrollments;
			_protocolVersions = protocolVersions;
			_index = index;
		}

		public IReadOnlyCollection<KnowledgeSourceKinds> Kinds => SourceKinds;

		public IReadOnlyCollection<KnowledgeSourceKinds> VersionKinds => SourceKinds;

		/// <summary>A department course that may be indexed at all: published with a version in force, not deleted, not a system course.</summary>
		public static bool IsIndexable(LmsCourse course, int departmentId) =>
			course != null && departmentId > 0 && course.DepartmentId == departmentId && !course.IsSystem && !course.IsDeleted &&
			course.Status == (int)ProtocolStatuses.Published && course.CurrentVersionId.HasValue;

		/// <summary>The lesson kinds whose text joins the corpus; assessments, surveys and packages never do.</summary>
		public static bool IsIndexedLesson(LmsLesson lesson) =>
			lesson != null && (lesson.Kind == (int)LmsLessonKinds.Blocks || lesson.Kind == (int)LmsLessonKinds.Video || lesson.Kind == (int)LmsLessonKinds.Document ||
				lesson.Kind == (int)LmsLessonKinds.ProtocolVersion);

		private async Task<bool> FlagOnAsync(int departmentId)
		{
			try
			{
				return departmentId > 0 && await _access.IsFlagEnabledAsync(departmentId);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"The LMS flag could not be read for department {departmentId}.");
				return false;
			}
		}

		public async Task<IReadOnlyList<string>> ListItemIdsAsync(int departmentId, KnowledgeSourceKinds kind, CancellationToken cancellationToken = default)
		{
			if (kind != KnowledgeSourceKinds.Course || !await FlagOnAsync(departmentId))
				return Array.Empty<string>();
			return (await _courses.GetForDepartmentAsync(departmentId) ?? new List<LmsCourse>())
				.Where(c => IsIndexable(c, departmentId)).Select(c => c.LmsCourseId.ToString(CultureInfo.InvariantCulture)).ToList();
		}

		public async Task<KnowledgeItemContent> BuildAsync(int departmentId, KnowledgeSourceKinds kind, string itemId, CancellationToken cancellationToken = default)
		{
			if (kind != KnowledgeSourceKinds.Course || !int.TryParse(itemId, NumberStyles.None, CultureInfo.InvariantCulture, out var courseId) || !await FlagOnAsync(departmentId))
				return null;

			var course = await _courses.GetVisibleByIdAsync(departmentId, courseId);
			if (!IsIndexable(course, departmentId) || course.LmsCourseId != courseId)
				return null;
			var version = await _versions.GetVisibleByIdAsync(departmentId, course.CurrentVersionId.Value);
			if (version == null || version.DepartmentId != departmentId || version.LmsCourseId != courseId || version.Status != (int)ProtocolVersionStatuses.Published)
				return null;

			var audience = (await _audiences.GetForCourseAsync(departmentId, courseId) ?? new List<LmsCourseAudience>())
				.Where(a => a.DepartmentId == departmentId && a.LmsCourseId == courseId).Cast<IContentAudienceRule>().ToList();
			return new KnowledgeItemContent
			{
				DepartmentId = departmentId,
				Kind = KnowledgeSourceKinds.Course,
				ItemId = itemId,
				Chunks = await BuildVersionChunksAsync(departmentId, course, version, audience, cancellationToken)
			};
		}

		/// <summary>
		/// The chunks of one course version: the lessons in outline order, each cut per block or heading section, citing its lesson. Only
		/// lesson rows are read (never a question bank, an attempt or a learner's file); a protocol lesson reads its published version.
		/// </summary>
		public async Task<List<KnowledgeChunk>> BuildVersionChunksAsync(int departmentId, LmsCourse course, LmsCourseVersion version, IEnumerable<IContentAudienceRule> audience,
			CancellationToken cancellationToken)
		{
			var modules = (await _modules.GetForVersionAsync(departmentId, version.LmsCourseVersionId) ?? new List<LmsCourseModule>())
				.Where(m => m.DepartmentId == departmentId && m.LmsCourseVersionId == version.LmsCourseVersionId).ToList();
			var lessons = (await _lessons.GetForVersionAsync(departmentId, version.LmsCourseVersionId) ?? new List<LmsLesson>())
				.Where(l => l.DepartmentId == departmentId && l.LmsCourseVersionId == version.LmsCourseVersionId && IsIndexedLesson(l)).ToList();
			var outline = LmsCourseService.BuildOutline(course, version, modules, lessons);

			var protocolIds = lessons.Where(l => l.Kind == (int)LmsLessonKinds.ProtocolVersion && l.ProtocolVersionId.HasValue).Select(l => l.ProtocolVersionId.Value).Distinct().ToList();
			var protocolVersions = protocolIds.Count == 0 ? new List<ProtocolVersion>()
				: await _protocolVersions.GetByIdsAsync(departmentId, protocolIds, true) ?? new List<ProtocolVersion>();
			var protocols = protocolIds.Count == 0 ? new List<DispatchProtocol>()
				: await _index.GetProtocolHeadersAsync(departmentId, cancellationToken) ?? new List<DispatchProtocol>();

			var audienceJson = KnowledgeAccessFilter.SerializeAudience(audience ?? Enumerable.Empty<IContentAudienceRule>());
			var title = KnowledgeChunkFactory.Truncate(string.IsNullOrWhiteSpace(course.Code) ? (version.Title ?? course.Title) : course.Code + " · " + (version.Title ?? course.Title),
				KnowledgeChunk.TitleMaxLength) ?? string.Empty;
			var chunks = new List<KnowledgeChunk>();
			foreach (var module in outline.Modules)
			{
				foreach (var lesson in module.Lessons)
				{
					var lessonPath = Join(module.Module.Title, lesson.Title);
					foreach (var section in LessonSections(departmentId, lesson, protocolVersions, protocols))
					{
						if (string.IsNullOrWhiteSpace(section.Text))
							continue;
						chunks.Add(new KnowledgeChunk
						{
							DepartmentId = departmentId,
							SourceKind = (int)KnowledgeSourceKinds.Course,
							ItemId = course.LmsCourseId.ToString(CultureInfo.InvariantCulture),
							VersionId = version.LmsCourseVersionId.ToString(CultureInfo.InvariantCulture),
							VersionLabel = KnowledgeChunkFactory.Truncate(version.Label, 32),
							Title = title,
							Anchor = KnowledgeLinks.CourseAnchor(lesson.LmsLessonId, section.Anchor),
							HeadingPath = KnowledgeChunkFactory.Truncate(Join(lessonPath, section.HeadingPath), KnowledgeChunk.HeadingPathMaxLength),
							Text = section.Text,
							Locale = KnowledgeChunkFactory.Truncate(version.Locale, 16),
							EffectiveOn = version.EffectiveOn,
							AudienceJson = audienceJson,
							// The live rule decides: enrolled, or in the reader's catalog (see AddReadableAsync).
							AccessFlags = (int)KnowledgeAccessFlags.AssigneesRead
						});
					}
				}
			}
			return chunks;
		}

		private static string Join(string first, string second)
		{
			var a = (first ?? string.Empty).Trim();
			var b = (second ?? string.Empty).Trim();
			return a.Length == 0 ? b : b.Length == 0 ? a : a + KnowledgeChunker.PathSeparator + b;
		}

		/// <summary>One lesson's sections by kind; every other kind (assessment, survey, packages, live sessions) yields nothing.</summary>
		private static IEnumerable<KnowledgeChunker.Section> LessonSections(int departmentId, LmsLesson lesson, List<ProtocolVersion> protocolVersions, List<DispatchProtocol> protocols)
		{
			var max = KnowledgeConfig.MaxChunkChars;
			switch ((LmsLessonKinds)lesson.Kind)
			{
				case LmsLessonKinds.Blocks:
					var document = ProtocolContentDocument.Parse(lesson.ContentJson, lesson.ContentHtml);
					if (document.IsBlockModel)
						return KnowledgeChunker.SplitBlocks(document, max);
					var html = document.Render();
					return KnowledgeChunker.SplitHtml(string.IsNullOrWhiteSpace(html) ? lesson.ContentHtml : html, max);
				case LmsLessonKinds.Video:
					return KnowledgeChunker.Cut(lesson.TranscriptText ?? string.Empty, max).Select(t => new KnowledgeChunker.Section { Text = t }).ToList();
				case LmsLessonKinds.Document:
					// The document itself is an https link outside Resgrid: its title and name are what the course holds.
					var name = (lesson.DocumentName ?? string.Empty).Trim();
					return name.Length == 0 ? Enumerable.Empty<KnowledgeChunker.Section>() : new[] { new KnowledgeChunker.Section { Text = (lesson.Title ?? string.Empty).Trim() + ": " + name } };
				case LmsLessonKinds.ProtocolVersion:
					var version = protocolVersions.FirstOrDefault(v => lesson.ProtocolVersionId.HasValue && v.ProtocolVersionId == lesson.ProtocolVersionId.Value && v.DepartmentId == departmentId);
					var protocol = version == null ? null : protocols.FirstOrDefault(p => p.DispatchProtocolId == version.DispatchProtocolId && p.DepartmentId == departmentId);
					// Decision 14: a protocol marked "no AI" or licensed never joins the corpus, not even inside a course.
					if (version == null || protocol == null || protocol.IsDeleted || protocol.ExcludeFromAi || protocol.IsLicensedContent ||
						(version.Status != (int)ProtocolVersionStatuses.Published && version.Status != (int)ProtocolVersionStatuses.Superseded))
						return Enumerable.Empty<KnowledgeChunker.Section>();
					return ProtocolKnowledgeSource.BuildVersionChunks(departmentId, protocol, version, null)
						.Select(c => new KnowledgeChunker.Section { Anchor = c.Anchor, HeadingPath = c.HeadingPath, Text = c.Text }).ToList();
				default:
					return Enumerable.Empty<KnowledgeChunker.Section>();
			}
		}

		#region Readers

		/// <summary>The LMS gate for AI course features (§17.2 row "AI"): the flag and an active add-on. Unknown billing is "no".</summary>
		public static bool GateOpen(LmsActor actor) =>
			actor != null && actor.IsCurrentMember && actor.Gate != null && actor.Gate.FlagOn && actor.Gate.AddonActive == true;

		public async Task AddReadableAsync(KnowledgeReader reader, KnowledgeReaderScope scope, CancellationToken cancellationToken = default)
		{
			if (reader == null || scope == null || !reader.CanViewTrainings || scope.DepartmentId <= 0)
				return;
			try
			{
				var actor = await _gate.GetActorAsync(scope.DepartmentId, scope.UserId);
				if (!GateOpen(actor))
					return;
				scope.Allow(KnowledgeSourceKinds.Course, (await ReadableCourseIdsAsync(actor)).Select(id => id.ToString(CultureInfo.InvariantCulture)));
			}
			catch (Exception ex)
			{
				// Unknown access is no access.
				Logging.LogException(ex, "Course knowledge access could not be read.");
			}
		}

		/// <summary>The reader's rule: department courses they are enrolled in (any enrollment not withdrawn) or that their catalog lists.</summary>
		public async Task<HashSet<int>> ReadableCourseIdsAsync(LmsActor actor)
		{
			var readable = new HashSet<int>();
			if (!GateOpen(actor))
				return readable;

			var courses = (await _courses.GetForDepartmentAsync(actor.DepartmentId) ?? new List<LmsCourse>()).Where(c => IsIndexable(c, actor.DepartmentId)).ToList();
			if (courses.Count == 0)
				return readable;
			var enrolled = new HashSet<int>((await _enrollments.GetForUserAsync(actor.DepartmentId, actor.UserId) ?? new List<LmsEnrollment>())
				.Where(e => e.DepartmentId == actor.DepartmentId && string.Equals(e.UserId, actor.UserId, StringComparison.OrdinalIgnoreCase) && e.Status != (int)LmsEnrollmentStatuses.Withdrawn)
				.Select(e => e.LmsCourseId));
			var audiences = (await _audiences.GetForDepartmentAsync(actor.DepartmentId) ?? new List<LmsCourseAudience>()).ToLookup(a => a.LmsCourseId);
			foreach (var course in courses)
			{
				if (enrolled.Contains(course.LmsCourseId) || InCatalog(actor, course, audiences[course.LmsCourseId]))
					readable.Add(course.LmsCourseId);
			}
			return readable;
		}

		/// <summary>The catalog's own rule (ILmsLearnerService.GetCatalogAsync): self-enrollable and in its audience (managers see past it).</summary>
		public static bool InCatalog(LmsActor actor, LmsCourse course, IEnumerable<LmsCourseAudience> audience)
		{
			if (course == null || !course.IsSelfEnrollable)
				return false;
			var rules = (audience ?? Enumerable.Empty<LmsCourseAudience>()).ToList();
			if (rules.Count == 0 || actor.MayManage)
				return true;
			var facts = actor.Facts;
			if (facts == null || !actor.IsCurrentMember)
				return false;
			// Reading is a gate, not automation: a current member who is hidden still sees what is theirs (KnowledgeAccessFilter's rule).
			return ContentAudienceMatcher.Matches(new ContentAudienceMemberFacts
			{
				UserId = facts.UserId ?? actor.UserId,
				IsActive = true,
				GroupIds = facts.GroupIds ?? new HashSet<int>(),
				RoleIds = facts.RoleIds ?? new HashSet<int>(),
				CertificationTypeIds = facts.CertificationTypeIds ?? new HashSet<int>(),
				UnitTypeIds = facts.UnitTypeIds ?? new HashSet<int>(),
				ShiftIds = facts.ShiftIds ?? new HashSet<int>()
			}, rules);
		}

		/// <summary>
		/// "Ask this course" (K3 tutor): the version the reader takes — the asked version when it is one of their enrollments (or the version in
		/// force of a course in their catalog), else their open (then latest) enrollment's, else the version in force when the catalog lists
		/// it. Refused NotEntitled while the LMS gate is closed; Invalid for anything the reader cannot open.
		/// </summary>
		public async Task<KnowledgeVersionContent> LoadForReaderAsync(KnowledgeReader reader, KnowledgeSourceKinds kind, string itemId, string versionId,
			CancellationToken cancellationToken = default)
		{
			if (reader == null || kind != KnowledgeSourceKinds.Course || reader.DepartmentId <= 0 || string.IsNullOrWhiteSpace(reader.UserId) ||
				!int.TryParse(itemId, NumberStyles.None, CultureInfo.InvariantCulture, out var courseId))
				return KnowledgeVersionContent.Refused(KnowledgeOutcomes.Invalid);
			if (!reader.CanViewTrainings)
				return KnowledgeVersionContent.Refused(KnowledgeOutcomes.NotEntitled);

			var actor = await _gate.GetActorAsync(reader.DepartmentId, reader.UserId);
			if (!GateOpen(actor))
				return KnowledgeVersionContent.Refused(KnowledgeOutcomes.NotEntitled);

			var course = await _courses.GetVisibleByIdAsync(reader.DepartmentId, courseId);
			if (!IsIndexable(course, reader.DepartmentId) || course.LmsCourseId != courseId)
				return KnowledgeVersionContent.Refused(KnowledgeOutcomes.Invalid);

			var mine = (await _enrollments.GetForUserAsync(reader.DepartmentId, reader.UserId) ?? new List<LmsEnrollment>())
				.Where(e => e.LmsCourseId == courseId && e.DepartmentId == reader.DepartmentId && string.Equals(e.UserId, reader.UserId, StringComparison.OrdinalIgnoreCase) &&
					e.Status != (int)LmsEnrollmentStatuses.Withdrawn).ToList();
			var catalog = InCatalog(actor, course, await _audiences.GetForCourseAsync(reader.DepartmentId, courseId));
			var allowed = new HashSet<int>(mine.Select(e => e.LmsCourseVersionId));
			if (catalog)
				allowed.Add(course.CurrentVersionId.Value);
			if (allowed.Count == 0)
				return KnowledgeVersionContent.Refused(KnowledgeOutcomes.Invalid);

			int chosen;
			if (!string.IsNullOrWhiteSpace(versionId))
			{
				if (!int.TryParse(versionId, NumberStyles.None, CultureInfo.InvariantCulture, out chosen) || !allowed.Contains(chosen))
					return KnowledgeVersionContent.Refused(KnowledgeOutcomes.Invalid);
			}
			else
			{
				var pick = mine.OrderByDescending(e => e.IsOpen).ThenByDescending(e => e.LastActivityOn ?? e.CompletedOn ?? e.AssignedOn).FirstOrDefault();
				chosen = pick?.LmsCourseVersionId ?? course.CurrentVersionId.Value;
			}

			var version = await _versions.GetVisibleByIdAsync(reader.DepartmentId, chosen);
			if (version == null || version.DepartmentId != reader.DepartmentId || version.LmsCourseId != courseId ||
				(version.Status != (int)ProtocolVersionStatuses.Published && version.Status != (int)ProtocolVersionStatuses.Superseded))
				return KnowledgeVersionContent.Refused(KnowledgeOutcomes.Invalid);

			var chunks = await BuildVersionChunksAsync(reader.DepartmentId, course, version, null, cancellationToken);
			// Read for this reader now: no stored audience; the stored vectors are reused where the text is indexed.
			var indexed = (await _index.GetItemChunksAsync(reader.DepartmentId, (int)KnowledgeSourceKinds.Course, itemId, cancellationToken) ?? new List<KnowledgeChunk>())
				.Where(c => c.Embedding != null && !string.IsNullOrEmpty(c.TextHash)).GroupBy(c => c.TextHash, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
			var ordinal = 0;
			foreach (var chunk in chunks)
			{
				chunk.Ordinal = ordinal++;
				chunk.AccessFlags = (int)KnowledgeAccessFlags.None;
				chunk.AudienceJson = null;
				chunk.TextHash = KnowledgeChunker.Hash(KnowledgeChunker.EmbeddingInput(chunk));
				chunk.KnowledgeChunkId = -chunk.Ordinal - 1;
				if (indexed.TryGetValue(chunk.TextHash, out var stored))
				{
					chunk.Embedding = stored.Embedding;
					chunk.EmbeddingModel = stored.EmbeddingModel;
				}
			}

			return new KnowledgeVersionContent
			{
				Kind = KnowledgeSourceKinds.Course,
				ItemId = course.LmsCourseId.ToString(CultureInfo.InvariantCulture),
				VersionId = version.LmsCourseVersionId.ToString(CultureInfo.InvariantCulture),
				Title = version.Title ?? course.Title,
				Chunks = chunks
			};
		}

		#endregion Readers
	}
}
