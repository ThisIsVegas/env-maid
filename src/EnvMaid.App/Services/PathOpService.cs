using System.Security.Cryptography;
using System.Text;
using EnvMaid.App.Models;

namespace EnvMaid.App.Services;

/// <summary>
/// Turns "here is what I want the PATH to become" into an ordered list of operations, and
/// applies such a list to whatever the registry actually holds.
/// </summary>
/// <remarks>
/// Pure and disk-free, which is what makes the elevated helper's logic testable without a UAC
/// prompt: write an intent, apply it to a fake value, assert the result.
/// </remarks>
public static class PathOpService
{
    /// <summary>Hashes a stored value so a baseline can be compared without carrying it around.</summary>
    public static string Hash(string rawData) =>
        "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.Unicode.GetBytes(rawData))).ToLowerInvariant();

    public static IntentBaseline BaselineOf(VariableValue value) =>
        new(value.Present, value.Type.ToString(), Hash(value.RawData));

    public static bool Matches(IntentBaseline baseline, VariableValue current) =>
        baseline.Present == current.Present
        && baseline.RegistryType == current.Type.ToString()
        && baseline.Hash == Hash(current.RawData);

    /// <summary>
    /// Describes how to get from <paramref name="from"/> to <paramref name="to"/>.
    /// </summary>
    /// <remarks>
    /// Removals come first so that positions in the Add ops refer to the list after the removals
    /// have happened — otherwise an Add and a Remove in the same batch fight over an index.
    /// </remarks>
    public static IReadOnlyList<PathOp> Diff(IReadOnlyList<string> from, IReadOnlyList<string> to)
    {
        var ops = new List<PathOp>();

        foreach (var token in from.Where(t => !to.Contains(t, StringComparer.OrdinalIgnoreCase)))
            ops.Add(new PathOp(PathOpKind.Remove, token));

        var survivors = from.Where(t => to.Contains(t, StringComparer.OrdinalIgnoreCase)).ToList();

        for (var i = 0; i < to.Count; i++)
        {
            var token = to[i];
            if (!survivors.Contains(token, StringComparer.OrdinalIgnoreCase))
            {
                ops.Add(new PathOp(PathOpKind.Add, token, At: i));
                survivors.Insert(Math.Min(i, survivors.Count), token);
            }
        }

        // Whatever order the survivors ended up in, state the intended final position for any
        // that moved. Positions are re-derived at apply time, so a shifted list still lands right.
        for (var i = 0; i < to.Count; i++)
        {
            var at = survivors.FindIndex(t => string.Equals(t, to[i], StringComparison.OrdinalIgnoreCase));
            if (at < 0 || at == i)
                continue;

            var token = survivors[at];
            survivors.RemoveAt(at);
            survivors.Insert(i, token);
            ops.Add(new PathOp(PathOpKind.Move, token, From: at, To: i));
        }

        return ops;
    }

    /// <summary>
    /// Applies ops to the entries actually found on disk.
    /// </summary>
    /// <remarks>
    /// An op that no longer applies — removing a token someone else already removed, moving one
    /// that is gone — is skipped rather than failing the batch. The baseline check upstream is
    /// what catches a genuinely changed value; by the time ops are applied, the value has already
    /// been verified to match, so a mismatch here can only come from a duplicate token.
    /// </remarks>
    public static IReadOnlyList<string> Apply(IReadOnlyList<string> current, IReadOnlyList<PathOp> ops)
    {
        var result = current.ToList();

        foreach (var op in ops)
        {
            switch (op.Op)
            {
                case PathOpKind.Remove:
                    var removeAt = IndexOf(result, op.RawToken);
                    if (removeAt >= 0)
                        result.RemoveAt(removeAt);
                    break;

                case PathOpKind.Add:
                    var insertAt = Math.Clamp(op.At ?? result.Count, 0, result.Count);
                    result.Insert(insertAt, op.RawToken);
                    break;

                case PathOpKind.Move:
                    var moveFrom = IndexOf(result, op.RawToken);
                    if (moveFrom < 0)
                        break;
                    var token = result[moveFrom];
                    result.RemoveAt(moveFrom);
                    result.Insert(Math.Clamp(op.To ?? moveFrom, 0, result.Count), token);
                    break;
            }
        }

        return result;
    }

    private static int IndexOf(List<string> entries, string token) =>
        entries.FindIndex(e => string.Equals(e, token, StringComparison.OrdinalIgnoreCase));
}
