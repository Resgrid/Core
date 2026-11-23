namespace Resgrid.Model
{
	public enum CallNoteSources
	{
		None		= 0,
		Web			= 1,
		Mobile	= 2,
		System  = 3,

		/// <summary>
		/// A member of the public's text on an intake SMS conversation (plan §8 D15, M0325). Not System on purpose: the texter's new
		/// information is a person's note, so it triggers the Enhanced AI destination review like any other person-written note.
		/// </summary>
		IntakeText = 4,

		/// <summary>
		/// What a CAD email changed (or would have changed) on the call, written by the CAD update engine
		/// (cad-email-call-updates-plan.md §4.4). Shown as "CAD update".
		/// </summary>
		CadEmail = 5
	}
}