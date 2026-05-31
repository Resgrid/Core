namespace Resgrid.Model
{
	/// <summary>
	/// The type of value a feature flag resolves to. Boolean is a simple on/off toggle; the others
	/// support multivariate flags where a string/number/JSON payload is returned to the caller.
	/// </summary>
	public enum FeatureFlagValueTypes
	{
		Boolean = 0,
		String = 1,
		Number = 2,
		Json = 3,
	}
}
