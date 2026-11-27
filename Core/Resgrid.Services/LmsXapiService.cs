using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
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
	/// <summary>
	/// xAPI statements out (T5 item 3): LMS learning events (a course started, completed; an assessment or a standards lesson passed or failed)
	/// become statements in an outbox when the department has a deliverable LRS configuration (setting 147), and worker 98 posts them in
	/// batches with retry and exponential backoff. The actor is the department-scoped account (a pseudonymous name under the department's
	/// home page); the member's e-mail address is used only when the department opted in. The LRS secret is encrypted for the department.
	/// </summary>
	public class LmsXapiService : ILmsXapiService
	{
		private static readonly TimeSpan StaleClaim = TimeSpan.FromMinutes(10);
		private static readonly Random Jitter = new Random();

		private readonly ILmsXapiOutboxRepository _outbox;
		private readonly IDepartmentSettingsService _settings;
		private readonly IDepartmentsService _departments;
		private readonly IEncryptionService _encryption;
		private readonly IUserProfileService _profiles;
		private readonly ILmsCoursesRepository _courses;
		private readonly ILmsCourseVersionsRepository _versions;
		private readonly ILmsLessonsRepository _lessons;
		private readonly ILmsStandardsTransport _transport;
		private readonly IEventAggregator _eventAggregator;

		public LmsXapiService(ILmsXapiOutboxRepository outbox, IDepartmentSettingsService settings, IDepartmentsService departments, IEncryptionService encryption,
			IUserProfileService profiles, ILmsCoursesRepository courses, ILmsCourseVersionsRepository versions, ILmsLessonsRepository lessons, ILmsStandardsTransport transport,
			IEventAggregator eventAggregator)
		{
			_outbox = outbox;
			_settings = settings;
			_departments = departments;
			_encryption = encryption;
			_profiles = profiles;
			_courses = courses;
			_versions = versions;
			_lessons = lessons;
			_transport = transport;
			_eventAggregator = eventAggregator;
		}

		/// <summary>The clock (a seam for tests).</summary>
		public Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

		#region Settings

		public async Task<LmsStandardsSettings> GetSettingsAsync(int departmentId)
		{
			if (departmentId <= 0)
				return new LmsStandardsSettings();
			try
			{
				var setting = await _settings.GetSettingByTypeAsync(departmentId, DepartmentSettingTypes.LmsStandardsSettings);
				if (setting != null && !string.IsNullOrWhiteSpace(setting.Setting))
					return ObjectSerialization.Deserialize<LmsStandardsSettings>(setting.Setting) ?? new LmsStandardsSettings();
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "The LMS standards settings could not be read.");
			}
			return new LmsStandardsSettings();
		}

		public async Task<LmsResult> SaveSettingsAsync(LmsActor actor, LmsStandardsSettings settings, string newSecret, bool clearSecret, CancellationToken cancellationToken = default)
		{
			if (actor == null || !actor.MayManage)
				return LmsResult.Fail(LmsErrors.NotAuthorized);
			if (actor.Gate?.CanAuthor != true)
				return LmsResult.Fail(LmsErrors.AddonRequired);
			settings ??= new LmsStandardsSettings();
			var endpoint = settings.LrsEndpoint?.Trim();
			if (!string.IsNullOrEmpty(endpoint) && !LmsStandardsUrls.IsAcceptableHttpsUrl(endpoint))
				return LmsResult.Fail(LmsErrors.InvalidInput);
			if (settings.XapiEnabled && string.IsNullOrEmpty(endpoint))
				return LmsResult.Fail(LmsErrors.InvalidInput);
			if ((settings.LrsKey?.Length ?? 0) > 500 || (newSecret?.Length ?? 0) > 500)
				return LmsResult.Fail(LmsErrors.InvalidInput);

			var current = await GetSettingsAsync(actor.DepartmentId);
			var stored = new LmsStandardsSettings
			{
				XapiEnabled = settings.XapiEnabled,
				LrsEndpoint = string.IsNullOrEmpty(endpoint) ? null : endpoint,
				LrsKey = string.IsNullOrWhiteSpace(settings.LrsKey) ? null : settings.LrsKey.Trim(),
				LrsSecretEncrypted = current.LrsSecretEncrypted,
				ShareEmailInActor = settings.ShareEmailInActor
			};
			if (clearSecret)
				stored.LrsSecretEncrypted = null;
			if (!string.IsNullOrEmpty(newSecret))
			{
				var department = await _departments.GetDepartmentByIdAsync(actor.DepartmentId, false);
				if (department == null)
					return LmsResult.Fail(LmsErrors.NotFound);
				stored.LrsSecretEncrypted = _encryption.EncryptForDepartment(newSecret, actor.DepartmentId, department.Code);
			}

			await _settings.SaveOrUpdateSettingAsync(actor.DepartmentId, ObjectSerialization.Serialize(stored), DepartmentSettingTypes.LmsStandardsSettings, cancellationToken);
			_eventAggregator?.SendMessage<AuditEvent>(new AuditEvent
			{
				DepartmentId = actor.DepartmentId,
				UserId = actor.UserId,
				Type = AuditLogTypes.LmsStandardsSettingsChanged,
				// Never the secret, only whether one is set.
				After = JsonConvert.SerializeObject(new { stored.XapiEnabled, stored.LrsEndpoint, stored.LrsKey, HasSecret = stored.LrsSecretEncrypted != null, stored.ShareEmailInActor }),
				Successful = true,
				ServerName = Environment.MachineName
			});
			return new LmsResult { Value = stored };
		}

		public async Task<LmsXapiOverview> GetOverviewAsync(int departmentId)
		{
			var settings = await GetSettingsAsync(departmentId);
			var (pending, failed, delivered) = await _outbox.GetCountsAsync(departmentId);
			return new LmsXapiOverview
			{
				Settings = settings,
				HasSecret = !string.IsNullOrEmpty(settings.LrsSecretEncrypted),
				Pending = pending,
				Failed = failed,
				Delivered = delivered,
				RecentFailures = await _outbox.GetRecentFailedAsync(departmentId, 10) ?? new List<LmsXapiOutboxItem>()
			};
		}

		public async Task<LmsResult> RetryFailedAsync(LmsActor actor, CancellationToken cancellationToken = default)
		{
			if (actor == null || !actor.MayManage)
				return LmsResult.Fail(LmsErrors.NotAuthorized);
			return new LmsResult { Count = await _outbox.RequeueFailedAsync(actor.DepartmentId, Clock(), cancellationToken) };
		}

		#endregion Settings

		#region Queue

		public async Task EnqueueAsync(LmsXapiEvent learningEvent, CancellationToken cancellationToken = default)
		{
			try
			{
				if (learningEvent == null || learningEvent.DepartmentId <= 0 || string.IsNullOrWhiteSpace(learningEvent.UserId) || LmsXapiVerbs.Display(learningEvent.VerbId) == null)
					return;
				var settings = await GetSettingsAsync(learningEvent.DepartmentId);
				if (!settings.IsDeliverable)
					return;

				// Lesson-level events may arrive with the lesson only: the course (and its titles) come from the lesson's version.
				if (learningEvent.LmsLessonId.HasValue && (learningEvent.LmsCourseId <= 0 || learningEvent.LessonTitle == null))
				{
					var lesson = await _lessons.GetVisibleByIdAsync(learningEvent.DepartmentId, learningEvent.LmsLessonId.Value);
					var version = lesson == null ? null : await _versions.GetVisibleByIdAsync(learningEvent.DepartmentId, lesson.LmsCourseVersionId);
					if (version != null && learningEvent.LmsCourseId <= 0)
					{
						learningEvent.LmsCourseId = version.LmsCourseId;
						learningEvent.CourseTitle ??= version.Title;
					}
					learningEvent.LessonTitle ??= lesson?.Title;
				}
				if (learningEvent.LmsCourseId <= 0)
					return;
				var course = await _courses.GetVisibleByIdAsync(learningEvent.DepartmentId, learningEvent.LmsCourseId);
				// The Academy is not the department's content (and is free): only department courses are reported.
				if (course == null || course.IsSystem || course.DepartmentId != learningEvent.DepartmentId)
					return;
				learningEvent.CourseTitle ??= course.Title;

				string email = null;
				if (settings.ShareEmailInActor)
				{
					email = (await _profiles.GetProfileByUserIdAsync(learningEvent.UserId))?.MembershipEmail?.Trim();
					if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
						email = null;
				}

				var id = Guid.NewGuid().ToString();
				var statement = LmsXapiStatements.Build(learningEvent, id, email);
				var now = Clock();
				await _outbox.InsertAsync(new LmsXapiOutboxItem
				{
					DepartmentId = learningEvent.DepartmentId,
					StatementId = id,
					UserId = learningEvent.UserId,
					VerbId = learningEvent.VerbId,
					StatementJson = statement.ToString(Formatting.None),
					Status = (int)LmsXapiOutboxStatuses.Pending,
					NextAttemptOn = now,
					CreatedOn = now
				}, cancellationToken, true);
			}
			catch (Exception ex)
			{
				// Never fatal for the learner: the learning event itself is already recorded.
				Logging.LogException(ex, "An xAPI statement could not be queued.");
			}
		}

		#endregion Queue

		#region Worker 98

		public async Task<int> DeliverDueAsync(DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var delivered = 0;
			var batch = Math.Max(1, Config.LmsStandardsConfig.XapiBatchSize);
			// Due work first (R-L9): nothing per department unless it has statements waiting; at most a few rounds per run.
			for (var round = 0; round < 10; round++)
			{
				cancellationToken.ThrowIfCancellationRequested();
				var claimed = await _outbox.ClaimDueAsync(utcNow, utcNow - StaleClaim, batch * 4, cancellationToken) ?? new List<LmsXapiOutboxItem>();
				if (claimed.Count == 0)
					break;
				foreach (var department in claimed.GroupBy(c => c.DepartmentId))
					delivered += await DeliverDepartmentAsync(department.Key, department.OrderBy(i => i.CreatedOn).ToList(), batch, utcNow, cancellationToken);
				if (claimed.Count < batch * 4)
					break;
			}
			return delivered;
		}

		private async Task<int> DeliverDepartmentAsync(int departmentId, List<LmsXapiOutboxItem> items, int batch, DateTime utcNow, CancellationToken cancellationToken)
		{
			var settings = await GetSettingsAsync(departmentId);
			if (!settings.IsDeliverable)
			{
				// Turned off (or the endpoint removed) after these were queued: they are kept as failed, never sent elsewhere.
				foreach (var item in items)
					await FinishAsync(item, delivered: false, retry: false, "xapi_disabled", utcNow, cancellationToken);
				return 0;
			}

			var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["X-Experience-API-Version"] = "1.0.3" };
			if (!string.IsNullOrWhiteSpace(settings.LrsKey))
			{
				string secret = null;
				if (!string.IsNullOrEmpty(settings.LrsSecretEncrypted))
				{
					try
					{
						var department = await _departments.GetDepartmentByIdAsync(departmentId, false);
						secret = department == null ? null : _encryption.DecryptForDepartment(settings.LrsSecretEncrypted, departmentId, department.Code);
					}
					catch (Exception ex)
					{
						Logging.LogException(ex, $"The LRS secret of department {departmentId} could not be decrypted.");
					}
				}
				headers["Authorization"] = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(settings.LrsKey + ":" + (secret ?? string.Empty)));
			}

			var url = StatementsUrl(settings.LrsEndpoint);
			var delivered = 0;
			foreach (var chunk in items.Select((item, index) => (item, index)).GroupBy(x => x.index / batch, x => x.item))
			{
				var list = chunk.ToList();
				var body = "[" + string.Join(",", list.Select(i => i.StatementJson)) + "]";
				LmsHttpResult response;
				try
				{
					response = await _transport.PostJsonAsync(url, body, headers, cancellationToken);
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					response = new LmsHttpResult { StatusCode = 0, Error = ex.GetType().Name };
				}

				var (ok, retry) = LmsXapiStatements.Classify(response?.StatusCode ?? 0);
				var error = ok ? null : response?.Error ?? ("HTTP " + (response?.StatusCode ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture));
				foreach (var item in list)
				{
					await FinishAsync(item, ok, retry, error, utcNow, cancellationToken);
					if (ok)
						delivered++;
				}
			}
			return delivered;
		}

		/// <summary>The statements resource of an LRS endpoint ("…/xapi/" → "…/xapi/statements").</summary>
		public static string StatementsUrl(string endpoint)
		{
			var root = (endpoint ?? string.Empty).Trim();
			var query = string.Empty;
			var cut = root.IndexOf('?');
			if (cut >= 0)
			{
				query = root.Substring(cut);
				root = root.Substring(0, cut);
			}
			if (root.EndsWith("/statements", StringComparison.OrdinalIgnoreCase))
				return root + query;
			return root.TrimEnd('/') + "/statements" + query;
		}

		private async Task FinishAsync(LmsXapiOutboxItem item, bool delivered, bool retry, string error, DateTime utcNow, CancellationToken cancellationToken)
		{
			if (delivered)
			{
				item.Status = (int)LmsXapiOutboxStatuses.Delivered;
				item.DeliveredOn = utcNow;
				item.LastError = null;
			}
			else if (retry && item.Attempts < Math.Max(1, Config.LmsStandardsConfig.XapiMaxAttempts))
			{
				item.Status = (int)LmsXapiOutboxStatuses.Pending;
				item.NextAttemptOn = utcNow.Add(LmsXapiStatements.Backoff(item.Attempts, Jitter));
				item.LastError = Truncate(error);
			}
			else
			{
				item.Status = (int)LmsXapiOutboxStatuses.Failed;
				item.LastError = Truncate(error);
			}
			try
			{
				await _outbox.TryFinishAsync(item, cancellationToken);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"xAPI outbox item {item.LmsXapiOutboxItemId} could not be updated.");
			}
		}

		private static string Truncate(string value) => value == null ? null : value.Length <= 500 ? value : value.Substring(0, 500);

		public Task<int> PurgeDeliveredAsync(DateTime utcNow, CancellationToken cancellationToken = default) =>
			_outbox.PurgeDeliveredAsync(utcNow.AddDays(-Math.Max(1, Config.LmsStandardsConfig.XapiDeliveredRetentionDays)), cancellationToken);

		#endregion Worker 98
	}

	/// <summary>
	/// Outbound HTTPS for the LMS standards features: https only, the resolved address must be public (DNS-rebinding safe: the connection
	/// goes to the address that was checked), no redirects, a bounded timeout and response size. Used by worker 98 (LRS) and the LTI token
	/// endpoint (a tool's key set).
	/// </summary>
	public class LmsStandardsHttpTransport : ILmsStandardsTransport
	{
		private const int MaxResponseChars = 64 * 1024;

		private static readonly Lazy<HttpClient> Client = new Lazy<HttpClient>(() => new HttpClient(new SocketsHttpHandler
		{
			AllowAutoRedirect = false,
			PooledConnectionLifetime = TimeSpan.FromMinutes(5),
			ConnectCallback = ConnectPublicOnlyAsync
		})
		{
			Timeout = Timeout.InfiniteTimeSpan
		});

		private static async ValueTask<Stream> ConnectPublicOnlyAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
		{
			var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
			var target = addresses.FirstOrDefault(PublicNetworkAddress.IsPublic);
			if (target == null || addresses.Any(a => !PublicNetworkAddress.IsPublic(a)))
				throw new HttpRequestException("lms_destination_not_public");
			var socket = new Socket(target.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
			try
			{
				await socket.ConnectAsync(new IPEndPoint(target, context.DnsEndPoint.Port), cancellationToken);
				return new NetworkStream(socket, ownsSocket: true);
			}
			catch
			{
				socket.Dispose();
				throw;
			}
		}

		public Task<LmsHttpResult> PostJsonAsync(string url, string json, IDictionary<string, string> headers, CancellationToken cancellationToken = default) =>
			SendAsync(HttpMethod.Post, url, json, headers, cancellationToken);

		public Task<LmsHttpResult> GetAsync(string url, CancellationToken cancellationToken = default) => SendAsync(HttpMethod.Get, url, null, null, cancellationToken);

		private static async Task<LmsHttpResult> SendAsync(HttpMethod method, string url, string json, IDictionary<string, string> headers, CancellationToken cancellationToken)
		{
			if (!LmsStandardsUrls.IsAcceptableHttpsUrl(url))
				return new LmsHttpResult { Error = "lms_destination_refused" };
			using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, Config.LmsStandardsConfig.HttpTimeoutSeconds)));
			try
			{
				using var request = new HttpRequestMessage(method, url);
				if (json != null)
					request.Content = new StringContent(json, Encoding.UTF8, "application/json");
				foreach (var header in headers ?? new Dictionary<string, string>())
				{
					if (string.Equals(header.Key, "Authorization", StringComparison.OrdinalIgnoreCase))
					{
						var space = header.Value.IndexOf(' ');
						request.Headers.Authorization = space > 0 ? new AuthenticationHeaderValue(header.Value.Substring(0, space), header.Value.Substring(space + 1)) : null;
					}
					else
						request.Headers.TryAddWithoutValidation(header.Key, header.Value);
				}
				using var response = await Client.Value.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
				var body = new StringBuilder();
				using (var stream = await response.Content.ReadAsStreamAsync(timeout.Token))
				using (var reader = new StreamReader(stream, Encoding.UTF8))
				{
					var buffer = new char[4096];
					int read;
					while (body.Length < MaxResponseChars && (read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), timeout.Token)) > 0)
						body.Append(buffer, 0, Math.Min(read, MaxResponseChars - body.Length));
				}
				return new LmsHttpResult { StatusCode = (int)response.StatusCode, Body = body.ToString() };
			}
			catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
			{
				return new LmsHttpResult { Error = "timeout" };
			}
			catch (HttpRequestException ex)
			{
				return new LmsHttpResult { Error = ex.Message != null && ex.Message.StartsWith("lms_", StringComparison.Ordinal) ? ex.Message : "network_error" };
			}
		}
	}
}
