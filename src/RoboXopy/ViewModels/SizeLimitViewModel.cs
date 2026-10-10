using CommunityToolkit.Mvvm.ComponentModel;

namespace RoboXopy.ViewModels;

/// <summary>Edits an optional byte size (e.g. a max-file-size filter) as a number plus a unit.</summary>
public sealed partial class SizeLimitViewModel : ObservableObject
{
    private static readonly string[] UnitNames = ["KB", "MB", "GB"];
    private readonly Func<long?> _get;
    private readonly Action<long?> _set;
    private bool _updating;

    public SizeLimitViewModel(Func<long?> get, Action<long?> set)
    {
        _get = get;
        _set = set;
        Reload();
    }

    public IReadOnlyList<string> Units => UnitNames;

    [ObservableProperty] private string _text = "";
    [ObservableProperty] private string _unit = "MB";

    public void Reload()
    {
        _updating = true;
        try
        {
            var bytes = _get();
            if (bytes is not { } b || b <= 0)
            {
                Text = "";
                return;
            }

            for (var i = UnitNames.Length - 1; i >= 0; i--)
            {
                var factor = Factor(UnitNames[i]);
                if (b % factor == 0 || i == 0)
                {
                    Unit = UnitNames[i];
                    Text = (b / (double)factor).ToString("0.###", System.Globalization.CultureInfo.CurrentCulture);
                    return;
                }
            }
        }
        finally
        {
            _updating = false;
        }
    }

    partial void OnTextChanged(string value) => Push();

    partial void OnUnitChanged(string value) => Push();

    private void Push()
    {
        if (_updating) return;
        if (string.IsNullOrWhiteSpace(Text))
        {
            _set(null);
            return;
        }

        if (double.TryParse(Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out var value) && value > 0)
        {
            _set((long)Math.Round(value * Factor(Unit)));
        }
    }

    private static long Factor(string unit) => unit switch
    {
        "GB" => 1024L * 1024 * 1024,
        "MB" => 1024L * 1024,
        _ => 1024L,
    };
}
