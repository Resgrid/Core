namespace Resgrid.Model
{
	/// <summary>The routing provider that answers first for a department (IRoutingService.GetProviderInfoAsync).</summary>
	public class RoutingProviderInfo
	{
		/// <summary>A <see cref="RoutingProviderNames"/> value; estimate when no provider is configured.</summary>
		public string Name { get; set; }

		public RoutingCapabilities Capabilities { get; set; }

		public string Attribution { get; set; }

		/// <summary>In-app navigation: the switch is on and the provider is Valhalla or Mapbox.</summary>
		public bool NavigationAvailable { get; set; }

		/// <summary>True when it is the department's own account (Mapping Settings).</summary>
		public bool DepartmentAccount { get; set; }
	}
}
