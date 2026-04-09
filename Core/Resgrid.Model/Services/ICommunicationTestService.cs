using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	public interface ICommunicationTestService
	{
		Task<IEnumerable<CommunicationTest>> GetTestsByDepartmentIdAsync(int departmentId);
		Task<CommunicationTest> GetTestByIdAsync(Guid communicationTestId);
		Task<bool> CanCreateScheduledTestAsync(int departmentId, int scheduleType, Guid? excludeTestId = null);
		Task<CommunicationTest> SaveTestAsync(CommunicationTest test, CancellationToken cancellationToken = default);
		Task<bool> DeleteTestAsync(Guid communicationTestId, CancellationToken cancellationToken = default);

		Task<bool> CanStartOnDemandRunAsync(Guid communicationTestId);
		Task<CommunicationTestRun> StartTestRunAsync(Guid communicationTestId, int departmentId, string initiatedByUserId, CancellationToken cancellationToken = default);
		Task<IEnumerable<CommunicationTestRun>> GetRunsByTestIdAsync(Guid communicationTestId);
		Task<CommunicationTestRun> GetRunByIdAsync(Guid communicationTestRunId);
		Task<IEnumerable<CommunicationTestRun>> GetRunsByDepartmentIdAsync(int departmentId);

		Task<IEnumerable<CommunicationTestResult>> GetResultsByRunIdAsync(Guid communicationTestRunId);

		Task<bool> RecordSmsResponseAsync(string runCode, string fromPhoneNumber);
		Task<bool> RecordEmailResponseAsync(string responseToken);
		Task<bool> RecordVoiceResponseAsync(string responseToken);
		Task<bool> RecordPushResponseAsync(string responseToken);

		Task ProcessScheduledTestsAsync(CancellationToken cancellationToken = default);
		Task CompleteExpiredRunsAsync(CancellationToken cancellationToken = default);
	}
}
