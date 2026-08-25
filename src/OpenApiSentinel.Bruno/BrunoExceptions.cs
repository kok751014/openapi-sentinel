namespace OpenApiSentinel.Bruno;

/// <summary>The Bruno collection file could not be parsed or violates the supported format.</summary>
public sealed class BrunoFormatException : Exception
{
    public BrunoFormatException(string message) : base(message) { }

    public BrunoFormatException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>The requested Bruno operation cannot be performed safely (e.g. unsupported layout).</summary>
public sealed class BrunoSyncException : Exception
{
    public BrunoSyncException(string message) : base(message) { }
}
