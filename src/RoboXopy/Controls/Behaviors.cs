using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace RoboXopy.Controls;

/// <summary>Placeholder text for a TextBox, drawn in an adorner while the box is empty.</summary>
public static class Placeholder
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.RegisterAttached("Text", typeof(string), typeof(Placeholder), new PropertyMetadata(null, OnTextChanged));

    public static string? GetText(DependencyObject d) => (string?)d.GetValue(TextProperty);
    public static void SetText(DependencyObject d, string? value) => d.SetValue(TextProperty, value);

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox box) return;
        box.Loaded -= OnLoaded;
        box.TextChanged -= OnBoxChanged;
        box.Loaded += OnLoaded;
        box.TextChanged += OnBoxChanged;
        if (box.IsLoaded) Update(box);
    }

    private static void OnLoaded(object sender, RoutedEventArgs e) => Update((TextBox)sender);
    private static void OnBoxChanged(object sender, TextChangedEventArgs e) => Update((TextBox)sender);

    private static void Update(TextBox box)
    {
        var layer = AdornerLayer.GetAdornerLayer(box);
        if (layer is null) return;

        var existing = layer.GetAdorners(box)?.OfType<PlaceholderAdorner>().FirstOrDefault();
        var show = string.IsNullOrEmpty(box.Text) && !string.IsNullOrEmpty(GetText(box));
        if (show && existing is null)
        {
            layer.Add(new PlaceholderAdorner(box));
        }
        else if (!show && existing is not null)
        {
            layer.Remove(existing);
        }
        else
        {
            existing?.InvalidateVisual();
        }
    }

    private sealed class PlaceholderAdorner : Adorner
    {
        private readonly TextBox _box;

        public PlaceholderAdorner(TextBox box) : base(box)
        {
            _box = box;
            IsHitTestVisible = false;
            box.IsVisibleChanged += (_, _) => InvalidateVisual();
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            if (!_box.IsVisible) return;
            var text = GetText(_box);
            if (string.IsNullOrEmpty(text)) return;

            var brush = _box.TryFindResource("TextControlPlaceholderForeground") as Brush ?? Brushes.Gray;
            // Hints are prose, so they use the UI font even in monospace boxes.
            var family = _box.TryFindResource("ContentControlThemeFontFamily") as FontFamily ?? SystemFonts.MessageFontFamily;
            var formatted = new FormattedText(text, CultureInfo.CurrentUICulture, _box.FlowDirection,
                new Typeface(family, _box.FontStyle, FontWeights.Normal, _box.FontStretch), _box.FontSize, brush,
                VisualTreeHelper.GetDpi(_box).PixelsPerDip)
            {
                MaxTextWidth = Math.Max(1, _box.ActualWidth - _box.Padding.Left - _box.Padding.Right - 8),
                MaxLineCount = 1,
                Trimming = TextTrimming.CharacterEllipsis,
            };

            var left = _box.BorderThickness.Left + _box.Padding.Left + 2;
            var top = (_box.ActualHeight - formatted.Height) / 2;
            drawingContext.DrawText(formatted, new Point(left, top));
        }
    }
}

/// <summary>Keeps an ItemsControl scrolled to the newest item while the user is at the bottom.</summary>
public static class AutoScroll
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached("IsEnabled", typeof(bool), typeof(AutoScroll), new PropertyMetadata(false, OnIsEnabledChanged));

    private static readonly DependencyProperty StateProperty =
        DependencyProperty.RegisterAttached("State", typeof(State), typeof(AutoScroll));

    public static bool GetIsEnabled(DependencyObject d) => (bool)d.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject d, bool value) => d.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ListBox list) return;
        if (list.GetValue(StateProperty) is State old)
        {
            old.Detach();
            list.ClearValue(StateProperty);
        }

        if (e.NewValue is true)
        {
            var state = new State(list);
            list.SetValue(StateProperty, state);
        }
    }

    private sealed class State
    {
        private readonly ListBox _list;
        private ScrollViewer? _scroll;
        private bool _stick = true;
        private bool _pending;

        public State(ListBox list)
        {
            _list = list;
            ((INotifyCollectionChanged)list.Items).CollectionChanged += OnItemsChanged;
            list.Loaded += OnLoaded;
            if (list.IsLoaded) OnLoaded(list, new RoutedEventArgs());
        }

        public void Detach()
        {
            ((INotifyCollectionChanged)_list.Items).CollectionChanged -= OnItemsChanged;
            _list.Loaded -= OnLoaded;
            if (_scroll is not null) _scroll.ScrollChanged -= OnScrollChanged;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_scroll is not null) return;
            _scroll = FindChild<ScrollViewer>(_list);
            if (_scroll is not null) _scroll.ScrollChanged += OnScrollChanged;
        }

        private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            // User scrolled (not caused by content growth): stick only when at the bottom.
            if (e.ExtentHeightChange == 0 && _scroll is not null)
            {
                _stick = _scroll.VerticalOffset >= _scroll.ScrollableHeight - 1;
            }
        }

        private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Reset) _stick = true;
            if (!_stick || _pending) return;
            _pending = true;
            _list.Dispatcher.BeginInvoke(() =>
            {
                _pending = false;
                _scroll ??= FindChild<ScrollViewer>(_list);
                _scroll?.ScrollToEnd();
            }, System.Windows.Threading.DispatcherPriority.Background);
        }
    }

    internal static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            var nested = FindChild<T>(child);
            if (nested is not null) return nested;
        }

        return null;
    }
}

/// <summary>Selects all text when a TextBox gets keyboard focus, and commits on Enter.</summary>
public static class TextBoxBehavior
{
    public static readonly DependencyProperty CommitOnEnterProperty =
        DependencyProperty.RegisterAttached("CommitOnEnter", typeof(bool), typeof(TextBoxBehavior), new PropertyMetadata(false, OnCommitOnEnterChanged));

    public static bool GetCommitOnEnter(DependencyObject d) => (bool)d.GetValue(CommitOnEnterProperty);
    public static void SetCommitOnEnter(DependencyObject d, bool value) => d.SetValue(CommitOnEnterProperty, value);

    private static void OnCommitOnEnterChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox box) return;
        box.KeyDown -= OnKeyDown;
        if (e.NewValue is true) box.KeyDown += OnKeyDown;
    }

    private static void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox box) return;
        box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        box.SelectAll();
    }
}

/// <summary>Opens a button's ContextMenu on left click (for "more" / split menus).</summary>
public static class DropDownMenu
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached("IsEnabled", typeof(bool), typeof(DropDownMenu), new PropertyMetadata(false, OnChanged));

    public static bool GetIsEnabled(DependencyObject d) => (bool)d.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject d, bool value) => d.SetValue(IsEnabledProperty, value);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ButtonBase button) return;
        button.Click -= OnClick;
        if (e.NewValue is true) button.Click += OnClick;
    }

    private static void OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.ContextMenu is not { } menu) return;
        menu.PlacementTarget = element;
        menu.Placement = PlacementMode.Bottom;
        menu.DataContext = element.DataContext;
        menu.IsOpen = true;
    }
}
