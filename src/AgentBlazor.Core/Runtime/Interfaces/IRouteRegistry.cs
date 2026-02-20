using AgentBlazor.Core.Runtime.Routing;

namespace AgentBlazor.Core.Runtime.Interfaces;

/// <summary>
/// Registry for application routes that can be matched against user intents.
/// </summary>
public interface IRouteRegistry
{
    /// <summary>
    /// Registers a route definition.
    /// </summary>
    void Register(RouteDefinition route);

    /// <summary>
    /// Attempts to resolve a user intent to a route.
    /// </summary>
    /// <param name="intent">The user's navigation intent (e.g., "suppliers", "go to settings").</param>
    /// <param name="match">The matched route if found.</param>
    /// <returns>True if a match was found.</returns>
    bool TryResolve(string intent, out RouteMatch match);

    /// <summary>
    /// Gets all registered routes.
    /// </summary>
    IReadOnlyCollection<RouteDefinition> GetAll();

    /// <summary>
    /// Gets the best matches for a given intent, ordered by confidence.
    /// </summary>
    /// <param name="intent">The user's navigation intent.</param>
    /// <param name="maxResults">Maximum number of results to return.</param>
    /// <returns>Matched routes ordered by confidence descending.</returns>
    IReadOnlyList<RouteMatch> ResolveAll(string intent, int maxResults = 5);

    /// <summary>
    /// Checks if a route exists for the given path.
    /// </summary>
    bool HasRoute(string path);

    /// <summary>
    /// Removes a route by path.
    /// </summary>
    bool Remove(string path);

    /// <summary>
    /// Clears all registered routes.
    /// </summary>
    void Clear();
}
