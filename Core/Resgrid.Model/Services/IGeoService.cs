using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Interface IGeoService
	/// </summary>
	public interface IGeoService
	{
		/// <summary>
		/// Gets the personnel eta in seconds asynchronous.
		/// </summary>
		/// <param name="log">The log.</param>
		/// <returns>Task&lt;System.Double&gt;.</returns>
		Task<double> GetPersonnelEtaInSecondsAsync(ActionLog log);

		/// <summary>
		/// Gets the eta in seconds asynchronous.
		/// </summary>
		/// <param name="start">The start.</param>
		/// <param name="destination">The destination.</param>
		/// <returns>Task&lt;System.Double&gt;.</returns>
		Task<double> GetEtaInSecondsAsync(string start, string destination);
	}
}
