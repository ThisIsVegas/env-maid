using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace EnvMaid.App.Models;

/// <summary>
/// One entry on the PATH.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="RawToken"/> is the only stored value and the only write point. Everything the app
/// needs — a cleaned display form, an expanded form for touching disk, a folded form for
/// comparison — is derived from it, so no caller can leave the four inconsistent, and an entry
/// nobody edited round-trips to the registry byte for byte.
/// </para>
/// <para>
/// Deriving on every change costs a trim, a quote strip, an expansion and a case-fold. Caching
/// would buy nothing measurable against a grid that re-renders anyway, and would reintroduce
/// exactly the staleness this model exists to prevent.
/// </para>
/// </remarks>
public partial class PathEntry : ObservableObject
{
    private string _rawToken;

    /// <summary>
    /// The token exactly as stored, including any quotes or padding.
    /// </summary>
    /// <remarks>
    /// Assigning this is what Normalize and Compress do — normalization is a user-invoked action
    /// with a visible diff, never a side effect of parsing.
    /// </remarks>
    public string RawToken
    {
        get => _rawToken;
        set
        {
            if (_rawToken == value)
                return;

            _rawToken = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ParsedValue));
            OnPropertyChanged(nameof(ExpandedValue));
            OnPropertyChanged(nameof(ComparisonKey));
            OnPropertyChanged(nameof(DisplayValue));
            OnPropertyChanged(nameof(DisplayDiffersFromRaw));
        }
    }

    /// <summary>The token cleaned for display: trimmed, and unwrapped if it was quoted.</summary>
    public string ParsedValue => Parse(_rawToken);

    /// <summary>The parsed token with <c>%VAR%</c> references expanded. What disk checks use.</summary>
    public string ExpandedValue => Environment.ExpandEnvironmentVariables(ParsedValue);

    /// <summary>Case-folded, trailing-separator-normalized form, for comparing two entries.</summary>
    public string ComparisonKey => Fold(ExpandedValue);

    /// <summary>What the grid shows.</summary>
    public string DisplayValue => ParsedValue;

    /// <summary>
    /// True when the display form differs from what is stored — a quoted or padded entry.
    /// The two are shown distinctly rather than conflated, so an edit is never a surprise.
    /// </summary>
    public bool DisplayDiffersFromRaw => ParsedValue != _rawToken;

    public ObservableCollection<Diagnostic> Diagnostics { get; } = new();

    [ObservableProperty]
    private ExistenceStatus _existenceStatus = ExistenceStatus.Unknown;

    [ObservableProperty]
    private bool _isChecked;

    [ObservableProperty]
    private int _globalRank;

    /// <summary>
    /// This entry sits past the point where some other PATH-writing tools mishandle the value.
    /// Not a truncation point — nothing is dropped here, and editing it in EnvMaid is safe.
    /// </summary>
    [ObservableProperty]
    private bool _isPastLengthLimit;

    /// <summary>The last entry before the caution boundary; where the marker row is drawn.</summary>
    [ObservableProperty]
    private bool _isLengthLimitBoundary;

    /// <summary>
    /// This entry sits past what a single environment variable can hold. A PATH reaching here
    /// cannot be written at all, and Save is refused until it is shortened.
    /// </summary>
    [ObservableProperty]
    private bool _isPastWriteLimit;

    /// <summary>The last entry before the write limit; where the second marker row is drawn.</summary>
    [ObservableProperty]
    private bool _isWriteLimitBoundary;

    public ObservableCollection<ShadowConflict> ShadowConflicts { get; } = new();

    public bool HasShadowConflicts => ShadowConflicts.Count > 0;

    public bool HasAttention => Diagnostics.Count > 0 || HasShadowConflicts;

    /// <summary>The most serious thing wrong with this entry, or null when nothing is.</summary>
    public Severity? WorstSeverity =>
        Diagnostics.Count == 0 ? null : Diagnostics.Max(d => d.Severity);

    /// <summary>
    /// Whether bulk cleanup may pre-check this entry for removal.
    /// </summary>
    /// <remarks>
    /// One unsafe diagnostic vetoes the entry, so an exact duplicate that <em>also</em> has an
    /// unresolved variable is not silently checked. Auto-check is kept for the safe kinds
    /// because "remove broken" over thirty dead entries is what bulk cleanup is for.
    /// </remarks>
    public bool IsAutoSelectable =>
        Diagnostics.Count > 0 && Diagnostics.All(d => d.SafeToAutoSelect);

    public bool Has(DiagnosticKind kind) => Diagnostics.Any(d => d.Kind == kind);

    /// <summary>
    /// The entry names nothing usable — a missing folder or an empty token.
    /// </summary>
    /// <remarks>
    /// Deliberately excludes an inaccessible folder and an undefined variable. Both look broken
    /// and neither is fixed by deletion: one may work for another process, the other needs its
    /// variable defined.
    /// </remarks>
    public bool IsBroken => Has(DiagnosticKind.FolderMissing) || Has(DiagnosticKind.EmptyToken);

    public bool IsDuplicate => Diagnostics.Any(d =>
        d.Kind is DiagnosticKind.DuplicateL1 or DiagnosticKind.DuplicateL2
            or DiagnosticKind.DuplicateL3 or DiagnosticKind.DuplicateL4);

    public string StatusLabel
    {
        get
        {
            if (Has(DiagnosticKind.FolderMissing)) return "Missing location";
            if (Has(DiagnosticKind.FolderInaccessible)) return "Cannot read";
            if (Has(DiagnosticKind.EmptyToken)) return "Empty entry";
            if (Has(DiagnosticKind.UnresolvedVariable)) return "Undefined variable";
            if (IsDuplicate) return "Duplicate";
            if (HasShadowConflicts) return "Command priority";
            if (Has(DiagnosticKind.NoExecutables)) return "Review";
            return string.Empty;
        }
    }

    /// <summary>The first diagnostic's message, for the grid's one-line explanation.</summary>
    public string Reason => Diagnostics.Count == 0 ? string.Empty : Diagnostics[0].Message;

    /// <summary>Worst (most-real) confidence among this entry's shadow conflicts,
    /// used to color the grid's conflict marker. Null when there are none.</summary>
    public ConflictConfidence? ShadowConfidence =>
        ShadowConflicts.Count == 0 ? null : ShadowConflicts.Max(c => c.Confidence);

    public PathScope Scope { get; }

    /// <summary>Where this entry sat when it was scanned, before any staged reordering.</summary>
    public int OriginalIndex { get; }

    public PathEntry(string rawToken, PathScope scope, int originalIndex = 0)
    {
        _rawToken = rawToken;
        Scope = scope;
        OriginalIndex = originalIndex;

        ShadowConflicts.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasShadowConflicts));
            OnPropertyChanged(nameof(HasAttention));
            OnPropertyChanged(nameof(StatusLabel));
            OnPropertyChanged(nameof(ShadowConfidence));
        };

        Diagnostics.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasAttention));
            OnPropertyChanged(nameof(StatusLabel));
            OnPropertyChanged(nameof(WorstSeverity));
            OnPropertyChanged(nameof(IsAutoSelectable));
            OnPropertyChanged(nameof(Reason));
        };
    }

    /// <summary>
    /// Cleans a token for display: trims padding, and unwraps a fully quoted value.
    /// </summary>
    /// <remarks>
    /// This never touches <see cref="RawToken"/>. A padded or quoted entry is reported as a
    /// diagnostic and shown cleaned, but what is stored stays exactly as the user left it until
    /// they ask for it to change.
    /// </remarks>
    public static string Parse(string raw)
    {
        var trimmed = raw.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
            trimmed = trimmed[1..^1];
        return trimmed;
    }

    /// <summary>
    /// Folds an expanded path for comparison: drops a trailing separator and case.
    /// </summary>
    /// <remarks>
    /// A drive root keeps its separator — <c>C:\</c> folded to <c>C:</c> would mean the current
    /// directory on that drive, which is a different place.
    /// </remarks>
    public static string Fold(string expanded)
    {
        var trimmed = expanded.TrimEnd('\\', '/');
        if (trimmed.Length == 2 && trimmed[1] == ':')
            trimmed += '\\';
        return trimmed.ToLowerInvariant();
    }
}
