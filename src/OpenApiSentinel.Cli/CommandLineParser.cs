using System.Globalization;

namespace OpenApiSentinel.Cli;

/// <summary>A parsed command line: command name, positional arguments, and named options.</summary>
public sealed class ParsedCommandLine
{
    public string? Command { get; init; }

    public IReadOnlyList<string> Positional { get; init; } = [];

    public IReadOnlyDictionary<string, string?> Options { get; init; } = new Dictionary<string, string?>(StringComparer.Ordinal);

    public bool Has(string name) => Options.ContainsKey(name);

    public string? Value(string name) => Options.TryGetValue(name, out var v) ? v : null;

    public int? IntValue(string name) =>
        Value(name) is { } raw && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;
}

/// <summary>
/// Minimal dependency-free argument parser.
/// Supports: <c>--flag</c>, <c>--opt value</c>, <c>--opt=value</c>, and <c>-</c> positionals (stdin marker).
/// Everything after <c>--</c> is treated as positional.
/// </summary>
public static class CommandLineParser
{
    public static ParsedCommandLine Parse(string[] args)
    {
        var options = new Dictionary<string, string?>(StringComparer.Ordinal);
        List<string> positional = [];
        string? command = null;
        var noMoreOptions = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (noMoreOptions)
            {
                positional.Add(arg);
                continue;
            }

            if (arg == "--")
            {
                noMoreOptions = true;
                continue;
            }

            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                var body = arg[2..];
                string name;
                string? value = null;
                var eq = body.IndexOf('=');
                if (eq >= 0)
                {
                    name = body[..eq];
                    value = body[(eq + 1)..];
                }
                else
                {
                    name = body;
                    // Consume the next token as the value unless it starts with '-' or is missing.
                    if (i + 1 < args.Length && !(args[i + 1].StartsWith('-') && args[i + 1] != "-"))
                    {
                        value = args[++i];
                    }
                }
                options[name] = value;
                continue;
            }

            if (arg.StartsWith('-') && arg != "-")
            {
                // Single-dash options (only -o/-h style aliases are used) behave like long options:
                // consume the next token as the value unless it looks like another option.
                var name = arg.TrimStart('-');
                string? value = null;
                if (i + 1 < args.Length && !(args[i + 1].StartsWith('-') && args[i + 1] != "-"))
                {
                    value = args[++i];
                }
                options[name] = value;
                continue;
            }

            if (command is null)
            {
                command = arg;
            }
            else
            {
                positional.Add(arg);
            }
        }

        return new ParsedCommandLine { Command = command, Positional = positional, Options = options };
    }
}
