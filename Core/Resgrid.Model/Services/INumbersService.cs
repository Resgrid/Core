using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
    public interface INumbersService
    {
		/// <summary>
		/// Gets the available numbers.
		/// </summary>
		/// <param name="country">The country.</param>
		/// <param name="areaCode">The area code.</param>
		/// <returns>List&lt;TextNumber&gt;.</returns>
		Task<List<TextNumber>> GetAvailableNumbers(string country, string areaCode);

		/// <summary>
		/// Provisions the number asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="number">The number.</param>
		/// <param name="country">The country.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> ProvisionNumberAsync(int departmentId, string number, string country);

		/// <summary>
		/// Saves the inbound message event asynchronous.
		/// </summary>
		/// <param name="messageEvent">The message event.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;InboundMessageEvent&gt;.</returns>
		Task<InboundMessageEvent> SaveInboundMessageEventAsync(InboundMessageEvent messageEvent, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Determines whether [is number pattern valid] [the specified pattern].
		/// </summary>
		/// <param name="pattern">The pattern.</param>
		/// <param name="number">The number.</param>
		/// <returns><c>true</c> if [is number pattern valid] [the specified pattern]; otherwise, <c>false</c>.</returns>
		bool IsNumberPatternValid(string pattern, string number);

		/// <summary>
		/// Doeses the number match any pattern.
		/// </summary>
		/// <param name="patterns">The patterns.</param>
		/// <param name="number">The number.</param>
		/// <returns><c>true</c> if XXXX, <c>false</c> otherwise.</returns>
		bool DoesNumberMatchAnyPattern(List<string> patterns, string number);
    }
}
