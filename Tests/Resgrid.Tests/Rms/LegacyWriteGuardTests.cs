using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Rms
{
	/// <summary>
	/// Legacy write-denial at the service boundary (RMS plan section 4.1): after activation
	/// WorkLogsService refuses to create, delete or attach to a Log whoever the caller is, and every
	/// path is denied before any repository write happens. A hidden button is not an acceptance result.
	/// </summary>
	[TestFixture]
	public class LegacyWriteGuardTests
	{
		private const int Dept = 3;
		private Mock<IRecordsCutoverService> _cutover;
		private Mock<ILogsRepository> _logs;
		private Mock<ILogUsersRepository> _logUsers;
		private Mock<ILogUnitsRepository> _logUnits;
		private Mock<ILogAttachmentRepository> _attachments;
		private Mock<IDepartmentsService> _departments;
		private WorkLogsService _service;

		[SetUp]
		public void SetUp()
		{
			_cutover = new Mock<IRecordsCutoverService>();
			_logs = new Mock<ILogsRepository>();
			_logUsers = new Mock<ILogUsersRepository>();
			_logUnits = new Mock<ILogUnitsRepository>();
			_attachments = new Mock<ILogAttachmentRepository>();
			_departments = new Mock<IDepartmentsService>();
			var protectedWrite = new Mock<IProtectedWriteService>();

			_logUsers.Setup(r => r.GetLogsByLogIdAsync(It.IsAny<int>())).ReturnsAsync(new List<LogUser>());
			_logUnits.Setup(r => r.GetLogsByLogIdAsync(It.IsAny<int>())).ReturnsAsync(new List<LogUnit>());
			_departments.Setup(d => d.GetDepartmentByIdAsync(Dept, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = Dept });

			_service = new WorkLogsService(_logs.Object, new Mock<ICallLogsRepository>().Object, _logUsers.Object, _attachments.Object,
				_logUnits.Object, _departments.Object, new Mock<IDepartmentGroupsService>().Object,
				new Mock<ICallsService>().Object, new Lazy<IProtectedWriteService>(() => protectedWrite.Object), new Lazy<IRecordsCutoverService>(() => _cutover.Object));
		}

		private void Blocked()
		{
			_cutover.Setup(c => c.EnsureLegacyWriteAllowedAsync(Dept, It.IsAny<string>(), It.IsAny<string>()))
				.ThrowsAsync(new RecordsLegacyWriteBlockedException(Dept, "test"));
		}

		[Test]
		public async Task Save_log_is_denied_before_any_repository_write_once_records_is_active()
		{
			Blocked();
			var log = new Log { DepartmentId = Dept, Narrative = "x", LoggedByUserId = "u1", Units = new List<LogUnit>(), Users = new List<LogUser>() };

			await _service.Invoking(s => s.SaveLogAsync(log)).Should().ThrowAsync<RecordsLegacyWriteBlockedException>();

			_logs.Verify(r => r.SaveOrUpdateAsync(It.IsAny<Log>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
			_cutover.Verify(c => c.EnsureLegacyWriteAllowedAsync(Dept, "WorkLogsService.SaveLogAsync", "u1"), Times.Once);
		}

		[Test]
		public async Task Delete_log_is_denied_for_the_owning_department()
		{
			Blocked();
			_logs.Setup(r => r.GetByIdAsync(15)).ReturnsAsync(new Log { LogId = 15, DepartmentId = Dept });

			await _service.Invoking(s => s.DeleteLogAsync(15)).Should().ThrowAsync<RecordsLegacyWriteBlockedException>();

			_logs.Verify(r => r.DeleteAsync(It.IsAny<Log>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Attachment_save_is_denied_through_the_owning_log()
		{
			Blocked();
			_logs.Setup(r => r.GetByIdAsync(21)).ReturnsAsync(new Log { LogId = 21, DepartmentId = Dept });

			await _service.Invoking(s => s.SaveLogAttachmentAsync(new LogAttachment { LogId = 21, UserId = "u1" })).Should().ThrowAsync<RecordsLegacyWriteBlockedException>();

			_attachments.Verify(r => r.SaveOrUpdateAsync(It.IsAny<LogAttachment>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public async Task Writes_proceed_normally_when_records_is_not_active()
		{
			_cutover.Setup(c => c.EnsureLegacyWriteAllowedAsync(Dept, It.IsAny<string>(), It.IsAny<string>())).Returns(Task.CompletedTask);
			_attachments.Setup(r => r.SaveOrUpdateAsync(It.IsAny<LogAttachment>(), It.IsAny<CancellationToken>(), It.IsAny<bool>())).ReturnsAsync((LogAttachment a, CancellationToken c, bool f) => a);
			_logs.Setup(r => r.GetByIdAsync(21)).ReturnsAsync(new Log { LogId = 21, DepartmentId = Dept });

			var saved = await _service.SaveLogAttachmentAsync(new LogAttachment { LogId = 21, UserId = "u1" });

			saved.Should().NotBeNull();
			_attachments.Verify(r => r.SaveOrUpdateAsync(It.IsAny<LogAttachment>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
		}
	}
}
