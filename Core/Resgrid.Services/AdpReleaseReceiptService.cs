using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public sealed class AdpReleaseReceiptService(IAdpAccessStore store, IAdpAuditRepository audit) : IAdpReleaseReceiptService
	{
		public async Task<string> IssueAsync(int departmentId, long epoch, string actorId, string purpose,
			IReadOnlyList<ProtectedFieldOperationItem> fields, CancellationToken cancellationToken = default)
		{
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(actorId) || fields == null || fields.Count is < 1 or > 3 ||
				fields.Any(f => f == null || f.IsBinary || f.FieldId is not ("calls.name" or "calls.address" or "calls.natureofcall")) ||
				fields.Select(f => f.RowKey).Distinct().Count() != 1 || fields.Select(f => f.FieldId).Distinct().Count() != fields.Count ||
				purpose is not ("sms-pin" or "voice-pin" or "staff-support")) throw new ArgumentException("Invalid release.");
			var consent = purpose == "staff-support" ? await store.GetAsync(AdpSupportConsent.Key(departmentId), cancellationToken) : null;
			if (purpose == "staff-support" && (consent == null || !JsonConvert.DeserializeObject<AdpSupportConsent>(consent.Json).Enabled))
				throw new UnauthorizedAccessException("Department support access is disabled.");
			var token = "adpr." + Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
			var receipt = new Receipt { DepartmentId = departmentId, Epoch = epoch, ActorId = actorId, Purpose = purpose,
				ExpiresUtc = DateTime.UtcNow.AddMinutes(1), Binding = Bind(fields), ConsentVersion = consent?.Version ?? 0 };
			await audit.AppendAsync(new AdpAuditEvent { DepartmentId = departmentId, Layer = "application",
				Operation = purpose, Outcome = "authorized", ActorId = actorId, CorrelationId = Digest(token), PolicyEpoch = epoch }, cancellationToken);
			if (!await store.SaveAsync(Digest(token), JsonConvert.SerializeObject(receipt), 0, cancellationToken))
				throw new InvalidOperationException("Release collision.");
			return token;
		}

		public async Task<string> ConsumeAsync(string token, int departmentId, long epoch,
			IReadOnlyList<ProtectedFieldOperationItem> fields, CancellationToken cancellationToken = default)
		{
			if (token == null || !token.StartsWith("adpr.", StringComparison.Ordinal) || fields == null) return null;
			var state = await store.GetAsync(Digest(token), cancellationToken);
			var receipt = state == null ? null : JsonConvert.DeserializeObject<Receipt>(state.Json);
			if (receipt == null || receipt.Used || receipt.DepartmentId != departmentId || receipt.Epoch != epoch ||
				receipt.ExpiresUtc <= DateTime.UtcNow || receipt.Binding != Bind(fields)) return null;
			if (receipt.Purpose == "staff-support")
			{
				var consent = await store.GetAsync(AdpSupportConsent.Key(departmentId), cancellationToken);
				if (consent == null || consent.Version != receipt.ConsentVersion || !JsonConvert.DeserializeObject<AdpSupportConsent>(consent.Json).Enabled) return null;
			}
			receipt.Used = true;
			if (!await store.SaveAsync(state.StateId, JsonConvert.SerializeObject(receipt), state.Version, cancellationToken)) return null;
			await audit.AppendAsync(new AdpAuditEvent { DepartmentId = departmentId, Layer = "broker", Operation = receipt.Purpose,
				Outcome = "consumed", ActorId = receipt.ActorId, CorrelationId = Digest(token), PolicyEpoch = epoch }, cancellationToken);
			return receipt.ActorId;
		}

		// Bind the exact envelopes too; neither another row nor a newer value can be substituted.
		private static string Bind(IEnumerable<ProtectedFieldOperationItem> fields) => Digest(JsonConvert.SerializeObject(
			fields.Select(f => new object[] { f.FieldId, f.RowKey, f.IsBinary, f.Value }).ToArray()));
		private static string Digest(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
		private sealed class Receipt
		{
			public int DepartmentId { get; set; }
			public long Epoch { get; set; }
			public string ActorId { get; set; }
			public string Purpose { get; set; }
			public DateTime ExpiresUtc { get; set; }
			public string Binding { get; set; }
			public bool Used { get; set; }
			public long ConsentVersion { get; set; }
		}
	}
}
