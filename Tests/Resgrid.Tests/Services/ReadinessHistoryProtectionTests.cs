using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Checklists;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public class ReadinessHistoryProtectionTests
	{
		private Mock<IDepartmentDataProtectionService> _policy;
		private ReadinessHistoryTestProtection _protection;
		[SetUp] public void SetUp()
		{
			_policy = new Mock<IDepartmentDataProtectionService>();
			_policy.Setup(s => s.IsProtectionEnforcedAsync(42)).ReturnsAsync(true);
			_protection = new ReadinessHistoryTestProtection(_policy);
		}
		[TearDown] public void TearDown() => _protection.Dispose();

		[Test]
		public async Task All_history_fields_encrypt_losslessly_and_display_without_disclosure_or_mutation()
		{
			async Task Check<T>(T row, string key, IReadOnlyDictionary<string, (Func<T, string> Get, Action<T, string> Set)> fields) where T : class
			{
				foreach (var field in fields) field.Value.Set(row, "SYNTHETIC-PHI-CANARY:" + field.Key);
				await _protection.Service.ProtectAsync(42, key, row, fields);
				var first = fields.ToDictionary(f => f.Key, f => f.Value.Get(row));
				await _protection.Service.ProtectAsync(42, key, row, fields);
				var display = await _protection.Service.ForDisplayAsync(42, row, fields);
				foreach (var field in fields)
				{
					var stored = field.Value.Get(row);
					stored.Should().StartWith("rgdp:").And.Be(first[field.Key]).And.NotContain("CANARY");
					_protection.Decrypt(42, field.Key, key, stored).Should().Be("SYNTHETIC-PHI-CANARY:" + field.Key);
					field.Value.Get(display).Should().Be(ProtectedDataEnvelope.RedactionValue);
				}
			}
			await Check(new AuditLog(), "19", ReadinessHistoryFields.Audits);
			await Check(new DomainEventOutboxEntry(), "1024", ReadinessHistoryFields.Outbox);
			await Check(new WorkflowRun(), Guid.NewGuid().ToString(), ReadinessHistoryFields.Runs);
			await Check(new WorkflowRunLog(), Guid.NewGuid().ToString(), ReadinessHistoryFields.Logs);
			_protection.Broker.Invocations.Should().OnlyContain(i => i.Method.Name == "EncryptAsync");
		}

		[Test]
		public async Task Older_catalog_and_redacted_views_cannot_overwrite_history()
		{
			_policy.Setup(s => s.GetPolicyByDepartmentIdAsync(42, It.IsAny<bool>())).ReturnsAsync(new DepartmentDataProtectionPolicy { DepartmentId = 42, CatalogVersion = 15 });
			var row = new AuditLog { Data = "SYNTHETIC-PHI-CANARY" };
			Func<Task> write = () => _protection.Service.ProtectAsync(42, "19", row, ReadinessHistoryFields.Audits);
			await write.Should().ThrowAsync<InvalidOperationException>(); row.Data.Should().Be("SYNTHETIC-PHI-CANARY");
			row.Data = ProtectedDataEnvelope.RedactionValue;
			await write.Should().ThrowAsync<InvalidOperationException>(); row.Data.Should().Be(ProtectedDataEnvelope.RedactionValue);
		}

		[Test]
		public async Task Audit_view_redacts_legacy_checklist_data_on_unknown_policy_and_leaves_other_audits_unchanged()
		{
			var checklist = new AuditLog { DepartmentId = 42, AuditLogId = 19, LogType = ReadinessHistoryFields.AuditTypes.First(), Data = "SYNTHETIC-PHI-CANARY" };
			var other = new AuditLog { DepartmentId = 42, AuditLogId = 20, LogType = (int)AuditLogTypes.GroupAdded, Data = "Legacy group audit" };
			var repository = new Mock<IAuditLogsRepository>();
			repository.Setup(s => s.GetAllByDepartmentIdAsync(42)).ReturnsAsync(new[] { checklist, other });
			_policy.Setup(s => s.IsProtectionEnforcedAsync(42)).ThrowsAsync(new InvalidOperationException());
			var service = new AuditService(repository.Object, Mock.Of<IUserProfileService>(), _protection.Lazy);
			var logs = await service.GetAllAuditLogsForDepartmentAsync(42);
			logs[0].Data.Should().Be(ProtectedDataEnvelope.RedactionValue); checklist.Data.Should().Be("SYNTHETIC-PHI-CANARY");
			logs[1].Should().BeSameAs(other);
		}

		[Test]
		public async Task Read_boundary_remains_redacted_until_offboarding_restores_plaintext()
		{
			var row = new WorkflowRun { InputPayload = "SYNTHETIC-PHI-CANARY" };
			var key = Guid.NewGuid().ToString();
			await _protection.Service.ProtectAsync(42, key, row, ReadinessHistoryFields.Runs);
			_policy.Setup(s => s.IsProtectionEnforcedAsync(42)).ReturnsAsync(false);
			(await _protection.Service.ForDisplayAsync(42, row, ReadinessHistoryFields.Runs)).InputPayload.Should().Be(ProtectedDataEnvelope.RedactionValue);
			row.InputPayload = _protection.Decrypt(42, "workflowruns.inputpayload", key, row.InputPayload);
			(await _protection.Service.ForDisplayAsync(42, row, ReadinessHistoryFields.Runs)).InputPayload.Should().Be("SYNTHETIC-PHI-CANARY");
		}
	}
}
