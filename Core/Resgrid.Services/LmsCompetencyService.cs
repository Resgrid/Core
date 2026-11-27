using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Lms;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>A CASE 1.1 package read into memory (CFDocument, CFItems, isChildOf associations); nothing is written yet.</summary>
	public class LmsCasePackage
	{
		public string DocumentId { get; set; }
		public string Uri { get; set; }
		public string Title { get; set; }
		public string Publisher { get; set; }
		public string Version { get; set; }
		public string Description { get; set; }
		public List<LmsCasePackageItem> Items { get; set; } = new List<LmsCasePackageItem>();
	}

	public class LmsCasePackageItem
	{
		public string Identifier { get; set; }
		public string Code { get; set; }
		public string Label { get; set; }
		public string ItemType { get; set; }
		/// <summary>The parent item's identifier; null when the item hangs off the document.</summary>
		public string ParentIdentifier { get; set; }
		public int SortOrder { get; set; }
	}

	/// <summary>Reads CASE 1.1 (1EdTech Competencies and Academic Standards Exchange) JSON: a CFPackage, bare or wrapped.</summary>
	public static class LmsCaseParser
	{
		public const int MaxBytes = 10 * 1024 * 1024;
		public const int MaxItems = 5000;

		/// <summary>The package, or null with <paramref name="error"/> set to a resource key.</summary>
		public static LmsCasePackage Parse(byte[] json, out string error)
		{
			error = null;
			if (json == null || json.Length == 0)
			{
				error = "lms_program_case_empty";
				return null;
			}
			if (json.Length > MaxBytes)
			{
				error = "lms_program_case_too_large";
				return null;
			}

			JObject root;
			try
			{
				var text = Encoding.UTF8.GetString(json).TrimStart('﻿');
				using var reader = new JsonTextReader(new System.IO.StringReader(text)) { DateParseHandling = DateParseHandling.None, MaxDepth = 64 };
				root = JObject.Load(reader);
			}
			catch (Exception)
			{
				error = "lms_program_case_unreadable";
				return null;
			}

			if (root["CFPackage"] is JObject wrapped)
				root = wrapped;
			if (!(root["CFDocument"] is JObject document))
			{
				error = "lms_program_case_no_document";
				return null;
			}

			var package = new LmsCasePackage
			{
				DocumentId = Text(document["identifier"], LmsCompetencyItem.IdentifierMaxLength),
				Uri = Text(document["uri"], 1000),
				Title = Text(document["title"], LmsCompetencyFramework.NameMaxLength),
				Publisher = Text(document["publisher"], 250) ?? Text(document["creator"], 250),
				Version = Text(document["version"], 100),
				Description = Text(document["description"], LmsCompetencyFramework.DescriptionMaxLength)
			};
			if (string.IsNullOrWhiteSpace(package.DocumentId) || string.IsNullOrWhiteSpace(package.Title))
			{
				error = "lms_program_case_no_document";
				return null;
			}

			var items = root["CFItems"] as JArray ?? new JArray();
			if (items.Count > MaxItems)
			{
				error = "lms_program_case_too_many_items";
				return null;
			}

			var index = 0;
			var byId = new Dictionary<string, LmsCasePackageItem>(StringComparer.OrdinalIgnoreCase);
			foreach (var token in items.OfType<JObject>())
			{
				var identifier = Text(token["identifier"], LmsCompetencyItem.IdentifierMaxLength);
				if (string.IsNullOrWhiteSpace(identifier) || byId.ContainsKey(identifier))
					continue;

				var label = Text(token["abbreviatedStatement"], LmsCompetencyItem.LabelMaxLength) ?? Text(token["fullStatement"], LmsCompetencyItem.LabelMaxLength);
				var type = Text(token["CFItemType"], LmsCompetencyItem.TypeMaxLength) ?? Text(token["CFItemTypeURI"]?["title"], LmsCompetencyItem.TypeMaxLength);
				var item = new LmsCasePackageItem
				{
					Identifier = identifier,
					Code = Text(token["humanCodingScheme"], LmsCompetencyItem.CodeMaxLength),
					Label = label ?? Text(token["humanCodingScheme"], LmsCompetencyItem.LabelMaxLength) ?? identifier,
					ItemType = type,
					SortOrder = int.TryParse(Text(token["listEnumeration"], 20), NumberStyles.Integer, CultureInfo.InvariantCulture, out var order) ? order : ++index
				};
				byId[identifier] = item;
				package.Items.Add(item);
			}

			foreach (var association in (root["CFAssociations"] as JArray ?? new JArray()).OfType<JObject>())
			{
				if (!string.Equals(Text(association["associationType"], 50), "isChildOf", StringComparison.OrdinalIgnoreCase))
					continue;
				var child = Text(association["originNodeURI"]?["identifier"], LmsCompetencyItem.IdentifierMaxLength);
				var parent = Text(association["destinationNodeURI"]?["identifier"], LmsCompetencyItem.IdentifierMaxLength);
				if (child == null || !byId.TryGetValue(child, out var item))
					continue;
				if (parent != null && byId.ContainsKey(parent) && !string.Equals(parent, child, StringComparison.OrdinalIgnoreCase))
					item.ParentIdentifier = parent;
				if (association["sequenceNumber"] != null && int.TryParse(association["sequenceNumber"].ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var sequence))
					item.SortOrder = sequence;
			}

			return package;
		}

		private static string Text(JToken token, int max)
		{
			if (token == null || token.Type == JTokenType.Null || token.Type == JTokenType.Object || token.Type == JTokenType.Array)
				return null;
			var text = token.ToString().Trim();
			if (text.Length == 0)
				return null;
			return text.Length > max ? text.Substring(0, max) : text;
		}
	}

	/// <summary>
	/// Competency frameworks (§13 T3 item 3b, R-C7; LMS add-on): the catalog (structure and identifiers only), CASE 1.1 import, custom
	/// items, tags on courses, trainings, categories and certification types (skill sheets and task-book tasks once T4 lands), and the
	/// per-member gap report. Consumes baseline services and the T2 course tables (it is LMS code); its callers check the LMS gate.
	/// </summary>
	public class LmsCompetencyService : ILmsCompetencyService
	{
		private readonly ILmsCompetencyFrameworksRepository _frameworks;
		private readonly ILmsCompetencyItemsRepository _items;
		private readonly ILmsCompetencyAlignmentsRepository _alignments;
		private readonly ILearningRecordService _learningRecords;
		private readonly ICertificationService _certifications;
		private readonly ILmsCoursesRepository _courses;
		private readonly ILmsEnrollmentsRepository _enrollments;
		private readonly IDepartmentsService _departments;
		private readonly ITrainingService _trainings;
		private readonly IEventAggregator _events;
		private readonly ILmsSkillCompetencySubjects _skills;

		public LmsCompetencyService(ILmsCompetencyFrameworksRepository frameworks, ILmsCompetencyItemsRepository items, ILmsCompetencyAlignmentsRepository alignments,
			ILearningRecordService learningRecords, ICertificationService certifications, ILmsCoursesRepository courses, ILmsEnrollmentsRepository enrollments,
			IDepartmentsService departments, ITrainingService trainings, IEventAggregator events, ILmsSkillCompetencySubjects skills)
		{
			_skills = skills;
			_trainings = trainings;
			_frameworks = frameworks;
			_items = items;
			_alignments = alignments;
			_learningRecords = learningRecords;
			_certifications = certifications;
			_courses = courses;
			_enrollments = enrollments;
			_departments = departments;
			_events = events;
		}

		public IReadOnlyList<LmsCompetencyCatalogFramework> GetCatalog() => LmsCompetencyFrameworkCatalog.All;

		public IReadOnlyList<LmsCompetencyCatalogFramework> GetCatalog(string culture) => LmsCompetencyFrameworkCatalog.AllFor(culture);

		public async Task<List<LmsCompetencyFramework>> GetFrameworksAsync(int departmentId) =>
			departmentId <= 0 ? new List<LmsCompetencyFramework>() : await _frameworks.GetForDepartmentAsync(departmentId) ?? new List<LmsCompetencyFramework>();

		public async Task<LmsCompetencyFramework> GetFrameworkAsync(int departmentId, int lmsCompetencyFrameworkId)
		{
			var framework = await _frameworks.GetByIdForDepartmentAsync(departmentId, lmsCompetencyFrameworkId);
			return framework == null || framework.IsDeleted || framework.DepartmentId != departmentId ? null : framework;
		}

		private async Task<LmsCompetencyFramework> RequireFrameworkAsync(int departmentId, int lmsCompetencyFrameworkId) =>
			await GetFrameworkAsync(departmentId, lmsCompetencyFrameworkId) ?? throw new InvalidOperationException("lms_program_framework_not_found");

		public async Task<List<LmsCompetencyItem>> GetItemsAsync(int departmentId, int lmsCompetencyFrameworkId) =>
			(await _items.GetForFrameworkAsync(departmentId, lmsCompetencyFrameworkId) ?? new List<LmsCompetencyItem>()).Where(i => i.DepartmentId == departmentId).ToList();

		#region Catalog and CASE import

		public Task<LmsCompetencyFramework> ApplyCatalogAsync(int departmentId, string catalogKey, string userId, CancellationToken cancellationToken = default) =>
			ApplyCatalogAsync(departmentId, catalogKey, userId, null, cancellationToken);

		public async Task<LmsCompetencyFramework> ApplyCatalogAsync(int departmentId, string catalogKey, string userId, string culture, CancellationToken cancellationToken = default)
		{
			// Round 9C: display names in the author's language (English when none ships); keys and codes are unchanged.
			var source = LmsCompetencyFrameworkCatalog.Find(catalogKey, culture) ?? throw new InvalidOperationException("lms_program_framework_not_found");
			var existing = (await GetFrameworksAsync(departmentId)).FirstOrDefault(f => string.Equals(f.CatalogKey, source.Key, StringComparison.OrdinalIgnoreCase));
			if (existing != null)
				return existing;

			var now = DateTime.UtcNow;
			var framework = await _frameworks.SaveOrUpdateAsync(new LmsCompetencyFramework
			{
				DepartmentId = departmentId,
				CatalogKey = source.Key,
				Name = source.Name,
				Publisher = source.Publisher,
				Edition = source.Edition,
				Description = source.Description,
				Source = (int)LmsCompetencyFrameworkSources.Catalog,
				CreatedOn = now,
				CreatedByUserId = userId
			}, cancellationToken, true);

			var ids = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
			var order = 0;
			foreach (var seed in source.Items)
			{
				var item = await _items.SaveOrUpdateAsync(new LmsCompetencyItem
				{
					LmsCompetencyFrameworkId = framework.LmsCompetencyFrameworkId,
					DepartmentId = departmentId,
					ParentItemId = seed.ParentKey != null && ids.TryGetValue(seed.ParentKey, out var parent) ? parent : (int?)null,
					Identifier = seed.Key,
					HumanCodingScheme = seed.Code,
					Label = seed.Label,
					ItemType = seed.ItemType,
					SortOrder = ++order,
					CreatedOn = now
				}, cancellationToken, true);
				ids[seed.Key] = item.LmsCompetencyItemId;
			}

			Audit(departmentId, userId, AuditLogTypes.LmsCompetencyFrameworkChanged, null, JsonConvert.SerializeObject(new { framework.LmsCompetencyFrameworkId, framework.CatalogKey, Items = source.Items.Count }));
			return framework;
		}

		public async Task<LmsCaseImportResult> ImportCaseAsync(int departmentId, byte[] json, string userId, CancellationToken cancellationToken = default)
		{
			var result = new LmsCaseImportResult();
			var package = LmsCaseParser.Parse(json, out var error);
			if (package == null)
			{
				result.Error = error;
				return result;
			}

			var now = DateTime.UtcNow;
			var framework = await _frameworks.GetByCaseDocumentIdAsync(departmentId, package.DocumentId);
			result.Updated = framework != null;
			framework ??= new LmsCompetencyFramework { DepartmentId = departmentId, Source = (int)LmsCompetencyFrameworkSources.CaseImport, CreatedOn = now, CreatedByUserId = userId };
			framework.Name = package.Title;
			framework.Publisher = package.Publisher;
			framework.Edition = package.Version;
			framework.Description = package.Description;
			framework.CaseDocumentId = package.DocumentId;
			framework.CaseUri = package.Uri;
			if (result.Updated)
			{
				framework.UpdatedOn = now;
				framework.UpdatedByUserId = userId;
			}
			framework = await _frameworks.SaveOrUpdateAsync(framework, cancellationToken, true);
			result.Framework = framework;

			// Items by CASE identifier: changed ones are updated in place (their tags stay), new ones added, missing ones soft-deleted.
			var existing = (await _items.GetForFrameworkAsync(departmentId, framework.LmsCompetencyFrameworkId, true) ?? new List<LmsCompetencyItem>())
				.Where(i => !string.IsNullOrWhiteSpace(i.Identifier))
				.GroupBy(i => i.Identifier, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
			var ids = new Dictionary<string, LmsCompetencyItem>(StringComparer.OrdinalIgnoreCase);
			foreach (var incoming in package.Items)
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (existing.TryGetValue(incoming.Identifier, out var item))
				{
					var changed = item.IsDeleted || item.Label != incoming.Label || item.HumanCodingScheme != incoming.Code || item.ItemType != incoming.ItemType || item.SortOrder != incoming.SortOrder;
					item.IsDeleted = false;
					item.Label = incoming.Label;
					item.HumanCodingScheme = incoming.Code;
					item.ItemType = incoming.ItemType;
					item.SortOrder = incoming.SortOrder;
					if (changed)
					{
						item = await _items.SaveOrUpdateAsync(item, cancellationToken, true);
						result.ItemsUpdated++;
					}
				}
				else
				{
					item = await _items.SaveOrUpdateAsync(new LmsCompetencyItem
					{
						LmsCompetencyFrameworkId = framework.LmsCompetencyFrameworkId,
						DepartmentId = departmentId,
						Identifier = incoming.Identifier,
						HumanCodingScheme = incoming.Code,
						Label = incoming.Label,
						ItemType = incoming.ItemType,
						SortOrder = incoming.SortOrder,
						CreatedOn = now
					}, cancellationToken, true);
					result.ItemsAdded++;
				}
				ids[incoming.Identifier] = item;
			}

			foreach (var incoming in package.Items)
			{
				var item = ids[incoming.Identifier];
				int? parent = incoming.ParentIdentifier != null && ids.TryGetValue(incoming.ParentIdentifier, out var p) ? p.LmsCompetencyItemId : (int?)null;
				if (item.ParentItemId != parent)
				{
					await _items.SetParentAsync(departmentId, item.LmsCompetencyItemId, parent, cancellationToken);
					item.ParentItemId = parent;
				}
			}

			foreach (var gone in existing.Values.Where(i => !i.IsDeleted && !ids.ContainsKey(i.Identifier)))
			{
				gone.IsDeleted = true;
				await _items.SaveOrUpdateAsync(gone, cancellationToken, true);
				result.ItemsRemoved++;
			}

			Audit(departmentId, userId, AuditLogTypes.LmsCompetencyFrameworkImported, null,
				JsonConvert.SerializeObject(new { framework.LmsCompetencyFrameworkId, framework.CaseDocumentId, result.ItemsAdded, result.ItemsUpdated, result.ItemsRemoved }));
			return result;
		}

		#endregion Catalog and CASE import

		#region Custom frameworks and items

		public async Task<LmsCompetencyFramework> SaveFrameworkAsync(LmsCompetencyFramework framework, string userId, CancellationToken cancellationToken = default)
		{
			if (framework == null || framework.DepartmentId <= 0)
				throw new InvalidOperationException("lms_program_framework_not_found");
			var name = Trim(framework.Name, LmsCompetencyFramework.NameMaxLength);
			if (name == null)
				throw new InvalidOperationException("lms_program_name_required");
			if (framework.EvidenceMaxAgeMonths < 0 || framework.EvidenceMaxAgeMonths > 240)
				throw new InvalidOperationException("lms_program_age_invalid");

			LmsCompetencyFramework stored;
			string before = null;
			if (framework.LmsCompetencyFrameworkId > 0)
			{
				stored = await RequireFrameworkAsync(framework.DepartmentId, framework.LmsCompetencyFrameworkId);
				before = JsonConvert.SerializeObject(stored);
				stored.UpdatedOn = DateTime.UtcNow;
				stored.UpdatedByUserId = userId;
			}
			else
			{
				stored = new LmsCompetencyFramework { DepartmentId = framework.DepartmentId, Source = (int)LmsCompetencyFrameworkSources.Custom, CreatedOn = DateTime.UtcNow, CreatedByUserId = userId };
			}

			stored.Name = name;
			stored.Publisher = Trim(framework.Publisher, 250);
			stored.Edition = Trim(framework.Edition, 100);
			stored.Description = Trim(framework.Description, LmsCompetencyFramework.DescriptionMaxLength);
			stored.EvidenceMaxAgeMonths = framework.EvidenceMaxAgeMonths;
			var saved = await _frameworks.SaveOrUpdateAsync(stored, cancellationToken, true);
			Audit(saved.DepartmentId, userId, AuditLogTypes.LmsCompetencyFrameworkChanged, before, JsonConvert.SerializeObject(saved));
			return saved;
		}

		public async Task<bool> DeleteFrameworkAsync(int departmentId, int lmsCompetencyFrameworkId, string userId, CancellationToken cancellationToken = default)
		{
			var framework = await GetFrameworkAsync(departmentId, lmsCompetencyFrameworkId);
			if (framework == null)
				return false;
			var before = JsonConvert.SerializeObject(framework);
			framework.IsDeleted = true;
			framework.UpdatedOn = DateTime.UtcNow;
			framework.UpdatedByUserId = userId;
			await _frameworks.SaveOrUpdateAsync(framework, cancellationToken, true);
			Audit(departmentId, userId, AuditLogTypes.LmsCompetencyFrameworkChanged, before, JsonConvert.SerializeObject(framework));
			return true;
		}

		public async Task<LmsCompetencyItem> SaveItemAsync(LmsCompetencyItem item, string userId, CancellationToken cancellationToken = default)
		{
			if (item == null || item.DepartmentId <= 0)
				throw new InvalidOperationException("lms_program_framework_not_found");
			var framework = await RequireFrameworkAsync(item.DepartmentId, item.LmsCompetencyFrameworkId);
			var label = Trim(item.Label, LmsCompetencyItem.LabelMaxLength);
			var code = Trim(item.HumanCodingScheme, LmsCompetencyItem.CodeMaxLength);
			if (label == null && code == null)
				throw new InvalidOperationException("lms_program_label_required");

			var items = await GetItemsAsync(item.DepartmentId, framework.LmsCompetencyFrameworkId);
			if (item.ParentItemId.HasValue && items.All(i => i.LmsCompetencyItemId != item.ParentItemId.Value))
				throw new InvalidOperationException("lms_program_parent_invalid");

			LmsCompetencyItem stored;
			if (item.LmsCompetencyItemId > 0)
			{
				stored = items.FirstOrDefault(i => i.LmsCompetencyItemId == item.LmsCompetencyItemId) ?? throw new InvalidOperationException("lms_program_framework_not_found");
				// A parent may not be the item itself or one of its descendants.
				for (var parent = item.ParentItemId; parent.HasValue; parent = items.FirstOrDefault(i => i.LmsCompetencyItemId == parent.Value)?.ParentItemId)
					if (parent.Value == stored.LmsCompetencyItemId)
						throw new InvalidOperationException("lms_program_parent_invalid");
			}
			else
			{
				stored = new LmsCompetencyItem
				{
					LmsCompetencyFrameworkId = framework.LmsCompetencyFrameworkId,
					DepartmentId = item.DepartmentId,
					Identifier = "custom:" + Guid.NewGuid().ToString("N"),
					SortOrder = items.Count == 0 ? 1 : items.Max(i => i.SortOrder) + 1,
					CreatedOn = DateTime.UtcNow
				};
			}

			stored.ParentItemId = item.ParentItemId;
			stored.Label = label ?? code;
			stored.HumanCodingScheme = code;
			stored.ItemType = Trim(item.ItemType, LmsCompetencyItem.TypeMaxLength) ?? "JPR";
			if (item.SortOrder > 0)
				stored.SortOrder = item.SortOrder;
			var saved = await _items.SaveOrUpdateAsync(stored, cancellationToken, true);
			Audit(item.DepartmentId, userId, AuditLogTypes.LmsCompetencyFrameworkChanged, null, JsonConvert.SerializeObject(saved));
			return saved;
		}

		public async Task<bool> DeleteItemAsync(int departmentId, int lmsCompetencyItemId, string userId, CancellationToken cancellationToken = default)
		{
			var item = await _items.GetByIdForDepartmentAsync(departmentId, lmsCompetencyItemId);
			if (item == null || item.IsDeleted || item.DepartmentId != departmentId || await GetFrameworkAsync(departmentId, item.LmsCompetencyFrameworkId) == null)
				return false;
			item.IsDeleted = true;
			await _items.SaveOrUpdateAsync(item, cancellationToken, true);
			Audit(departmentId, userId, AuditLogTypes.LmsCompetencyFrameworkChanged, JsonConvert.SerializeObject(item), null);
			return true;
		}

		#endregion Custom frameworks and items

		#region Tags

		public async Task<List<LmsCompetencyAlignment>> GetAlignmentsAsync(int departmentId, int lmsCompetencyFrameworkId)
		{
			var items = await GetItemsAsync(departmentId, lmsCompetencyFrameworkId);
			return items.Count == 0 ? new List<LmsCompetencyAlignment>() : await _alignments.GetForItemsAsync(departmentId, items.Select(i => i.LmsCompetencyItemId)) ?? new List<LmsCompetencyAlignment>();
		}

		public async Task<List<LmsCompetencyAlignment>> GetAlignmentsForSubjectAsync(int departmentId, LmsCompetencySubjectKinds kind, string subjectId) =>
			string.IsNullOrWhiteSpace(subjectId) ? new List<LmsCompetencyAlignment>() : await _alignments.GetForSubjectAsync(departmentId, (int)kind, subjectId.Trim()) ?? new List<LmsCompetencyAlignment>();

		public async Task<LmsCompetencyAlignment> TagAsync(int departmentId, int lmsCompetencyItemId, LmsCompetencySubjectKinds kind, string subjectId, string userId,
			CancellationToken cancellationToken = default)
		{
			if (!Enum.IsDefined(typeof(LmsCompetencySubjectKinds), kind) || string.IsNullOrWhiteSpace(subjectId) || subjectId.Trim().Length > 100)
				throw new InvalidOperationException("lms_program_subject_invalid");
			var item = await _items.GetByIdForDepartmentAsync(departmentId, lmsCompetencyItemId);
			if (item == null || item.IsDeleted || item.DepartmentId != departmentId || await GetFrameworkAsync(departmentId, item.LmsCompetencyFrameworkId) == null)
				throw new InvalidOperationException("lms_program_framework_not_found");
			if (!await SubjectExistsAsync(departmentId, kind, subjectId.Trim()))
				throw new InvalidOperationException("lms_program_subject_invalid");

			var alignment = new LmsCompetencyAlignment
			{
				DepartmentId = departmentId,
				LmsCompetencyItemId = item.LmsCompetencyItemId,
				SubjectKind = (int)kind,
				SubjectId = subjectId.Trim(),
				CreatedOn = DateTime.UtcNow,
				CreatedByUserId = userId
			};
			if (await _alignments.AddAsync(alignment, cancellationToken))
				Audit(departmentId, userId, AuditLogTypes.LmsCompetencyFrameworkChanged, null, JsonConvert.SerializeObject(alignment));
			return (await GetAlignmentsForSubjectAsync(departmentId, kind, alignment.SubjectId)).FirstOrDefault(a => a.LmsCompetencyItemId == item.LmsCompetencyItemId) ?? alignment;
		}

		public async Task<bool> UntagAsync(int departmentId, int lmsCompetencyAlignmentId, string userId, CancellationToken cancellationToken = default)
		{
			var removed = await _alignments.RemoveAsync(departmentId, lmsCompetencyAlignmentId, cancellationToken) > 0;
			if (removed)
				Audit(departmentId, userId, AuditLogTypes.LmsCompetencyFrameworkChanged, JsonConvert.SerializeObject(new { lmsCompetencyAlignmentId }), null);
			return removed;
		}

		/// <summary>A subject of the department: a course, a training, a live category, a certification type, a skill sheet or a task-book task (T4).</summary>
		private async Task<bool> SubjectExistsAsync(int departmentId, LmsCompetencySubjectKinds kind, string subjectId)
		{
			// T4 (round 5C): skill sheets (their id) and task-book tasks ("{definitionId}:{taskKey}").
			if (kind == LmsCompetencySubjectKinds.SkillSheet || kind == LmsCompetencySubjectKinds.TaskBookTask)
				return await _skills.ExistsAsync(departmentId, kind, subjectId);
			if (!int.TryParse(subjectId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) || id <= 0)
				return false;
			switch (kind)
			{
				case LmsCompetencySubjectKinds.Course:
					var course = await _courses.GetVisibleByIdAsync(departmentId, id);
					return course != null && !course.IsSystem && course.DepartmentId == departmentId;
				case LmsCompetencySubjectKinds.Training:
					var training = await _trainings.GetTrainingByIdAsync(id);
					return training != null && training.DepartmentId == departmentId;
				case LmsCompetencySubjectKinds.Category:
					return (await _learningRecords.GetCategoriesAsync(departmentId) ?? new List<TrainingCategory>()).Any(c => c.TrainingCategoryId == id && !c.IsDeleted);
				case LmsCompetencySubjectKinds.CertificationType:
					var type = await _certifications.GetCertificationTypeByIdAsync(id);
					return type != null && type.DepartmentId == departmentId && !type.IsDeleted;
				default:
					return false;
			}
		}

		#endregion Tags

		#region Gap report

		public async Task<LmsCompetencyGapReport> GetMemberGapsAsync(int departmentId, int lmsCompetencyFrameworkId, string userId, DateTime utcNow)
		{
			var framework = await RequireFrameworkAsync(departmentId, lmsCompetencyFrameworkId);
			var items = await GetItemsAsync(departmentId, framework.LmsCompetencyFrameworkId);
			var alignments = items.Count == 0 ? new List<LmsCompetencyAlignment>() : await _alignments.GetForItemsAsync(departmentId, items.Select(i => i.LmsCompetencyItemId)) ?? new List<LmsCompetencyAlignment>();
			var evidence = await EvidenceAsync(departmentId, userId, alignments);
			var names = await _departments.GetAllPersonnelNamesForDepartmentAsync(departmentId) ?? new List<PersonName>();

			return new LmsCompetencyGapReport
			{
				Framework = framework,
				UserId = userId,
				MemberName = names.FirstOrDefault(n => string.Equals(n?.UserId, userId, StringComparison.OrdinalIgnoreCase))?.Name ?? userId,
				Rows = LmsCompetencyGapMath.Build(framework, items, alignments, evidence, utcNow),
				GeneratedOn = utcNow
			};
		}

		/// <summary>The member's evidence for the tagged subjects only (never the whole department).</summary>
		private async Task<List<LmsCompetencyEvidence>> EvidenceAsync(int departmentId, string userId, IReadOnlyCollection<LmsCompetencyAlignment> alignments)
		{
			var evidence = new List<LmsCompetencyEvidence>();
			if (string.IsNullOrWhiteSpace(userId) || alignments.Count == 0)
				return evidence;
			var kinds = new HashSet<int>(alignments.Select(a => a.SubjectKind));

			if (kinds.Contains((int)LmsCompetencySubjectKinds.Training) || kinds.Contains((int)LmsCompetencySubjectKinds.Category))
			{
				var transcript = (await _learningRecords.GetTranscriptAsync(departmentId, userId, null, null, null, null, false) ?? new List<LearningRecord>())
					.Where(r => r.Counts && string.Equals(r.UserId, userId, StringComparison.OrdinalIgnoreCase)).ToList();
				foreach (var record in transcript)
				{
					if (record.CategoryId.HasValue)
						evidence.Add(new LmsCompetencyEvidence { Kind = LmsCompetencySubjectKinds.Category, SubjectId = record.CategoryId.Value.ToString(CultureInfo.InvariantCulture), On = record.CompletedOn, Title = record.Title });
					// Baseline completions are keyed "training:{trainingId}:{cycle}".
					if (record.SourceKind == (int)LearningRecordSourceKinds.CourseCompletion && record.SourceRefId != null && record.SourceRefId.StartsWith("training:", StringComparison.Ordinal))
					{
						var parts = record.SourceRefId.Split(':');
						if (parts.Length >= 2 && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var trainingId))
							evidence.Add(new LmsCompetencyEvidence { Kind = LmsCompetencySubjectKinds.Training, SubjectId = trainingId.ToString(CultureInfo.InvariantCulture), On = record.CompletedOn, Title = record.Title });
					}
				}
			}

			if (kinds.Contains((int)LmsCompetencySubjectKinds.Course))
			{
				var courses = (await _courses.GetForDepartmentAsync(departmentId) ?? new List<LmsCourse>()).ToDictionary(c => c.LmsCourseId);
				foreach (var enrollment in (await _enrollments.GetForUserAsync(departmentId, userId) ?? new List<LmsEnrollment>())
					         .Where(e => e.Status == (int)LmsEnrollmentStatuses.Completed && e.CompletedOn.HasValue))
					evidence.Add(new LmsCompetencyEvidence
					{
						Kind = LmsCompetencySubjectKinds.Course,
						SubjectId = enrollment.LmsCourseId.ToString(CultureInfo.InvariantCulture),
						On = enrollment.CompletedOn.Value,
						Title = courses.TryGetValue(enrollment.LmsCourseId, out var course) ? course.Title : null
					});
			}

			if (kinds.Contains((int)LmsCompetencySubjectKinds.CertificationType))
			{
				var types = (await _certifications.GetAllCertificationTypesByDepartmentAsync(departmentId) ?? new List<DepartmentCertificationType>()).ToDictionary(t => t.DepartmentCertificationTypeId);
				foreach (var record in (await _certifications.GetCertificationsForDepartmentAsync(departmentId, new[] { userId }) ?? new List<PersonnelCertification>())
					         .Where(r => r.DepartmentCertificationTypeId.HasValue && LmsCertificationCycleMath.IsLive(r)))
					evidence.Add(new LmsCompetencyEvidence
					{
						Kind = LmsCompetencySubjectKinds.CertificationType,
						SubjectId = record.DepartmentCertificationTypeId.Value.ToString(CultureInfo.InvariantCulture),
						On = record.RecievedOn ?? record.VerifiedOn ?? DateTime.MinValue,
						Title = types.TryGetValue(record.DepartmentCertificationTypeId.Value, out var type) ? type.Type : record.Name
					});
			}

			// T4: passed, completed skill evaluations evidence their sheet and their task-book task.
			if (kinds.Contains((int)LmsCompetencySubjectKinds.SkillSheet) || kinds.Contains((int)LmsCompetencySubjectKinds.TaskBookTask))
				evidence.AddRange(await _skills.GetEvidenceAsync(departmentId, userId) ?? new List<LmsCompetencyEvidence>());

			return evidence.Where(e => e.On > DateTime.MinValue).ToList();
		}

		#endregion Gap report

		private static string Trim(string value, int max)
		{
			var text = (value ?? string.Empty).Trim();
			return text.Length == 0 ? null : text.Length > max ? text.Substring(0, max) : text;
		}

		private void Audit(int departmentId, string userId, AuditLogTypes type, string before, string after)
		{
			_events?.SendMessage(new AuditEvent { DepartmentId = departmentId, UserId = userId, Type = type, Before = before, After = after, Successful = true });
		}
	}
}
