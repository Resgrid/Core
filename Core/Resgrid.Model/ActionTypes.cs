using System.ComponentModel.DataAnnotations;

namespace Resgrid.Model
{
	/// <summary>
	/// Enumeration of the all the Actions that a user can perform in the system
	/// </summary>
	public enum ActionTypes
	{
		/// <summary>
		/// User is Available
		/// </summary>
		[Display(Name = "Available")]
		StandingBy	= 0,

		/// <summary>
		/// User is Not Responding (Generic)
		/// </summary>
		[Display(Name = "Not Responding")]
		NotResponding = 1,

		/// <summary>
		/// User is Responding (Generic)
		/// </summary>
		[Display(Name = "Responding")]
		Responding = 2,

		/// <summary>
		/// User is OnScene (Generic)
		/// </summary>
		[Display(Name = "On Scene")]
		OnScene = 3,

		/// <summary>
		/// User is Available in a Station (Generic)
		/// </summary>
		[Display(Name = "Available Station")]
		AvailableStation = 4,

		/// <summary>
		/// User is Responding to a Station (For a Specific Station)
		/// </summary>
		[Display(Name = "Responding To Station")]
		RespondingToStation = 5,

		/// <summary>
		/// User is Responding directly to a scene (For a Specific Call)
		/// </summary>
		[Display(Name = "Responding To Scene")]
		RespondingToScene = 6
	}
}
