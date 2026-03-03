namespace Resgrid.Config
{
	public static class WorkflowConfig
	{

		/// <summary>Default maximum number of retry attempts for a failed workflow action execution.</summary>
		public static int DefaultMaxRetryCount = 3;

		/// <summary>Hard ceiling on MaxRetryCount a user may configure — prevents infinite retry abuse.</summary>
		public static int MaxAllowedRetryCount = 5;

		/// <summary>Base seconds for exponential back-off: delay = RetryBackoffBaseSeconds * 2^(attempt-1)</summary>
		public static int RetryBackoffBaseSeconds = 5;

		/// <summary>Maximum concurrent workflow executions processed by a single worker instance.</summary>
		public static int MaxConcurrentWorkflows = 5;

		// ── Rate Limiting ────────────────────────────────────────────────────────────

		/// <summary>Maximum workflow enqueue operations allowed per department per minute (paid plans).</summary>
		public static int RateLimitPerDepartmentPerMinute = 60;

		/// <summary>Aggressive rate limit for free-plan departments — cannot be bypassed by exempt event types.</summary>
		public static int FreePlanRateLimitPerDepartmentPerMinute = 5;

		/// <summary>Maximum workflow runs a free-plan department may enqueue in a single calendar day (UTC).</summary>
		public static int FreePlanDailyRunLimit = 50;

		// ── Workflow and Step Caps ───────────────────────────────────────────────────

		/// <summary>Maximum number of workflows a paid-plan department may create.</summary>
		public static int MaxWorkflowsPerDepartment = 28;

		/// <summary>Maximum number of steps per workflow for paid-plan departments.</summary>
		public static int MaxStepsPerWorkflow = 20;

		/// <summary>Maximum number of workflows a free-plan department may create.</summary>
		public static int FreeMaxWorkflowsPerDepartment = 3;

		/// <summary>Maximum number of steps per workflow for free-plan departments.</summary>
		public static int FreeMaxStepsPerWorkflow = 5;

		// ── Per-Action Daily Send Limits ─────────────────────────────────────────────

		/// <summary>Maximum emails a department may send per workflow step per day (paid plans).</summary>
		public static int MaxDailyEmailSendsPerDepartment = 500;

		/// <summary>Maximum SMS messages a department may send per workflow step per day (paid plans).</summary>
		public static int MaxDailySmsPerDepartment = 200;

		/// <summary>Maximum emails a free-plan department may send via workflows per day.</summary>
		public static int FreeMaxDailyEmailSendsPerDepartment = 10;

		/// <summary>Maximum SMS messages a free-plan department may send via workflows per day.</summary>
		public static int FreeMaxDailySmsPerDepartment = 5;

		// ── Recipient Caps ───────────────────────────────────────────────────────────

		/// <summary>Maximum email recipients (To + Cc combined) for paid-plan workflow steps.</summary>
		public static int MaxEmailRecipients = 10;

		/// <summary>Maximum SMS recipients for paid-plan workflow steps.</summary>
		public static int MaxSmsRecipients = 5;

		// ── Template Size Limits ─────────────────────────────────────────────────────

		/// <summary>Maximum length in characters for an OutputTemplate stored on a WorkflowStep (64 KB).</summary>
		public static int MaxOutputTemplateLength = 65536;

		/// <summary>Maximum length in characters of rendered template content passed to an executor (256 KB).</summary>
		public static int MaxRenderedContentLength = 262144;

		/// <summary>Maximum length in characters for a ConditionExpression on a WorkflowStep (4 KB).</summary>
		public static int MaxConditionExpressionLength = 4096;

		// ── Scriban Sandbox Limits ───────────────────────────────────────────────────

		/// <summary>Maximum number of loop iterations in a Scriban template.</summary>
		public static int ScribanLoopLimit = 500;

		/// <summary>Maximum recursion depth in a Scriban template.</summary>
		public static int ScribanRecursionLimit = 50;
	}
}

