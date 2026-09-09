using System;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Model;
using Resgrid.Model.Inventories;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public sealed class InventoryScheduledReportService : IInventoryScheduledReportService
	{
		private readonly IScheduledTasksService _tasks;
		private readonly IInventoryAuthorizationService _authorization;
		private readonly IInventoryMigrationService _migration;
		private readonly IInventoryOperationsService _reports;
		private readonly IDepartmentDataProtectionService _protection;
		private readonly IUsersService _users;
		private readonly IUserProfileService _profiles;
		private readonly IPdfProvider _pdf;
		// A weak, request-object-bound proof retains only comparison metadata, never report rows, HTML or PDF bytes.
		private readonly ConditionalWeakTable<ScheduledTask, DeliveryProof> _proofs = new();
		private readonly object _proofGate = new();

		public InventoryScheduledReportService(IScheduledTasksService tasks, IInventoryAuthorizationService authorization,
			IInventoryMigrationService migration, IInventoryOperationsService reports, IDepartmentDataProtectionService protection,
			IUsersService users, IUserProfileService profiles, IPdfProvider pdf)
		{ _tasks = tasks; _authorization = authorization; _migration = migration; _reports = reports; _protection = protection; _users = users; _profiles = profiles; _pdf = pdf; }

		private sealed class DeliveryProof
		{
			public int TaskId;
			public int DepartmentId;
			public string UserId;
			public string Data;
			public string ScheduleHash;
			public string Email;
			public string CultureName;
			public bool Protected;
			public string ProjectionHash;
			public InventoryReportKind Kind;
		}

		public async Task<EmailNotification> BuildAsync(ScheduledTask queuedTask)
		{
			var proof = await ReadCurrentAsync(queuedTask);
			var culture = CultureInfo.GetCultureInfo(proof.CultureName);
			string html;
			if (proof.Protected) html = InventoryReportDocuments.Locked(proof.Kind, culture);
			else
			{
				var report = await ReportAsync(proof);
				proof.ProjectionHash = ProjectionHash(report);
				html = InventoryReportDocuments.Build(report, culture);
			}
			var bytes = _pdf.ConvertHtmlToPdf(html);
			if (bytes == null || bytes.Length < 5 || Encoding.ASCII.GetString(bytes, 0, 5) != "%PDF-") throw new InventoryException(409, "ReportGenerationFailed");
			await RevalidateAsync(queuedTask, proof);
			lock (_proofGate)
			{
				if (_proofs.TryGetValue(queuedTask, out _)) throw new InventoryException(409, "ReportDeliveryAlreadyBuilt");
				_proofs.Add(queuedTask, proof);
			}
			return new EmailNotification { To = proof.Email, Subject = InventoryReportDocuments.Title(proof.Kind, culture),
				AttachmentName = "inventory-" + proof.Kind.ToString().ToLowerInvariant() + "-" + DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".pdf", AttachmentData = bytes };
		}

		public async Task ValidateAsync(ScheduledTask queuedTask)
		{
			if (queuedTask == null || !_proofs.TryGetValue(queuedTask, out var proof)) throw new InventoryException(409, "ReportDeliveryNotBuilt");
			await RevalidateAsync(queuedTask, proof);
			lock (_proofGate) _proofs.Remove(queuedTask);
		}

		private async Task<DeliveryProof> ReadCurrentAsync(ScheduledTask queuedTask)
		{
			if (queuedTask == null || queuedTask.ScheduledTaskId <= 0) throw new InventoryException(403, "ReportDeliveryUnavailable");
			var task = await _tasks.GetScheduledTaskByIdAsync(queuedTask.ScheduledTaskId);
			if (task == null || !task.Active || task.ScheduledTaskId != queuedTask.ScheduledTaskId || task.DepartmentId != queuedTask.DepartmentId
				|| task.UserId != queuedTask.UserId || task.Data != queuedTask.Data || task.TaskType != (int)TaskTypes.ReportDelivery
				|| queuedTask.TaskType != (int)TaskTypes.ReportDelivery || !int.TryParse(task.Data, NumberStyles.None, CultureInfo.InvariantCulture, out var type)
				|| type < (int)ReportTypes.InventoryOnHand || type > (int)ReportTypes.ControlledSubstanceLog || task.Data != type.ToString(CultureInfo.InvariantCulture))
				throw new InventoryException(403, "ReportDeliveryUnavailable");
			var scheduleHash = ScheduleHash(task);
			if (scheduleHash != ScheduleHash(queuedTask)) throw new InventoryException(409, "ReportDeliveryChanged");
			var actor = new InventoryActor { DepartmentId = task.DepartmentId, UserId = task.UserId };
			await _authorization.RequireAsync(actor, false, PermissionTypes.AdjustInventory);
			if (type == (int)ReportTypes.ControlledSubstanceLog) await _authorization.RequireAsync(actor, false, PermissionTypes.ManageControlledSubstances);
			if (!await _authorization.IsEnabledAsync(task.DepartmentId) || !await _migration.IsMigratedAsync(task.DepartmentId)) throw new InventoryException(409, "InventoryDisabled");
			var user = _users.GetUserById(task.UserId, true);
			if (string.IsNullOrWhiteSpace(user?.Email)) throw new InventoryException(403, "ReportDeliveryUnavailable");
			var language = (await _profiles.GetProfileByUserIdAsync(task.UserId, true))?.Language;
			var culture = Resgrid.Localization.SupportedLocales.GetSupportedCultures().Contains(language) ? language : "en";
			return new DeliveryProof { TaskId = task.ScheduledTaskId, DepartmentId = task.DepartmentId, UserId = task.UserId, Data = task.Data, ScheduleHash = scheduleHash,
				Email = user.Email, CultureName = culture, Protected = await _protection.IsProtectionEnforcedAsync(task.DepartmentId),
				Kind = (InventoryReportKind)(type - (int)ReportTypes.InventoryOnHand) };
		}

		private Task<InventoryReport> ReportAsync(DeliveryProof proof) => _reports.BuildReportAsync(
			new InventoryActor { DepartmentId = proof.DepartmentId, UserId = proof.UserId }, new InventoryReportInput { Kind = proof.Kind });

		private static string ScheduleHash(ScheduledTask task)
		{
			// Compare the timing selected by the worker, including changes made while a report is rendering.
			// Database timestamps can lose DateTimeKind; the scheduler compares their stored wall-clock values.
			var json = JsonConvert.SerializeObject(new { task.ScheduleType, SpecificDateTicks = task.SpecifcDate?.Ticks, task.Time,
				task.Sunday, task.Monday, task.Tuesday, task.Wednesday, task.Thursday, task.Friday, task.Saturday });
			return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
		}

		private async Task RevalidateAsync(ScheduledTask queuedTask, DeliveryProof proof)
		{
			var current = await ReadCurrentAsync(queuedTask);
			if (proof.TaskId != current.TaskId || proof.DepartmentId != current.DepartmentId || proof.UserId != current.UserId || proof.Data != current.Data
				|| proof.ScheduleHash != current.ScheduleHash || proof.Email != current.Email || proof.CultureName != current.CultureName || proof.Protected != current.Protected)
				throw new InventoryException(409, "ReportDeliveryChanged");
			if (!current.Protected && ProjectionHash(await ReportAsync(current)) != proof.ProjectionHash) throw new InventoryException(409, "ReportDeliveryChanged");
			// Report generation can itself take time; check policy, membership, recipient and schedule once more afterwards.
			current = await ReadCurrentAsync(queuedTask);
			if (proof.TaskId != current.TaskId || proof.DepartmentId != current.DepartmentId || proof.UserId != current.UserId || proof.Data != current.Data
				|| proof.ScheduleHash != current.ScheduleHash || proof.Email != current.Email || proof.CultureName != current.CultureName || proof.Protected != current.Protected)
				throw new InventoryException(409, "ReportDeliveryChanged");
		}

		private static string ProjectionHash(InventoryReport report)
		{
			// Column order is significant; row and dictionary enumeration order are not. GeneratedOn is deliberately excluded.
			var rows = report.Rows.Select(row => JsonConvert.SerializeObject(row.OrderBy(cell => cell.Key, StringComparer.Ordinal).ToDictionary(cell => cell.Key, cell => cell.Value)))
				.OrderBy(row => row, StringComparer.Ordinal).ToArray();
			var json = JsonConvert.SerializeObject(new { report.Kind, report.FromUtc, report.UntilUtc, report.Columns, Rows = rows,
				Totals = report.Totals.OrderBy(total => total.CurrencyCode, StringComparer.Ordinal).Select(total => new { total.CurrencyCode, total.KnownValue, total.UncostedRows }) });
			return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
		}
	}
}
