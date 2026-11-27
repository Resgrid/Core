using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Certifications;
using Resgrid.Model.Events;
using Resgrid.Model.Helpers;
using Resgrid.Model.Lms;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Round 9A (LMS add-on): certification requirement sets. A department defines, per person-scoped certification type, the items a member
	/// must all complete inside the cycle window (LMS courses, a course set or series = a learning path, baseline trainings, a supervised
	/// practical an authorized instructor recorded, passed skill evaluations, CE hours of a category). Progress is read from the same fact
	/// queries the learning requirements use (T3) and evaluated by <see cref="LmsCertificationRequirementEvaluator"/>; when the last item
	/// completes the issuance engine issues or renews. Worker 94 catches up once per department-local day (the gate once per department with
	/// active sets, R-L9) and tells holders whose expiry nears with items still open (once per record, expiry and stage). Only active members
	/// are evaluated, issued or told (inactive-member-automation-policy).
	/// </summary>
	public class LmsCertificationRequirementService : ILmsCertificationRequirementService
	{
		public const string SystemUserId = "system";

		private readonly ILmsCertificationRequirementSetsRepository _sets;
		private readonly ILmsCertificationRequirementItemsRepository _items;
		private readonly ILmsCertificationIssuanceService _issuance;
		private readonly ICertificationService _certifications;
		private readonly IDepartmentsService _departments;
		private readonly ILmsCoursesRepository _courses;
		private readonly ITrainingService _trainings;
		private readonly ILmsPathsRepository _paths;
		private readonly ILearningRecordService _learningRecords;
		private readonly ILmsProgramStatesRepository _states;
		private readonly ILmsCertificationAlertsRepository _alerts;
		private readonly ILmsAccessService _access;
		private readonly ILmsProgramNotifier _notifier;
		private readonly IEventAggregator _events;
		private readonly LmsCertificationFactReader _reader;

		public LmsCertificationRequirementService(ILmsCertificationRequirementSetsRepository sets, ILmsCertificationRequirementItemsRepository items,
			ILmsCertificationIssuanceService issuance, ICertificationService certifications, IDepartmentsService departments, ILmsCoursesRepository courses,
			ITrainingService trainings, ILmsPathsRepository paths, ILearningRecordService learningRecords, ILmsRequirementFactsRepository facts, ILmsPathStepsRepository pathSteps,
			ILmsProgramStatesRepository states, ILmsCertificationAlertsRepository alerts, ILmsAccessService access, ILmsProgramNotifier notifier, IEventAggregator events)
		{
			_sets = sets;
			_items = items;
			_issuance = issuance;
			_certifications = certifications;
			_departments = departments;
			_courses = courses;
			_trainings = trainings;
			_paths = paths;
			_learningRecords = learningRecords;
			_states = states;
			_alerts = alerts;
			_access = access;
			_notifier = notifier;
			_events = events;
			_reader = new LmsCertificationFactReader(facts, pathSteps);
		}

		#region Sets

		public async Task<LmsCertificationRequirementSet> GetSetAsync(int departmentId, int certificationTypeId)
		{
			if (departmentId <= 0)
				return null;
			var set = await _sets.GetForTypeAsync(departmentId, certificationTypeId);
			if (set == null || set.DepartmentId != departmentId)
				return null;
			set.Items = await _items.GetForSetsAsync(departmentId, new[] { set.LmsCertificationRequirementSetId }) ?? new List<LmsCertificationRequirementItem>();
			return set;
		}

		public async Task<List<LmsCertificationRequirementSetRow>> GetSetRowsAsync(int departmentId, DateTime utcNow)
		{
			var rows = new List<LmsCertificationRequirementSetRow>();
			if (departmentId <= 0)
				return rows;
			var types = await _certifications.GetActiveCertificationTypesAsync(departmentId, CertificationAppliesTo.Person) ?? new List<DepartmentCertificationType>();
			var sets = (await _sets.GetForDepartmentAsync(departmentId) ?? new List<LmsCertificationRequirementSet>()).ToDictionary(s => s.DepartmentCertificationTypeId);
			var items = sets.Count == 0 ? new List<LmsCertificationRequirementItem>()
				: await _items.GetForSetsAsync(departmentId, sets.Values.Select(s => s.LmsCertificationRequirementSetId)) ?? new List<LmsCertificationRequirementItem>();
			var names = await GetItemNamesAsync(departmentId, items);
			var department = await _departments.GetDepartmentByIdAsync(departmentId, false);
			var localToday = TrainingDueDates.LocalDate(utcNow, department);
			var active = await _departments.GetActiveMemberUserIdsAsync(departmentId) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var records = (await _certifications.GetCertificationsForDepartmentAsync(departmentId) ?? new List<PersonnelCertification>())
				.Where(r => r != null && !r.IsDeleted && r.DepartmentCertificationTypeId.HasValue && r.UserId != null && active.Contains(r.UserId)).ToList();

			foreach (var type in types.OrderBy(t => t.Type, StringComparer.CurrentCultureIgnoreCase))
			{
				sets.TryGetValue(type.DepartmentCertificationTypeId, out var set);
				var row = new LmsCertificationRequirementSetRow { Type = type, Set = set };
				if (set != null)
					foreach (var item in items.Where(i => i.LmsCertificationRequirementSetId == set.LmsCertificationRequirementSetId))
						row.Items.Add((item, Label(item, names)));

				foreach (var holder in records.Where(r => r.DepartmentCertificationTypeId == type.DepartmentCertificationTypeId).GroupBy(r => r.UserId, StringComparer.OrdinalIgnoreCase))
				{
					var current = LmsCertificationIssuanceRules.CurrentRecord(holder, type.DepartmentCertificationTypeId, holder.Key);
					if (current == null || !LmsCertificationRequirementEvaluator.LiveOrExpired(current))
						continue;
					var expired = current.ExpiresOn.HasValue && current.ExpiresOn.Value.Date < localToday;
					if (!expired)
						row.Holders++;
					if (expired && CertificationValidity.IsInGrace(type, current, localToday))
						row.InGrace++;
					else if (!expired && current.ExpiresOn.HasValue && set != null && set.DueSoonDays > 0 && (current.ExpiresOn.Value.Date - localToday).TotalDays <= set.DueSoonDays)
						row.DueSoon++;
				}

				rows.Add(row);
			}

			return rows;
		}

		public async Task<LmsCertificationRequirementSet> SaveSetAsync(int departmentId, LmsCertificationRequirementSetInput input, string userId, CancellationToken cancellationToken = default)
		{
			if (departmentId <= 0 || input == null)
				throw new InvalidOperationException("lms_cert_req_invalid");
			var type = await _certifications.GetCertificationTypeByIdAsync(input.DepartmentCertificationTypeId);
			if (type == null || type.DepartmentId != departmentId || type.IsDeleted || type.AppliesTo != (int)CertificationAppliesTo.Person)
				throw new InvalidOperationException("lms_cert_req_type_invalid");
			var inputs = (input.Items ?? new List<LmsCertificationRequirementItemInput>()).Where(i => i != null).ToList();
			if (inputs.Count == 0)
				throw new InvalidOperationException("lms_cert_req_items_required");
			if (inputs.Count > LmsCertificationRequirementSet.MaxItems)
				throw new InvalidOperationException("lms_cert_req_too_many_items");
			if (input.DueSoonDays < 0 || input.DueSoonDays > LmsCertificationRequirementSet.MaxDueSoonDays)
				throw new InvalidOperationException("lms_cert_req_due_days_invalid");
			if (!input.IssueToNonHolders && !input.RenewHolders)
				throw new InvalidOperationException("lms_cert_req_nothing_to_do");

			var items = new List<LmsCertificationRequirementItem>();
			foreach (var item in inputs)
			{
				var problem = LmsCertificationIssuanceRules.ValidateItem(item) ?? await TargetProblemAsync(departmentId, item);
				if (problem != null)
					throw new InvalidOperationException(problem);
				var kind = (LmsRequirementKinds)item.Kind;
				items.Add(new LmsCertificationRequirementItem
				{
					DepartmentId = departmentId,
					Kind = item.Kind,
					TargetRefId = kind == LmsRequirementKinds.Evaluations ? null : item.TargetRefId,
					TargetCode = kind == LmsRequirementKinds.Evaluations ? item.TargetCode.Trim() : null,
					RequiredHours = kind == LmsRequirementKinds.CategoryHours ? Math.Round(item.RequiredHours.Value, 2) : null,
					RequiredCount = kind == LmsRequirementKinds.Evaluations ? Math.Max(1, item.RequiredCount ?? 1) : null,
					RequireRecordedByInstructor = item.RequireRecordedByInstructor && (kind == LmsRequirementKinds.Training || kind == LmsRequirementKinds.Course),
					Label = string.IsNullOrWhiteSpace(item.Label) ? null : item.Label.Trim(),
					SortOrder = items.Count + 1
				});
			}
			if (items.GroupBy(i => (i.Kind, i.TargetRefId, (i.TargetCode ?? string.Empty).ToUpperInvariant(), i.RequireRecordedByInstructor)).Any(g => g.Count() > 1))
				throw new InvalidOperationException("lms_cert_req_duplicate_item");

			var existing = await GetSetAsync(departmentId, type.DepartmentCertificationTypeId);
			var before = existing == null ? null : Describe(existing, existing.Items);
			var now = DateTime.UtcNow;
			var set = existing ?? new LmsCertificationRequirementSet { DepartmentId = departmentId, DepartmentCertificationTypeId = type.DepartmentCertificationTypeId, CreatedOn = now, CreatedByUserId = userId };
			set.IsActive = input.IsActive;
			set.IssueToNonHolders = input.IssueToNonHolders;
			set.RenewHolders = input.RenewHolders;
			set.DueSoonDays = input.DueSoonDays;
			set.NotifyMembers = input.NotifyMembers;
			if (existing != null)
			{
				set.UpdatedOn = now;
				set.UpdatedByUserId = userId;
			}
			set.Items = new List<LmsCertificationRequirementItem>();
			set = await _sets.SaveOrUpdateAsync(set, cancellationToken, true);

			// Items are replaced as a whole (they carry no member history: progress is read from the ledger).
			await _items.DeleteForSetAsync(departmentId, set.LmsCertificationRequirementSetId, cancellationToken);
			foreach (var item in items)
			{
				item.LmsCertificationRequirementSetId = set.LmsCertificationRequirementSetId;
				await _items.InsertAsync(item, cancellationToken, true);
			}
			set.Items = items;

			Audit(departmentId, userId, AuditLogTypes.CertificationRequirementSetChanged, before, Describe(set, items));
			return set;
		}

		public async Task<bool> DeleteSetAsync(int departmentId, int certificationTypeId, string userId, CancellationToken cancellationToken = default)
		{
			var set = await GetSetAsync(departmentId, certificationTypeId);
			if (set == null)
				return false;
			await _items.DeleteForSetAsync(departmentId, set.LmsCertificationRequirementSetId, cancellationToken);
			var deleted = await _sets.DeleteAsync(set, cancellationToken);
			Audit(departmentId, userId, AuditLogTypes.CertificationRequirementSetChanged, Describe(set, set.Items), null);
			return deleted;
		}

		private async Task<string> TargetProblemAsync(int departmentId, LmsCertificationRequirementItemInput item)
		{
			switch ((LmsRequirementKinds)item.Kind)
			{
				case LmsRequirementKinds.Course:
					var course = await _courses.GetVisibleByIdAsync(departmentId, item.TargetRefId.Value);
					return course == null || course.IsDeleted ? "lms_cert_req_item_target_required" : null;
				case LmsRequirementKinds.Training:
					var training = await _trainings.GetTrainingByIdAsync(item.TargetRefId.Value);
					return training == null || training.DepartmentId != departmentId ? "lms_cert_req_item_target_required" : null;
				case LmsRequirementKinds.CategoryHours:
					var categories = await _learningRecords.GetCategoriesAsync(departmentId) ?? new List<TrainingCategory>();
					return categories.Any(c => c.TrainingCategoryId == item.TargetRefId.Value && !c.IsDeleted) ? null : "lms_cert_req_item_target_required";
				case LmsRequirementKinds.Path:
					var path = await _paths.GetVisibleByIdAsync(departmentId, item.TargetRefId.Value);
					return path == null || path.IsDeleted ? "lms_cert_req_item_target_required" : null;
				default:
					return item.TargetCode != null && item.TargetCode.Trim().Length > 100 ? "lms_cert_req_item_code_required" : null;
			}
		}

		public async Task<List<(int Kind, int Id, string Name)>> GetTargetOptionsAsync(int departmentId)
		{
			var options = new List<(int Kind, int Id, string Name)>();
			if (departmentId <= 0)
				return options;
			foreach (var course in (await _courses.GetForDepartmentAsync(departmentId) ?? new List<LmsCourse>()).Where(c => !c.IsDeleted && !c.IsSystem).OrderBy(c => c.Title))
				options.Add(((int)LmsRequirementKinds.Course, course.LmsCourseId, course.Title));
			foreach (var training in (await _trainings.GetAllTrainingsForDepartmentAsync(departmentId) ?? new List<Training>()).Where(t => !t.IsArchived).OrderBy(t => t.Name))
				options.Add(((int)LmsRequirementKinds.Training, training.TrainingId, training.Name));
			foreach (var category in (await _learningRecords.GetCategoriesAsync(departmentId) ?? new List<TrainingCategory>()).Where(c => !c.IsDeleted).OrderBy(c => c.Name))
				options.Add(((int)LmsRequirementKinds.CategoryHours, category.TrainingCategoryId, category.Name));
			foreach (var path in (await _paths.GetVisibleAsync(departmentId) ?? new List<LmsPath>()).Where(p => !p.IsDeleted && p.DepartmentId == departmentId).OrderBy(p => p.Name))
				options.Add(((int)LmsRequirementKinds.Path, path.LmsPathId, path.Name));
			return options;
		}

		public async Task<Dictionary<int, string>> GetItemNamesAsync(int departmentId, IEnumerable<LmsCertificationRequirementItem> items)
		{
			var names = new Dictionary<int, string>();
			var list = (items ?? Enumerable.Empty<LmsCertificationRequirementItem>()).Where(i => i != null).ToList();
			if (list.Count == 0)
				return names;
			var courseIds = list.Where(i => i.Kind == (int)LmsRequirementKinds.Course && i.TargetRefId.HasValue).Select(i => i.TargetRefId.Value).Distinct().ToList();
			var courses = courseIds.Count == 0 ? new List<LmsCourse>() : await _courses.GetVisibleByIdsAsync(departmentId, courseIds) ?? new List<LmsCourse>();
			var categories = list.Any(i => i.Kind == (int)LmsRequirementKinds.CategoryHours)
				? await _learningRecords.GetCategoriesAsync(departmentId, true) ?? new List<TrainingCategory>() : new List<TrainingCategory>();
			var trainings = new Dictionary<int, string>();
			var paths = new Dictionary<int, string>();
			foreach (var item in list)
			{
				string name = null;
				try
				{
					switch ((LmsRequirementKinds)item.Kind)
					{
						case LmsRequirementKinds.Course:
							name = courses.FirstOrDefault(c => c.LmsCourseId == item.TargetRefId)?.Title;
							break;
						case LmsRequirementKinds.Training:
							if (!trainings.TryGetValue(item.TargetRefId ?? 0, out name))
								trainings[item.TargetRefId ?? 0] = name = item.TargetRefId.HasValue ? (await _trainings.GetTrainingByIdAsync(item.TargetRefId.Value))?.Name : null;
							break;
						case LmsRequirementKinds.CategoryHours:
							name = categories.FirstOrDefault(c => c.TrainingCategoryId == item.TargetRefId)?.Name;
							break;
						case LmsRequirementKinds.Path:
							if (!paths.TryGetValue(item.TargetRefId ?? 0, out name))
								paths[item.TargetRefId ?? 0] = name = item.TargetRefId.HasValue ? (await _paths.GetVisibleByIdAsync(departmentId, item.TargetRefId.Value))?.Name : null;
							break;
						case LmsRequirementKinds.Evaluations:
							name = item.TargetCode;
							break;
					}
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Certification requirement item {item.LmsCertificationRequirementItemId} could not be named.");
				}

				names[item.LmsCertificationRequirementItemId] = name;
			}

			return names;
		}

		private static string Label(LmsCertificationRequirementItem item, IReadOnlyDictionary<int, string> names) =>
			!string.IsNullOrWhiteSpace(item.Label) ? item.Label
			: names != null && names.TryGetValue(item.LmsCertificationRequirementItemId, out var name) && !string.IsNullOrWhiteSpace(name) ? name
			: item.TargetCode ?? "#" + (item.TargetRefId ?? 0).ToString(CultureInfo.InvariantCulture);

		#endregion Sets

		#region Progress

		/// <summary>One department's active sets with their items, types and item names, read once for a page or a run.</summary>
		private sealed class Snapshot
		{
			public Department Department { get; set; }
			public List<LmsCertificationRequirementSet> Sets { get; set; } = new List<LmsCertificationRequirementSet>();
			public Dictionary<int, DepartmentCertificationType> Types { get; set; } = new Dictionary<int, DepartmentCertificationType>();
			public Dictionary<int, string> Names { get; set; } = new Dictionary<int, string>();
			public Dictionary<int, List<LmsRequirementFact>> Facts { get; set; } = new Dictionary<int, List<LmsRequirementFact>>();
			public HashSet<string> Active { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		}

		private async Task<Snapshot> LoadAsync(int departmentId, int? onlyTypeId, DateTime utcNow, bool activeOnly = true)
		{
			var snapshot = new Snapshot { Department = await _departments.GetDepartmentByIdAsync(departmentId, false) };
			var sets = (await _sets.GetForDepartmentAsync(departmentId) ?? new List<LmsCertificationRequirementSet>())
				.Where(s => (!activeOnly || s.IsActive) && (!onlyTypeId.HasValue || s.DepartmentCertificationTypeId == onlyTypeId.Value)).ToList();
			if (sets.Count == 0)
				return snapshot;
			var types = (await _certifications.GetAllCertificationTypesByDepartmentAsync(departmentId) ?? new List<DepartmentCertificationType>())
				.Where(t => !t.IsDeleted && t.IsActive && t.AppliesTo == (int)CertificationAppliesTo.Person).ToDictionary(t => t.DepartmentCertificationTypeId);
			sets = sets.Where(s => types.ContainsKey(s.DepartmentCertificationTypeId)).ToList();
			if (sets.Count == 0)
				return snapshot;
			var items = await _items.GetForSetsAsync(departmentId, sets.Select(s => s.LmsCertificationRequirementSetId)) ?? new List<LmsCertificationRequirementItem>();
			foreach (var set in sets)
				set.Items = items.Where(i => i.LmsCertificationRequirementSetId == set.LmsCertificationRequirementSetId).OrderBy(i => i.SortOrder).ToList();
			snapshot.Sets = sets.Where(s => s.Items.Count > 0).ToList();
			snapshot.Types = types;
			snapshot.Names = await GetItemNamesAsync(departmentId, items);
			var months = snapshot.Sets.Max(s => CertificationValidity.CycleMonths(types[s.DepartmentCertificationTypeId])) + 13;
			snapshot.Facts = await _reader.ForItemsAsync(departmentId, items, null, null, utcNow.AddMonths(-months));
			snapshot.Active = await _departments.GetActiveMemberUserIdsAsync(departmentId) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			return snapshot;
		}

		private static LmsCertificationProgress Evaluate(Snapshot snapshot, LmsCertificationRequirementSet set, string userId, IEnumerable<PersonnelCertification> records, DateTime utcNow)
		{
			var mine = set.Items.ToDictionary(i => i.LmsCertificationRequirementItemId, i => snapshot.Facts.TryGetValue(i.LmsCertificationRequirementItemId, out var facts)
				? facts.Where(f => string.Equals(f.UserId, userId, StringComparison.OrdinalIgnoreCase)).ToList()
				: new List<LmsRequirementFact>());
			return LmsCertificationRequirementEvaluator.Evaluate(set, snapshot.Types[set.DepartmentCertificationTypeId], set.Items, i => Label(i, snapshot.Names), mine, userId,
				records, snapshot.Department, utcNow);
		}

		/// <summary>Whether the member has done anything toward the set (a non-holder is listed only then).</summary>
		private static bool HasStarted(Snapshot snapshot, LmsCertificationRequirementSet set, string userId) =>
			set.Items.Any(i => snapshot.Facts.TryGetValue(i.LmsCertificationRequirementItemId, out var facts) &&
			                   facts.Any(f => string.Equals(f.UserId, userId, StringComparison.OrdinalIgnoreCase)));

		public async Task<List<LmsCertificationProgress>> GetMemberProgressAsync(int departmentId, string userId, DateTime utcNow)
		{
			var progress = new List<LmsCertificationProgress>();
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(userId) || !await _sets.HasActiveSetsAsync(departmentId))
				return progress;
			var snapshot = await LoadAsync(departmentId, null, utcNow);
			if (snapshot.Sets.Count == 0)
				return progress;
			var records = await _certifications.GetCertificationsForDepartmentAsync(departmentId, new[] { userId }) ?? new List<PersonnelCertification>();
			foreach (var set in snapshot.Sets)
			{
				var holds = records.Any(r => r != null && !r.IsDeleted && r.DepartmentCertificationTypeId == set.DepartmentCertificationTypeId &&
				                             string.Equals(r.UserId, userId, StringComparison.OrdinalIgnoreCase));
				if (!holds && !(set.IssueToNonHolders && HasStarted(snapshot, set, userId)))
					continue;
				progress.Add(Evaluate(snapshot, set, userId, records, utcNow));
			}

			return progress.OrderBy(Rank).ThenBy(p => p.DaysUntilExpiry ?? int.MaxValue).ThenBy(p => p.CertificationTypeName).ToList();
		}

		public async Task<List<LmsCertificationProgress>> GetTypeProgressAsync(int departmentId, int certificationTypeId, DateTime utcNow)
		{
			var progress = new List<LmsCertificationProgress>();
			if (departmentId <= 0)
				return progress;
			var snapshot = await LoadAsync(departmentId, certificationTypeId, utcNow, activeOnly: false);
			var set = snapshot.Sets.FirstOrDefault();
			if (set == null)
				return progress;
			var records = (await _certifications.GetCertificationsForDepartmentAsync(departmentId) ?? new List<PersonnelCertification>())
				.Where(r => r != null && !r.IsDeleted && r.UserId != null).ToList();
			var names = (await _departments.GetAllPersonnelNamesForDepartmentAsync(departmentId) ?? new List<PersonName>())
				.Where(n => n?.UserId != null).GroupBy(n => n.UserId, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);
			foreach (var userId in Candidates(snapshot, set, records))
			{
				var row = Evaluate(snapshot, set, userId, records.Where(r => string.Equals(r.UserId, userId, StringComparison.OrdinalIgnoreCase)), utcNow);
				row.MemberName = names.TryGetValue(userId, out var name) ? name : userId;
				progress.Add(row);
			}

			return progress.OrderBy(Rank).ThenBy(p => p.DaysUntilExpiry ?? int.MaxValue).ThenBy(p => p.MemberName, StringComparer.CurrentCultureIgnoreCase).ToList();
		}

		/// <summary>The active members a set concerns: holders of the type, and members who started it when it issues to non-holders.</summary>
		private static IEnumerable<string> Candidates(Snapshot snapshot, LmsCertificationRequirementSet set, IEnumerable<PersonnelCertification> records)
		{
			var users = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (var record in records.Where(r => r.DepartmentCertificationTypeId == set.DepartmentCertificationTypeId))
				users.Add(record.UserId);
			if (set.IssueToNonHolders)
				foreach (var item in set.Items)
					if (snapshot.Facts.TryGetValue(item.LmsCertificationRequirementItemId, out var facts))
						foreach (var fact in facts)
							users.Add(fact.UserId);
			return users.Where(u => !string.IsNullOrWhiteSpace(u) && snapshot.Active.Contains(u) && !LmsExternalLearnerIds.IsExternal(u)).OrderBy(u => u, StringComparer.OrdinalIgnoreCase);
		}

		/// <summary>Closest to lapsing first: in grace, due soon, lapsed, complete, current, not held, blocked.</summary>
		private static int Rank(LmsCertificationProgress progress) => progress.State switch
		{
			LmsCertificationProgressStates.InGrace => 0,
			LmsCertificationProgressStates.DueSoon => 1,
			LmsCertificationProgressStates.Lapsed => 2,
			LmsCertificationProgressStates.Complete => 3,
			LmsCertificationProgressStates.Current => 4,
			LmsCertificationProgressStates.NotHeld => 5,
			_ => 6
		};

		#endregion Progress

		#region Engine

		public async Task<LmsCertificationRequirementRunResult> EvaluateMembersAsync(int departmentId, IReadOnlyCollection<string> userIds, string actorUserId, DateTime utcNow,
			CancellationToken cancellationToken = default)
		{
			var result = new LmsCertificationRequirementRunResult();
			var users = (userIds ?? Array.Empty<string>()).Where(u => !string.IsNullOrWhiteSpace(u) && !LmsExternalLearnerIds.IsExternal(u))
				.Select(u => u.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			if (departmentId <= 0 || users.Count == 0)
				return result;
			var snapshot = await LoadAsync(departmentId, null, utcNow);
			if (snapshot.Sets.Count == 0)
				return result;
			var records = await _certifications.GetCertificationsForDepartmentAsync(departmentId, users.Count <= 500 ? users : null) ?? new List<PersonnelCertification>();
			foreach (var userId in users.Where(snapshot.Active.Contains))
			{
				var mine = records.Where(r => r != null && string.Equals(r.UserId, userId, StringComparison.OrdinalIgnoreCase)).ToList();
				foreach (var set in snapshot.Sets)
				{
					cancellationToken.ThrowIfCancellationRequested();
					await IssueIfCompleteAsync(snapshot, set, userId, mine, actorUserId, utcNow, result, cancellationToken);
				}
			}

			return result;
		}

		private async Task<LmsCertificationProgress> IssueIfCompleteAsync(Snapshot snapshot, LmsCertificationRequirementSet set, string userId, List<PersonnelCertification> records,
			string actorUserId, DateTime utcNow, LmsCertificationRequirementRunResult result, CancellationToken cancellationToken)
		{
			var progress = Evaluate(snapshot, set, userId, records, utcNow);
			if (progress.State != LmsCertificationProgressStates.Complete || !progress.CompletedOnUtc.HasValue)
				return progress;
			if (progress.IsRenewal ? !set.RenewHolders : !set.IssueToNonHolders)
				return progress;
			try
			{
				var issued = await _issuance.IssueOrRenewAsync(new LmsCertificationIssueRequest
				{
					DepartmentId = set.DepartmentId,
					UserId = userId,
					CertificationTypeId = set.DepartmentCertificationTypeId,
					CompletedOnUtc = progress.CompletedOnUtc.Value,
					Source = LmsCertificationIssueSources.RequirementSet,
					SourceRefId = LmsCertificationIssuanceRules.SetRef(set.LmsCertificationRequirementSetId),
					BasisRecordIds = progress.BasisRecordIds,
					ActorUserId = actorUserId,
					NotifyMember = set.NotifyMembers,
					RenewOnly = !set.IssueToNonHolders
				}, cancellationToken);
				if (issued.Action == LmsCertificationIssueActions.Issued)
					result.Issued++;
				else if (issued.Action == LmsCertificationIssueActions.Renewed)
					result.Renewed++;
				else
					result.Skipped++;
				if (issued.Changed)
				{
					// The record changed: the caller works from the new state (a second set of the same type must not act on the old one).
					records.RemoveAll(r => r.PersonnelCertificationId == issued.CertificationId);
					var fresh = (await _certifications.GetCertificationsForDepartmentAsync(set.DepartmentId, new[] { userId }) ?? new List<PersonnelCertification>())
						.FirstOrDefault(r => r.PersonnelCertificationId == issued.CertificationId);
					if (fresh != null)
						records.Add(fresh);
				}
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				result.Failures++;
				Logging.LogException(ex, $"Certification requirement set {set.LmsCertificationRequirementSetId} could not issue for {userId}.");
			}

			return progress;
		}

		public async Task<LmsCertificationRequirementRunResult> RunDueAsync(DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var result = new LmsCertificationRequirementRunResult();
			foreach (var departmentId in await _sets.GetDepartmentIdsWithActiveSetsAsync() ?? new List<int>())
			{
				cancellationToken.ThrowIfCancellationRequested();
				DateTime localToday = default;
				var claimed = false;
				try
				{
					var department = await _departments.GetDepartmentByIdAsync(departmentId, false);
					if (department == null)
						continue;
					localToday = TrainingDueDates.LocalDate(utcNow, department);
					if (!await _states.TryClaimRequirementSetSweepAsync(departmentId, localToday, cancellationToken))
						continue;
					claimed = true;

					// The gate once per department with due work, never per member (R-L9). No add-on: the engine pauses (lapse rule 5).
					if (!await _access.CanAuthorAndManageAsync(departmentId))
					{
						result.Skipped++;
						continue;
					}

					var one = await RunDepartmentAsync(departmentId, utcNow, cancellationToken);
					result.Departments++;
					result.Issued += one.Issued;
					result.Renewed += one.Renewed;
					result.DueNotices += one.DueNotices;
					result.Failures += one.Failures;
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					result.Failures++;
					Logging.LogException(ex, $"Certification requirement sweep failed for department {departmentId}.");
					if (claimed)
						await _states.ReleaseRequirementSetSweepAsync(departmentId, localToday, cancellationToken);
				}
			}

			return result;
		}

		public async Task<LmsCertificationRequirementRunResult> RunDepartmentAsync(int departmentId, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var result = new LmsCertificationRequirementRunResult { Departments = 1 };
			var snapshot = await LoadAsync(departmentId, null, utcNow);
			if (snapshot.Sets.Count == 0)
				return result;
			var records = (await _certifications.GetCertificationsForDepartmentAsync(departmentId) ?? new List<PersonnelCertification>())
				.Where(r => r != null && !r.IsDeleted && r.UserId != null).ToList();
			var byMember = records.GroupBy(r => r.UserId, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

			foreach (var set in snapshot.Sets)
			{
				var type = snapshot.Types[set.DepartmentCertificationTypeId];
				foreach (var userId in Candidates(snapshot, set, records).ToList())
				{
					cancellationToken.ThrowIfCancellationRequested();
					try
					{
						if (!byMember.TryGetValue(userId, out var mine))
							byMember[userId] = mine = new List<PersonnelCertification>();
						var progress = await IssueIfCompleteAsync(snapshot, set, userId, mine, null, utcNow, result, cancellationToken);
						if (progress.State == LmsCertificationProgressStates.DueSoon || progress.State == LmsCertificationProgressStates.InGrace)
							result.DueNotices += await NotifyDueAsync(set, type, progress, snapshot.Department, cancellationToken);
					}
					catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
					{
						throw;
					}
					catch (Exception ex)
					{
						result.Failures++;
						Logging.LogException(ex, $"Certification requirement step failed for {userId}, set {set.LmsCertificationRequirementSetId}, department {departmentId}.");
					}
				}
			}

			return result;
		}

		/// <summary>
		/// "Requirements due" (EventTypes 78, trigger 278): once per record, expiry and stage (due soon, then in grace), claimed before it is
		/// sent so a repeated run never doubles it. The member only when the set tells members; the workflow event either way.
		/// </summary>
		private async Task<int> NotifyDueAsync(LmsCertificationRequirementSet set, DepartmentCertificationType type, LmsCertificationProgress progress, Department department,
			CancellationToken cancellationToken)
		{
			if (!progress.CertificationId.HasValue || !progress.ExpiresOn.HasValue)
				return 0;
			var stage = progress.State == LmsCertificationProgressStates.InGrace ? "grace" : "due";
			var claim = new LmsCertificationAlert
			{
				DepartmentId = set.DepartmentId,
				PersonnelCertificationId = progress.CertificationId.Value,
				Kind = (int)LmsCertificationAlertKinds.RequirementsDue,
				AlertKey = "req:" + progress.ExpiresOn.Value.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ":" + stage,
				SentOn = DateTime.UtcNow
			};
			if (!await _alerts.TryClaimAsync(claim, cancellationToken))
				return 0;

			var sent = 0;
			if (set.NotifyMembers)
			{
				var graceEnds = progress.ExpiresOn.Value.AddDays(CertificationValidity.GraceDays(type));
				sent = await _notifier.SendAsync(set.DepartmentId, new[] { progress.UserId }, "CertReq.Notify.DueTitle",
					stage == "grace" ? "CertReq.Notify.DueGrace" : "CertReq.Notify.Due",
					culture => new object[] { type.Type, progress.ExpiresOn.Value.ToString("d", culture), progress.Done, progress.Total, graceEnds.ToString("d", culture) },
					LmsCertificationIssuanceService.EventCode(progress.CertificationId.Value), cancellationToken);
				await _alerts.SetRecipientsAsync(claim.LmsCertificationAlertId, sent, cancellationToken);
			}

			_events?.SendMessage(new CertificationTrainingWorkflowEvent
			{
				DepartmentId = set.DepartmentId,
				Change = CertificationTrainingWorkflowChanges.RequirementsDue,
				CertificationId = progress.CertificationId.Value,
				CertificationTypeId = type.DepartmentCertificationTypeId,
				CertificationTypeName = type.Type,
				CertificationTypeCode = type.Code,
				UserId = progress.UserId,
				Source = LmsCertificationIssueSources.RequirementSet.ToString(),
				Outcome = stage == "grace" ? "InGrace" : "DueSoon",
				ExpiresOn = progress.ExpiresOn,
				ItemsDone = progress.Done,
				ItemsTotal = progress.Total,
				DaysUntilExpiry = progress.DaysUntilExpiry ?? 0,
				OccurredOn = DateTime.UtcNow
			});
			return sent > 0 ? 1 : 0;
		}

		#endregion Engine

		private static string Describe(LmsCertificationRequirementSet set, IEnumerable<LmsCertificationRequirementItem> items) => JsonConvert.SerializeObject(new
		{
			set.LmsCertificationRequirementSetId,
			set.DepartmentCertificationTypeId,
			set.IsActive,
			set.IssueToNonHolders,
			set.RenewHolders,
			set.DueSoonDays,
			set.NotifyMembers,
			Items = (items ?? Enumerable.Empty<LmsCertificationRequirementItem>()).Select(i => new
			{
				Kind = ((LmsRequirementKinds)i.Kind).ToString(), i.TargetRefId, i.TargetCode, i.RequiredHours, i.RequiredCount, i.RequireRecordedByInstructor, i.Label
			})
		});

		private void Audit(int departmentId, string userId, AuditLogTypes type, string before, string after) =>
			_events?.SendMessage(new AuditEvent { DepartmentId = departmentId, UserId = userId, Type = type, Before = before, After = after, Successful = true });
	}
}
