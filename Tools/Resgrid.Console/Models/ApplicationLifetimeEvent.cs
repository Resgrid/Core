namespace Resgrid.Console.Models;

/// <summary>
///     Represents the various lifetime events of an application's lifecycle.
///     These events are triggered at different points during the application's lifetime,
///     such as when the application starts, stops, or is in the process of stopping.
/// </summary>
public enum ApplicationLifetimeEvent
{
	ApplicationStarted,
	ApplicationStopping,
	ApplicationStopped
}
