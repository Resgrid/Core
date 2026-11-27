using System;
using System.Threading.Tasks;
using Resgrid.Model.Checklists;

namespace Resgrid.Services
{
	/// <summary>
	/// S3 checklist witness signature (free with Checklists): the witness step of a form that requires it records an L2 signature
	/// inside the same transaction that completes the run, bound to the completion's operational content hash
	/// (<see cref="ChecklistWitnessArtifact"/>). The signing itself is the caller's (the shared signing layer); this service only
	/// decides when a witness may sign and what the content is.
	/// </summary>
	public partial class ChecklistsService
	{
		public async Task WitnessWithSignatureAsync(ChecklistActor actor, string id, string submissionHash, string attestation, Func<ChecklistRunView, string, Task<bool>> sign)
		{
			if (sign == null) throw new ArgumentNullException(nameof(sign));
			await WitnessCoreAsync(actor, id, submissionHash, attestation, sign);
		}

		public async Task<string> WitnessArtifactHashAsync(ChecklistActor actor, string id)
		{
			var view = await GetRunAsync(actor, id);
			return view?.Completion == null || view.Completion.State == (int)ChecklistRunState.InProgress ? null : await WitnessArtifactHashCoreAsync(actor, view);
		}

		/// <summary>The hash over the run's operational content; item failures come from the stored item rows (outcome state, not private).</summary>
		private async Task<string> WitnessArtifactHashCoreAsync(ChecklistActor actor, ChecklistRunView view)
		{
			var items = await _store.ListAsync<ChecklistCompletionItem>(actor.DepartmentId, view.Completion.Id, take: 250);
			return ChecklistWitnessArtifact.Hash(view.Completion, view.Form, view.Input?.Answers, items, view.Files, view.Input);
		}
	}
}
