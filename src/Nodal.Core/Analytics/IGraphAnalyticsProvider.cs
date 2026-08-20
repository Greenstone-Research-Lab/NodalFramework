using Nodal.Core.Providers;

namespace Nodal.Core.Analytics;

/// <summary>Compiles provider-neutral analytics requests into provider-native commands.</summary>
public interface IGraphAnalyticsCompiler
{
    /// <summary>Compiles an analytics request without embedding runtime configuration values.</summary>
    GraphCommand Compile(GraphAnalyticsQueryModel query);
}

/// <summary>Segregates optional graph analytics services from ordinary query providers.</summary>
public interface IGraphAnalyticsProvider
{
    /// <summary>Gets the provider-native analytics compiler.</summary>
    IGraphAnalyticsCompiler AnalyticsCompiler { get; }

    /// <summary>Gets the explicitly supported analytics feature set.</summary>
    GraphAnalyticsCapabilities AnalyticsCapabilities { get; }
}
