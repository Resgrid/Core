using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Resgrid.Web.Services.Models.v4.Workflows;

// ── Result wrappers ────────────────────────────────────────────────────────────

public class GetWorkflowsResult { public List<WorkflowSummaryData> Data { get; set; } = new(); }
public class WorkflowDetailResult { public WorkflowDetailData Workflow { get; set; } }
public class WorkflowStepResult { public WorkflowStepData Step { get; set; } }
public class GetCredentialsResult { public List<CredentialSummaryData> Data { get; set; } = new(); }
public class SaveCredentialResult { public string WorkflowCredentialId { get; set; } }
public class GetWorkflowRunsResult { public List<WorkflowRunData> Data { get; set; } = new(); }
public class GetWorkflowRunLogsResult { public List<WorkflowRunLogData> Data { get; set; } = new(); }
public class WorkflowHealthResult { public WorkflowHealthData Health { get; set; } }
public class DeleteWorkflowResult { public bool Success { get; set; } }
public class GetTemplateVariablesResult { public List<TemplateVariableData> Data { get; set; } = new(); }

// ── Data objects ──────────────────────────────────────────────────────────────

public class WorkflowSummaryData
{
	public string WorkflowId { get; set; }
	public string Name { get; set; }
	public string Description { get; set; }
	public int TriggerEventType { get; set; }
	public bool IsEnabled { get; set; }
	public DateTime CreatedOn { get; set; }
	public DateTime? UpdatedOn { get; set; }
}

public class WorkflowDetailData : WorkflowSummaryData
{
	public int MaxRetryCount { get; set; }
	public int RetryBackoffBaseSeconds { get; set; }
	public List<WorkflowStepData> Steps { get; set; } = new();
}

public class WorkflowStepData
{
	public string WorkflowStepId { get; set; }
	public string WorkflowId { get; set; }
	public int ActionType { get; set; }
	public int StepOrder { get; set; }
	public string OutputTemplate { get; set; }
	public string ActionConfig { get; set; }
	public string WorkflowCredentialId { get; set; }
	public bool IsEnabled { get; set; }
	public string ConditionExpression { get; set; }
}

public class CredentialSummaryData
{
	public string WorkflowCredentialId { get; set; }
	public string Name { get; set; }
	public int CredentialType { get; set; }
	public DateTime CreatedOn { get; set; }
}

public class WorkflowRunData
{
	public string WorkflowRunId { get; set; }
	public string WorkflowId { get; set; }
	public int Status { get; set; }
	public string StatusText { get; set; }
	public int TriggerEventType { get; set; }
	public DateTime StartedOn { get; set; }
	public DateTime? CompletedOn { get; set; }
	public string ErrorMessage { get; set; }
	public int AttemptNumber { get; set; }
}

public class WorkflowRunLogData
{
	public string WorkflowRunLogId { get; set; }
	public string WorkflowRunId { get; set; }
	public string WorkflowStepId { get; set; }
	public int Status { get; set; }
	public string StatusText { get; set; }
	public string RenderedOutput { get; set; }
	public string ActionResult { get; set; }
	public string ErrorMessage { get; set; }
	public DateTime StartedOn { get; set; }
	public DateTime? CompletedOn { get; set; }
	public long? DurationMs { get; set; }
}

public class WorkflowHealthData
{
	public string WorkflowId { get; set; }
	public string WorkflowName { get; set; }
	public int TotalRuns24h { get; set; }
	public int SuccessfulRuns24h { get; set; }
	public int FailedRuns24h { get; set; }
	public int RetryingRuns24h { get; set; }
	public int TotalRuns7d { get; set; }
	public int SuccessfulRuns7d { get; set; }
	public int FailedRuns7d { get; set; }
	public int TotalRuns30d { get; set; }
	public int SuccessfulRuns30d { get; set; }
	public int FailedRuns30d { get; set; }
	public double SuccessRatePercent30d { get; set; }
	public double? AverageDurationMs30d { get; set; }
	public DateTime? LastRunOn { get; set; }
	public string LastRunStatus { get; set; }
	public string LastErrorMessage { get; set; }
}

public class TemplateVariableData
{
	public string Name { get; set; }
	public string Description { get; set; }
	public string DataType { get; set; }
	public bool IsCommon { get; set; }
}

// ── Input models ──────────────────────────────────────────────────────────────

public class SaveWorkflowInput
{
	public string WorkflowId { get; set; }
	[Required] public string Name { get; set; }
	public string Description { get; set; }
	public int TriggerEventType { get; set; }
	public bool IsEnabled { get; set; } = true;
	public int MaxRetryCount { get; set; } = 3;
	public int RetryBackoffBaseSeconds { get; set; } = 5;
}

public class SaveWorkflowStepInput
{
	public string WorkflowStepId { get; set; }
	[Required] public string WorkflowId { get; set; }
	public int ActionType { get; set; }
	public int StepOrder { get; set; }
	[Required] public string OutputTemplate { get; set; }
	public string ActionConfig { get; set; }
	public string WorkflowCredentialId { get; set; }
	public bool IsEnabled { get; set; } = true;
	public string ConditionExpression { get; set; }
}

public class SaveCredentialInput
{
	public string WorkflowCredentialId { get; set; }
	[Required] public string Name { get; set; }
	public int CredentialType { get; set; }
	/// <summary>Plaintext credential JSON — will be AES-encrypted server-side before storage.</summary>
	[Required] public string PlaintextCredentialJson { get; set; }
}

public class ValidateConditionInput
{
	[Required] public string ConditionExpression { get; set; }
	public int TriggerEventType { get; set; }
	public string SamplePayloadJson { get; set; }
}

public class ValidateConditionResult
{
	public bool IsValid { get; set; }
	public string EvaluatedResult { get; set; }
	public List<string> ParseErrors { get; set; }
}


