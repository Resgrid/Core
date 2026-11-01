using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autofac.Features.Indexed;
using Newtonsoft.Json;
using Resgrid.Model;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Dashboard
{
	/// <summary>
	/// Boards, templates and layout resolution for the home dashboard (home-dashboard-redesign-plan.md §5). Resolution order:
	/// the member's saved board, the first template matching one of their roles or groups, the department default
	/// template, the built-in default for their audience. The inputs (saved rows, templates) are cached, never the resolved
	/// board, so a role or group change takes effect at once (plan §5.3).
	/// </summary>
	public class DashboardService : IDashboardService
	{
		private static readonly TimeSpan CacheLength = TimeSpan.FromDays(1);
		private const string BoardsCacheKey = "DashboardBoards_{0}_{1}";
		private const string TemplatesCacheKey = "DashboardTemplates_{0}";

		private readonly IUserDashboardsRepository _userDashboardsRepository;
		private readonly IDepartmentDashboardTemplatesRepository _templatesRepository;
		private readonly ICacheProvider _cacheProvider;
		private readonly IIndex<string, IDashboardOptionSource> _optionSources;

		public DashboardService(IUserDashboardsRepository userDashboardsRepository, IDepartmentDashboardTemplatesRepository templatesRepository,
			ICacheProvider cacheProvider, IIndex<string, IDashboardOptionSource> optionSources)
		{
			_userDashboardsRepository = userDashboardsRepository;
			_templatesRepository = templatesRepository;
			_cacheProvider = cacheProvider;
			_optionSources = optionSources;
		}

		#region Boards
		public async Task<DashboardBoards> GetBoardsAsync(int departmentId, string userId, DashboardViewer viewer)
		{
			var rows = await GetRowsAsync(departmentId, userId);
			var result = new DashboardBoards { UseClassic = rows.Any(r => r.UseClassic) };

			if (rows.Count == 0 || rows.All(r => r.SortOrder != 0))
				result.Boards.Add(await ResolveDefaultAsync(departmentId, viewer));

			foreach (var row in rows.OrderBy(r => r.SortOrder))
			{
				// A home row without a layout only carries the classic switch: it still follows the department default.
				if (row.SortOrder == 0 && string.IsNullOrWhiteSpace(row.LayoutJson))
				{
					var resolved = await ResolveDefaultAsync(departmentId, viewer);
					resolved.BoardId = row.UserDashboardId;
					resolved.RowVersion = row.RowVersion;
					resolved.UseClassic = row.UseClassic;
					result.Boards.Add(resolved);
					continue;
				}

				result.Boards.Add(ToBoard(row));
			}

			return result;
		}

		public async Task<DashboardResolvedBoard> GetBoardAsync(int departmentId, string userId, DashboardViewer viewer, string boardId = null)
		{
			var boards = await GetBoardsAsync(departmentId, userId, viewer);
			return (boardId == null ? null : boards.Boards.FirstOrDefault(b => b.BoardId == boardId)) ?? boards.Boards[0];
		}

		public async Task<DashboardSaveResult> SaveBoardAsync(int departmentId, string userId, DashboardViewer viewer, string boardId, int rowVersion,
			DashboardLayout layout, string name, CancellationToken cancellationToken)
		{
			var normalized = DashboardLayoutValidator.Normalize(layout);
			if (normalized == null)
				return new DashboardSaveResult { Error = DashboardSaveErrors.Invalid };

			var json = DashboardLayoutValidator.Serialize(normalized);
			if (json.Length > DashboardLayout.MaxJsonLength)
				return new DashboardSaveResult { Error = DashboardSaveErrors.TooLarge };

			var rows = await _userDashboardsRepository.GetForUserAsync(departmentId, userId) ?? new List<UserDashboard>();
			var row = boardId == null ? rows.FirstOrDefault(r => r.SortOrder == 0) : rows.FirstOrDefault(r => r.UserDashboardId == boardId);
			var now = DateTime.UtcNow;

			if (row == null)
			{
				// Only the home board is created by a save; extra boards come from AddBoardAsync.
				if (boardId != null)
					return new DashboardSaveResult { Error = DashboardSaveErrors.NotFound };

				// rowVersion 0 = "I loaded a resolved default"; anything else means a row existed and is gone or never was.
				if (rowVersion != 0)
					return new DashboardSaveResult { Error = DashboardSaveErrors.Conflict, Current = await ResolveDefaultAsync(departmentId, viewer) };

				var resolved = await ResolveDefaultAsync(departmentId, viewer);
				row = new UserDashboard
				{
					UserDashboardId = Guid.NewGuid().ToString(),
					DepartmentId = departmentId,
					UserId = userId,
					Name = Truncate(name, DashboardLimits.MaxNameLength),
					SortOrder = 0,
					LayoutJson = json,
					SchemaVersion = DashboardLayout.CurrentSchemaVersion,
					TemplateId = resolved.TemplateId,
					UseClassic = rows.Any(r => r.UseClassic),
					CreatedOn = now,
					UpdatedOn = now,
					RowVersion = 1
				};

				try
				{
					await _userDashboardsRepository.InsertAsync(row, CancellationToken.None, true);
				}
				catch (Exception)
				{
					// Another browser created the home board first (unique department/user/sort index); anything else rethrows.
					if (!await HomeRowExistsAsync(departmentId, userId))
						throw;

					await InvalidateUserAsync(departmentId, userId);
					return new DashboardSaveResult { Error = DashboardSaveErrors.Conflict, Current = await GetBoardAsync(departmentId, userId, viewer) };
				}

				await InvalidateUserAsync(departmentId, userId);
				return new DashboardSaveResult { Board = ToBoard(row) };
			}

			row.LayoutJson = json;
			row.SchemaVersion = DashboardLayout.CurrentSchemaVersion;
			if (name != null)
				row.Name = Truncate(name, DashboardLimits.MaxNameLength);
			row.UpdatedOn = now;

			// Writes bind CancellationToken.None: a browser closing mid-save must not leave the caller unsure what was stored.
			var updated = await _userDashboardsRepository.UpdateWithRowVersionAsync(row, rowVersion, CancellationToken.None);
			await InvalidateUserAsync(departmentId, userId);

			if (!updated)
				return new DashboardSaveResult { Error = DashboardSaveErrors.Conflict, Current = await GetBoardAsync(departmentId, userId, viewer, boardId) };

			return new DashboardSaveResult { Board = ToBoard(row) };
		}

		public async Task<DashboardSaveResult> AddBoardAsync(int departmentId, string userId, DashboardViewer viewer, string name, CancellationToken cancellationToken)
		{
			var rows = await _userDashboardsRepository.GetForUserAsync(departmentId, userId) ?? new List<UserDashboard>();
			var count = rows.Count + (rows.Any(r => r.SortOrder == 0) ? 0 : 1);
			if (count >= DashboardLimits.MaxBoards)
				return new DashboardSaveResult { Error = DashboardSaveErrors.LimitReached };

			var resolved = await ResolveDefaultAsync(departmentId, viewer);
			var now = DateTime.UtcNow;
			var row = new UserDashboard
			{
				UserDashboardId = Guid.NewGuid().ToString(),
				DepartmentId = departmentId,
				UserId = userId,
				Name = Truncate(string.IsNullOrWhiteSpace(name) ? null : name.Trim(), DashboardLimits.MaxNameLength),
				SortOrder = Math.Max(1, rows.Count == 0 ? 1 : rows.Max(r => r.SortOrder) + 1),
				LayoutJson = DashboardLayoutValidator.Serialize(resolved.Layout),
				SchemaVersion = DashboardLayout.CurrentSchemaVersion,
				TemplateId = resolved.TemplateId,
				UseClassic = rows.Any(r => r.UseClassic),
				CreatedOn = now,
				UpdatedOn = now,
				RowVersion = 1
			};

			await _userDashboardsRepository.InsertAsync(row, CancellationToken.None, true);
			await InvalidateUserAsync(departmentId, userId);

			return new DashboardSaveResult { Board = ToBoard(row) };
		}

		public async Task<bool> DeleteBoardAsync(int departmentId, string userId, string boardId, CancellationToken cancellationToken)
		{
			if (string.IsNullOrWhiteSpace(boardId))
				return false;

			var row = await _userDashboardsRepository.GetByIdForUserAsync(boardId, departmentId, userId);
			if (row == null || row.SortOrder == 0)
				return false;

			var deleted = await _userDashboardsRepository.DeleteAsync(row, CancellationToken.None);
			await InvalidateUserAsync(departmentId, userId);
			return deleted;
		}

		public async Task<DashboardResolvedBoard> ResetBoardAsync(int departmentId, string userId, DashboardViewer viewer, string boardId, CancellationToken cancellationToken)
		{
			var rows = await _userDashboardsRepository.GetForUserAsync(departmentId, userId) ?? new List<UserDashboard>();
			var row = boardId == null ? rows.FirstOrDefault(r => r.SortOrder == 0) : rows.FirstOrDefault(r => r.UserDashboardId == boardId);
			var resolved = await ResolveDefaultAsync(departmentId, viewer);

			if (row == null)
				return resolved;

			if (row.SortOrder == 0)
			{
				// Follows the department default from now on, so template edits reach the member again: no row, or (when the
				// row also carries the classic switch) a row without a layout.
				if (row.UseClassic)
				{
					row.LayoutJson = null;
					row.TemplateId = null;
					row.UpdatedOn = DateTime.UtcNow;
					await _userDashboardsRepository.UpdateWithRowVersionAsync(row, row.RowVersion, CancellationToken.None);
				}
				else
				{
					await _userDashboardsRepository.DeleteAsync(row, CancellationToken.None);
				}

				await InvalidateUserAsync(departmentId, userId);
				resolved.BoardId = row.UseClassic ? row.UserDashboardId : null;
				resolved.RowVersion = row.UseClassic ? row.RowVersion : 0;
				resolved.UseClassic = row.UseClassic;
				return resolved;
			}

			row.LayoutJson = DashboardLayoutValidator.Serialize(resolved.Layout);
			row.TemplateId = resolved.TemplateId;
			row.UpdatedOn = DateTime.UtcNow;
			await _userDashboardsRepository.UpdateWithRowVersionAsync(row, row.RowVersion, CancellationToken.None);
			await InvalidateUserAsync(departmentId, userId);
			return ToBoard(row);
		}

		public async Task SetUseClassicAsync(int departmentId, string userId, bool useClassic, CancellationToken cancellationToken)
		{
			var rows = await _userDashboardsRepository.GetForUserAsync(departmentId, userId) ?? new List<UserDashboard>();
			if (rows.Count == 0)
			{
				if (!useClassic)
					return;

				// The switch needs a row; without a layout the home board keeps following the department default.
				var now = DateTime.UtcNow;
				await _userDashboardsRepository.InsertAsync(new UserDashboard
				{
					UserDashboardId = Guid.NewGuid().ToString(),
					DepartmentId = departmentId,
					UserId = userId,
					SortOrder = 0,
					LayoutJson = null,
					SchemaVersion = DashboardLayout.CurrentSchemaVersion,
					UseClassic = true,
					CreatedOn = now,
					UpdatedOn = now,
					RowVersion = 1
				}, CancellationToken.None, true);
			}
			else
			{
				await _userDashboardsRepository.SetUseClassicAsync(departmentId, userId, useClassic, CancellationToken.None);
			}

			await InvalidateUserAsync(departmentId, userId);
		}

		public async Task<bool> GetUseClassicAsync(int departmentId, string userId)
		{
			var rows = await GetRowsAsync(departmentId, userId);
			return rows.Any(r => r.UseClassic);
		}

		public DashboardLayout GetBuiltInLayout(DashboardAudiences audience, bool classic = false) =>
			DashboardLayoutValidator.Normalize(classic ? DashboardBuiltInLayouts.ForClassic() : DashboardBuiltInLayouts.For(audience));
		#endregion Boards

		#region Templates
		public async Task<List<DepartmentDashboardTemplate>> GetTemplatesAsync(int departmentId)
		{
			async Task<List<DepartmentDashboardTemplate>> load() =>
				await _templatesRepository.GetForDepartmentAsync(departmentId) ?? new List<DepartmentDashboardTemplate>();

			if (!Config.SystemBehaviorConfig.CacheEnabled)
				return await load();

			var key = string.Format(TemplatesCacheKey, departmentId);
			var cached = await ReadCacheAsync<CachedRows<DepartmentDashboardTemplate>>(key);
			if (cached != null)
				return cached.Rows ?? new List<DepartmentDashboardTemplate>();

			var rows = await load();
			await WriteCacheAsync(key, new CachedRows<DepartmentDashboardTemplate> { Rows = rows });
			return rows;
		}

		public async Task<DepartmentDashboardTemplate> GetTemplateAsync(int departmentId, string templateId)
		{
			if (string.IsNullOrWhiteSpace(templateId))
				return null;

			return (await GetTemplatesAsync(departmentId)).FirstOrDefault(t => t.DepartmentDashboardTemplateId == templateId);
		}

		public async Task<DashboardTemplateSaveResult> SaveTemplateAsync(int departmentId, string userId, DashboardTemplateSaveRequest request, CancellationToken cancellationToken)
		{
			if (request == null || string.IsNullOrWhiteSpace(request.Name))
				return new DashboardTemplateSaveResult { Error = DashboardSaveErrors.Invalid };

			var layout = DashboardLayoutValidator.Normalize(request.Layout);
			if (layout == null)
				return new DashboardTemplateSaveResult { Error = DashboardSaveErrors.Invalid };

			var json = DashboardLayoutValidator.Serialize(layout);
			if (json.Length > DashboardLayout.MaxJsonLength)
				return new DashboardTemplateSaveResult { Error = DashboardSaveErrors.TooLarge };

			var audience = Enum.IsDefined(typeof(DashboardTemplateAudiences), request.Audience) ? request.Audience : DashboardTemplateAudiences.Everyone;
			if (audience != DashboardTemplateAudiences.Everyone && (!request.TargetId.HasValue || request.TargetId.Value <= 0))
				return new DashboardTemplateSaveResult { Error = DashboardSaveErrors.Invalid };

			var existing = await _templatesRepository.GetForDepartmentAsync(departmentId) ?? new List<DepartmentDashboardTemplate>();
			var now = DateTime.UtcNow;
			DepartmentDashboardTemplate template;
			var isNew = string.IsNullOrWhiteSpace(request.DepartmentDashboardTemplateId);

			if (isNew)
			{
				if (existing.Count >= DashboardLimits.MaxTemplatesPerDepartment)
					return new DashboardTemplateSaveResult { Error = DashboardSaveErrors.LimitReached };

				template = new DepartmentDashboardTemplate
				{
					DepartmentDashboardTemplateId = Guid.NewGuid().ToString(),
					DepartmentId = departmentId,
					CreatedByUserId = userId,
					CreatedOn = now
				};
			}
			else
			{
				template = existing.FirstOrDefault(t => t.DepartmentDashboardTemplateId == request.DepartmentDashboardTemplateId.Trim());
				if (template == null || template.DepartmentId != departmentId)
					return new DashboardTemplateSaveResult { Error = DashboardSaveErrors.NotFound };
			}

			template.Name = Truncate(request.Name.Trim(), DashboardLimits.MaxNameLength);
			template.Description = string.IsNullOrWhiteSpace(request.Description) ? null : Truncate(request.Description.Trim(), DashboardLimits.MaxDescriptionLength);
			template.Audience = (int)audience;
			template.TargetId = audience == DashboardTemplateAudiences.Everyone ? null : request.TargetId;
			template.Priority = Math.Clamp(request.Priority, 0, 1000);
			template.IsDefault = request.IsDefault;
			template.LayoutJson = json;
			template.SchemaVersion = DashboardLayout.CurrentSchemaVersion;
			template.UpdatedByUserId = userId;
			template.UpdatedOn = now;

			if (template.IsDefault)
				await _templatesRepository.ClearDefaultAsync(departmentId, template.DepartmentDashboardTemplateId, CancellationToken.None);

			if (isNew)
				await _templatesRepository.InsertAsync(template, CancellationToken.None, true);
			else
				await _templatesRepository.UpdateAsync(template, CancellationToken.None, true);

			await InvalidateTemplatesAsync(departmentId);
			return new DashboardTemplateSaveResult { Template = template };
		}

		public async Task<bool> DeleteTemplateAsync(int departmentId, string userId, string templateId, CancellationToken cancellationToken)
		{
			if (string.IsNullOrWhiteSpace(templateId))
				return false;

			var template = await _templatesRepository.GetByIdForDepartmentAsync(templateId.Trim(), departmentId);
			if (template == null || template.DepartmentId != departmentId)
				return false;

			var deleted = await _templatesRepository.DeleteAsync(template, CancellationToken.None);
			await InvalidateTemplatesAsync(departmentId);
			return deleted;
		}
		#endregion Templates

		public async Task<Dictionary<string, List<KeyValuePair<string, string>>>> GetSettingOptionsAsync(int departmentId, string userId, IEnumerable<string> sources)
		{
			var result = new Dictionary<string, List<KeyValuePair<string, string>>>(StringComparer.Ordinal);
			foreach (var source in (sources ?? Enumerable.Empty<string>()).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.Ordinal))
			{
				// Sequential on purpose: every source reads through the request's unit of work.
				if (_optionSources.TryGetValue(source, out var optionSource))
					result[source] = await optionSource.GetOptionsAsync(departmentId, userId) ?? new List<KeyValuePair<string, string>>();
				else
					result[source] = new List<KeyValuePair<string, string>>();
			}

			return result;
		}

		public async Task InvalidateUserAsync(int departmentId, string userId)
		{
			if (Config.SystemBehaviorConfig.CacheEnabled)
				await _cacheProvider.RemoveAsync(string.Format(BoardsCacheKey, departmentId, userId));
		}

		#region Resolution
		private async Task<DashboardResolvedBoard> ResolveDefaultAsync(int departmentId, DashboardViewer viewer)
		{
			var templates = await GetTemplatesAsync(departmentId);
			var roleIds = new HashSet<int>(viewer?.PersonnelRoleIds ?? Array.Empty<int>());
			var groupIds = new HashSet<int>(viewer?.GroupIds ?? Array.Empty<int>());

			var match = templates
				.Where(t => (t.Audience == (int)DashboardTemplateAudiences.PersonnelRole && t.TargetId.HasValue && roleIds.Contains(t.TargetId.Value)) ||
					(t.Audience == (int)DashboardTemplateAudiences.Group && t.TargetId.HasValue && groupIds.Contains(t.TargetId.Value)))
				.OrderBy(t => t.Priority).ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
				.FirstOrDefault() ?? templates.FirstOrDefault(t => t.IsDefault);

			var layout = match == null ? null : DashboardLayoutValidator.Normalize(DashboardLayoutValidator.Deserialize(match.LayoutJson));
			if (layout != null)
				return new DashboardResolvedBoard { Layout = layout, Source = DashboardLayoutSources.Template, TemplateId = match.DepartmentDashboardTemplateId };

			return new DashboardResolvedBoard
			{
				Layout = GetBuiltInLayout(viewer?.Audience ?? DashboardAudiences.Member),
				Source = DashboardLayoutSources.Default
			};
		}

		private static DashboardResolvedBoard ToBoard(UserDashboard row)
		{
			var layout = DashboardLayoutValidator.Normalize(DashboardLayoutValidator.Deserialize(row.LayoutJson)) ?? new DashboardLayout();
			return new DashboardResolvedBoard
			{
				BoardId = row.UserDashboardId,
				Name = row.Name,
				SortOrder = row.SortOrder,
				Layout = layout,
				RowVersion = row.RowVersion,
				Source = DashboardLayoutSources.User,
				TemplateId = row.TemplateId,
				UseClassic = row.UseClassic
			};
		}

		private async Task<List<UserDashboard>> GetRowsAsync(int departmentId, string userId)
		{
			async Task<List<UserDashboard>> load() =>
				await _userDashboardsRepository.GetForUserAsync(departmentId, userId) ?? new List<UserDashboard>();

			if (!Config.SystemBehaviorConfig.CacheEnabled)
				return await load();

			var key = string.Format(BoardsCacheKey, departmentId, userId);
			var cached = await ReadCacheAsync<CachedRows<UserDashboard>>(key);
			if (cached != null)
				return cached.Rows ?? new List<UserDashboard>();

			var rows = await load();
			await WriteCacheAsync(key, new CachedRows<UserDashboard> { Rows = rows });
			return rows;
		}

		private async Task<bool> HomeRowExistsAsync(int departmentId, string userId) =>
			(await _userDashboardsRepository.GetForUserAsync(departmentId, userId) ?? new List<UserDashboard>()).Any(r => r.SortOrder == 0);

		private Task InvalidateTemplatesAsync(int departmentId) =>
			Config.SystemBehaviorConfig.CacheEnabled ? _cacheProvider.RemoveAsync(string.Format(TemplatesCacheKey, departmentId)) : Task.CompletedTask;

		// JSON through the string cache, not the ProtoBuf object path: the wrapper's HasValue distinguishes "no rows" from an
		// empty Redis payload, which the object path would hand back as a blank entity (cache poisoning).
		private async Task<T> ReadCacheAsync<T>(string key) where T : class, ICachedWrapper
		{
			try
			{
				var json = await _cacheProvider.GetStringAsync(key);
				if (string.IsNullOrWhiteSpace(json))
					return null;

				var value = JsonConvert.DeserializeObject<T>(json);
				return value != null && value.HasValue ? value : null;
			}
			catch (Exception)
			{
				return null;
			}
		}

		private async Task WriteCacheAsync<T>(string key, T value) where T : class, ICachedWrapper
		{
			try
			{
				value.HasValue = true;
				await _cacheProvider.SetStringAsync(key, JsonConvert.SerializeObject(value), CacheLength);
			}
			catch (Exception)
			{
				// A cache outage must never fail the dashboard.
			}
		}

		private interface ICachedWrapper
		{
			bool HasValue { get; set; }
		}

		private sealed class CachedRows<TRow> : ICachedWrapper
		{
			public bool HasValue { get; set; }

			public List<TRow> Rows { get; set; }
		}
		#endregion Resolution

		private static string Truncate(string value, int length) =>
			value == null ? null : value.Length <= length ? value : value.Substring(0, length);
	}
}
