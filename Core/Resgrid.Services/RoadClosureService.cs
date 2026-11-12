using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>Road closures (M0314); see <see cref="IRoadClosureService"/>.</summary>
	public class RoadClosureService : IRoadClosureService
	{
		private const string CacheKey = "RoadClosures1_{0}";
		private static readonly TimeSpan CacheLength = TimeSpan.FromDays(1);

		// Parsed shapes by closure version, shared across requests: a board asks for the same closures dozens of times.
		private const int MaxCachedShapes = 2000;
		private static readonly ConcurrentDictionary<string, RoadClosureShape> Shapes = new ConcurrentDictionary<string, RoadClosureShape>(StringComparer.Ordinal);

		private readonly IRoadClosuresRepository _roadClosuresRepository;
		private readonly ICacheProvider _cacheProvider;

		// One read per department per request scope: the board looks drive times up in parallel.
		private readonly ConcurrentDictionary<int, Lazy<Task<List<RoadClosure>>>> _scopeClosures = new ConcurrentDictionary<int, Lazy<Task<List<RoadClosure>>>>();

		public RoadClosureService(IRoadClosuresRepository roadClosuresRepository, ICacheProvider cacheProvider)
		{
			_roadClosuresRepository = roadClosuresRepository;
			_cacheProvider = cacheProvider;
		}

		public async Task<List<RoadClosure>> GetClosuresForDepartmentAsync(int departmentId, bool bypassCache = false)
		{
			if (departmentId <= 0)
				return new List<RoadClosure>();

			async Task<List<RoadClosure>> read() => (await _roadClosuresRepository.GetForDepartmentAsync(departmentId) ?? new List<RoadClosure>())
				.OrderByDescending(c => c.CreatedOn)
				.ToList();

			if (bypassCache || !Config.SystemBehaviorConfig.CacheEnabled || _cacheProvider == null)
				return await read();

			var key = string.Format(CacheKey, departmentId);
			try
			{
				var cached = await _cacheProvider.GetStringAsync(key);
				if (!string.IsNullOrWhiteSpace(cached))
				{
					var parsed = JsonConvert.DeserializeObject<List<RoadClosure>>(cached);
					if (parsed != null)
						return parsed;
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Reading cached road closures for department {departmentId} failed.");
			}

			var closures = await read();

			try
			{
				await _cacheProvider.SetStringAsync(key, JsonConvert.SerializeObject(closures), CacheLength);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Caching road closures for department {departmentId} failed.");
			}

			return closures;
		}

		public async Task<RoadClosure> GetClosureByIdAsync(int departmentId, string roadClosureId)
		{
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(roadClosureId))
				return null;

			return await _roadClosuresRepository.GetByIdForDepartmentAsync(roadClosureId, departmentId);
		}

		public async Task<List<RoadClosure>> GetActiveClosuresAsync(int departmentId, DateTime utc)
		{
			return (await GetClosuresForDepartmentAsync(departmentId))
				.Where(c => c.StateAt(utc) == RoadClosureStates.Active)
				.ToList();
		}

		public async Task<RoadClosure> SaveClosureAsync(RoadClosure closure, string userId, CancellationToken cancellationToken = default)
		{
			if (closure == null)
				throw new ArgumentNullException(nameof(closure));

			Validate(closure);

			var now = DateTime.UtcNow;
			var isNew = string.IsNullOrWhiteSpace(closure.RoadClosureId);

			closure.Name = closure.Name.Trim();
			closure.GeometryJson = RoadClosureShape.Normalize(closure.GeometryJson);
			closure.Windows = closure.Windows;
			closure.Reason = Clean(closure.Reason);
			closure.ClosedBy = Clean(closure.ClosedBy);
			closure.Authority = Clean(closure.Authority);
			closure.ContactName = Clean(closure.ContactName);
			closure.ContactDetails = Clean(closure.ContactDetails);
			closure.Notes = Clean(closure.Notes);
			closure.SpeedPercent = Math.Max(RoadClosure.MinimumSpeedPercent, Math.Min(RoadClosure.MaximumSpeedPercent, closure.SpeedPercent));
			closure.DetourMinutes = Math.Max(0, Math.Min(RoadClosure.MaximumDetourMinutes, closure.DetourMinutes));
			closure.AppliesTo = (int)closure.AppliesToModes;
			closure.UpdatedByUserId = userId;
			closure.UpdatedOn = now;

			RoadClosure saved;
			if (isNew)
			{
				closure.RoadClosureId = Guid.NewGuid().ToString();
				closure.CreatedByUserId = userId;
				closure.CreatedOn = now;
				saved = await _roadClosuresRepository.InsertAsync(closure, CancellationToken.None);
			}
			else
			{
				var existing = await _roadClosuresRepository.GetByIdForDepartmentAsync(closure.RoadClosureId, closure.DepartmentId);
				if (existing == null)
					throw new InvalidOperationException("The road closure does not belong to the department.");

				closure.CreatedByUserId = existing.CreatedByUserId;
				closure.CreatedOn = existing.CreatedOn;
				saved = await _roadClosuresRepository.UpdateAsync(closure, CancellationToken.None);
			}

			await InvalidateAsync(closure.DepartmentId);
			return saved ?? closure;
		}

		public async Task<bool> SetClosureEnabledAsync(int departmentId, string roadClosureId, bool enabled, string userId, CancellationToken cancellationToken = default)
		{
			var closure = await GetClosureByIdAsync(departmentId, roadClosureId);
			if (closure == null)
				return false;

			closure.IsEnabled = enabled;
			closure.UpdatedByUserId = userId;
			closure.UpdatedOn = DateTime.UtcNow;
			await _roadClosuresRepository.UpdateAsync(closure, CancellationToken.None);
			await InvalidateAsync(departmentId);
			return true;
		}

		public async Task<bool> DeleteClosureAsync(int departmentId, string roadClosureId, CancellationToken cancellationToken = default)
		{
			var closure = await GetClosureByIdAsync(departmentId, roadClosureId);
			if (closure == null)
				return false;

			await _roadClosuresRepository.DeleteAsync(closure, CancellationToken.None);
			await InvalidateAsync(departmentId);
			return true;
		}

		public async Task<List<RoutingClosure>> GetRoutingClosuresAsync(int departmentId, UnitTravelModes mode, DateTime utc)
		{
			var routing = new List<RoutingClosure>();
			if (departmentId <= 0 || mode == UnitTravelModes.Air)
				return routing;

			var closures = await _scopeClosures.GetOrAdd(departmentId, id => new Lazy<Task<List<RoadClosure>>>(() => GetClosuresForDepartmentAsync(id))).Value;

			foreach (var closure in closures ?? new List<RoadClosure>())
			{
				if (!closure.AppliesAt(utc, mode))
					continue;

				var shape = ShapeOf(closure);
				var routingClosure = RoutingClosure.From(closure, shape);
				if (routingClosure != null)
					routing.Add(routingClosure);
			}

			return routing;
		}

		/// <summary>Throws <see cref="RoadClosureValidationException"/> for the first problem found.</summary>
		public static void Validate(RoadClosure closure)
		{
			if (string.IsNullOrWhiteSpace(closure.Name))
				throw new RoadClosureValidationException(RoadClosureValidationErrors.NameRequired, nameof(RoadClosure.Name), "A road closure needs a name.");

			if (closure.Name.Trim().Length > RoadClosure.MaxNameLength)
				throw new RoadClosureValidationException(RoadClosureValidationErrors.NameTooLong, nameof(RoadClosure.Name), "The name is too long.");

			if (!Enum.IsDefined(typeof(RoadClosureTypes), closure.ClosureType))
				throw new RoadClosureValidationException(RoadClosureValidationErrors.InvalidType, nameof(RoadClosure.ClosureType), "Unknown closure type.");

			if (!Enum.IsDefined(typeof(RoadClosureSeverities), closure.Severity))
				throw new RoadClosureValidationException(RoadClosureValidationErrors.InvalidType, nameof(RoadClosure.Severity), "Unknown closure severity.");

			if (string.IsNullOrWhiteSpace(closure.GeometryJson))
				throw new RoadClosureValidationException(RoadClosureValidationErrors.ShapeRequired, nameof(RoadClosure.GeometryJson), "Draw the closed road or area on the map.");

			if (!RoadClosureShape.TryParse(closure.GeometryJson, out var shape, out var shapeError))
			{
				var tooLarge = shapeError != null && shapeError.Contains("more than");
				throw new RoadClosureValidationException(tooLarge ? RoadClosureValidationErrors.ShapeTooLarge : RoadClosureValidationErrors.ShapeInvalid,
					nameof(RoadClosure.GeometryJson), shapeError ?? "The drawn shape is not valid.");
			}

			if (closure.AppliesToModes == RoadClosureTravelModes.None)
				throw new RoadClosureValidationException(RoadClosureValidationErrors.TravelModeRequired, nameof(RoadClosure.AppliesTo), "Choose at least one kind of traffic the closure stops.");

			if (!string.IsNullOrWhiteSpace(closure.WindowsJson))
			{
				List<RoadClosureWindow> raw;
				try
				{
					raw = JsonConvert.DeserializeObject<List<RoadClosureWindow>>(closure.WindowsJson) ?? new List<RoadClosureWindow>();
				}
				catch (JsonException)
				{
					raw = new List<RoadClosureWindow>();
				}

				if (raw.Count > RoadClosureWindows.MaxWindows)
					throw new RoadClosureValidationException(RoadClosureValidationErrors.TooManyWindows, nameof(RoadClosure.WindowsJson), "Too many closure times.");

				if (raw.Any(w => w != null && w.EndsOn.HasValue && w.EndsOn.Value <= w.StartsOn))
					throw new RoadClosureValidationException(RoadClosureValidationErrors.WindowEndsBeforeStart, nameof(RoadClosure.WindowsJson), "A closure time ends before it starts.");
			}

			CheckLength(closure.Reason, RoadClosure.MaxReasonLength, nameof(RoadClosure.Reason));
			CheckLength(closure.ClosedBy, RoadClosure.MaxTextLength, nameof(RoadClosure.ClosedBy));
			CheckLength(closure.Authority, RoadClosure.MaxTextLength, nameof(RoadClosure.Authority));
			CheckLength(closure.ContactName, RoadClosure.MaxTextLength, nameof(RoadClosure.ContactName));
			CheckLength(closure.ContactDetails, RoadClosure.MaxContactDetailsLength, nameof(RoadClosure.ContactDetails));
			CheckLength(closure.Notes, RoadClosure.MaxNotesLength, nameof(RoadClosure.Notes));
		}

		private static void CheckLength(string value, int max, string field)
		{
			if (value != null && value.Trim().Length > max)
				throw new RoadClosureValidationException(RoadClosureValidationErrors.TextTooLong, field, $"{field} is too long.");
		}

		private static string Clean(string value)
		{
			return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
		}

		private static RoadClosureShape ShapeOf(RoadClosure closure)
		{
			var key = closure.RoadClosureId + "@" + closure.UpdatedOn.Ticks;
			if (Shapes.TryGetValue(key, out var shape))
				return shape;

			shape = RoadClosureShape.ParseOrNull(closure.GeometryJson);
			if (shape == null)
				return null;

			if (Shapes.Count >= MaxCachedShapes)
				Shapes.Clear();

			Shapes[key] = shape;
			return shape;
		}

		private async Task InvalidateAsync(int departmentId)
		{
			_scopeClosures.TryRemove(departmentId, out _);

			if (_cacheProvider == null)
				return;

			try
			{
				await _cacheProvider.RemoveAsync(string.Format(CacheKey, departmentId));
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Clearing cached road closures for department {departmentId} failed.");
			}
		}
	}
}
