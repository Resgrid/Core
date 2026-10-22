using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.RunCards;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Applies RunCardTemplateCatalog templates to a department (conditional run cards): maps the template's resources by
	/// name, then by meaning, onto the department's unit types and roles, and creates the packages, codes and a disabled
	/// card. An unmatched resource blocks applying: a silently dropped row is a resource that never goes to a call.
	/// </summary>
	public interface IRunCardTemplatesService
	{
		/// <summary>How the template maps onto the department, without changing anything. Null for an unknown template.</summary>
		Task<RunCardTemplatePreview> PreviewAsync(int departmentId, string templateId);

		/// <summary>Creates the packages, missing codes and the card (disabled). Refused while anything is unmatched.</summary>
		Task<RunCardTemplateApplyResult> ApplyAsync(int departmentId, string templateId, string userId, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>Templates for the department's call types that no run card covers yet, most resolvable first.</summary>
		Task<List<RunCardTemplateSuggestion>> GetSuggestionsAsync(int departmentId, int maximum = 6);
	}

	public class RunCardTemplatePreview
	{
		public RunCardTemplate Template { get; set; }

		/// <summary>"Engine (class) → Engine, Pumper".</summary>
		public List<string> Mappings { get; set; } = new List<string>();

		/// <summary>Resources with no match in the department; any of these blocks applying.</summary>
		public List<string> Unmatched { get; set; } = new List<string>();

		public List<string> Triggers { get; set; } = new List<string>();

		public List<string> Warnings { get; set; } = new List<string>();

		public List<string> PackagesReused { get; set; } = new List<string>();

		public List<string> PackagesCreated { get; set; } = new List<string>();

		public List<string> CodesAdded { get; set; } = new List<string>();

		public bool CanApply => Unmatched.Count == 0;
	}

	public class RunCardTemplateApplyResult
	{
		public bool Success { get; set; }

		public string Message { get; set; }

		public int? RunCardId { get; set; }

		public RunCardTemplatePreview Preview { get; set; }
	}

	public class RunCardTemplateSuggestion
	{
		public RunCardTemplate Template { get; set; }

		/// <summary>Why it is suggested ("No run card covers 'Structure Fire'").</summary>
		public string Reason { get; set; }

		public int ResolvedTargets { get; set; }

		public int TotalTargets { get; set; }
	}
}
