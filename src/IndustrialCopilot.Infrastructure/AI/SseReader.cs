using System.Runtime.CompilerServices;
using System.Text;

namespace IndustrialCopilot.Infrastructure.AI;

internal static class SseReader
{
    // Bound both an unfinished line and the accumulated event, including ignored fields.
    internal const int MaximumEventCharacters = 1024 * 1024;

    internal static async IAsyncEnumerable<string> ReadAsync(Stream source,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(source, new UTF8Encoding(false, true), leaveOpen: true);
        var buffer = new char[4096];
        var line = new StringBuilder();
        var data = new StringBuilder();
        var eventCharacters = 0;
        var previousCarriageReturn = false;
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) != 0)
        {
            for (var index = 0; index < read; index++)
            {
                var value = buffer[index];
                if (previousCarriageReturn && value == '\n') { previousCarriageReturn = false; continue; }
                previousCarriageReturn = value == '\r';
                if (++eventCharacters > MaximumEventCharacters) throw OpenAiProtocol.Invalid();
                if (value is not ('\r' or '\n')) { line.Append(value); continue; }
                if (line.Length == 0)
                {
                    eventCharacters = 0;
                    if (data.Length != 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        yield return data.ToString(0, data.Length - 1);
                        data.Clear();
                    }
                }
                else
                {
                    var field = line.ToString();
                    if (field.StartsWith("data:", StringComparison.Ordinal))
                    {
                        var start = field.Length > 5 && field[5] == ' ' ? 6 : 5;
                        data.Append(field.AsSpan(start)).Append('\n');
                    }
                    else if (field == "data") data.Append('\n');
                    line.Clear();
                }
            }
        }
        // Unterminated data is deliberately not dispatched at EOF.
    }
}
