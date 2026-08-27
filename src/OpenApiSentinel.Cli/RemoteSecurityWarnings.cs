using OpenApiSentinel.OpenApi;

namespace OpenApiSentinel.Cli;

internal static class RemoteSecurityWarnings
{
    internal static void WriteIfInsecure(RemoteFetchPolicy policy, bool insecureTlsOverride, IConsoleWriter console)
    {
        if (policy.InsecureTls || insecureTlsOverride)
        {
            console.WriteError("WARNING: TLS certificate validation is disabled for remote OpenAPI fetching.");
            console.WriteError("Use only with explicitly trusted internal/test endpoints.");
        }
    }
}
