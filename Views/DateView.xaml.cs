using System.Globalization;
using DesktopPanels.Models;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace DesktopPanels.Views;

public sealed partial class DateView : UserControl
{
    readonly DispatcherQueueTimer _timer;
    readonly TextBlock[] _weekdayLabels = new TextBlock[7];
    readonly DayCell[] _days = new DayCell[42];
    readonly SolidColorBrush _dayBrush = new(Color.FromArgb(255, 255, 255, 255));
    readonly SolidColorBrush _inkBrush = new(Color.FromArgb(255, 22, 22, 22));
    readonly SolidColorBrush _badgeBrush = new(Color.FromArgb(242, 255, 255, 255));
    readonly SolidColorBrush _weekdayBrush = new(Color.FromArgb(204, 255, 255, 255));
    readonly SolidColorBrush _clearBrush = new(Color.FromArgb(0, 255, 255, 255));
    readonly SolidColorBrush _hoverBrush = new(Color.FromArgb(230, 255, 255, 255));
    PanelModel? _model;
    DateTime _month;
    bool _fitting;
    string _fitKey = "";
    string _tickToken = "";

    public DateView()
    {
        InitializeComponent();
        var today = DateTime.Today;
        _month = new DateTime(today.Year, today.Month, 1);
        BuildCalendar();
        SizeChanged += (_, _) => Fit(force: true);
        _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick += (_, _) => OnTick();
        _timer.Start();
        Unloaded += (_, _) => _timer.Stop();
    }

    public void Load(PanelModel model)
    {
        _model = model;
        _fitKey = "";
        _tickToken = "";
        Render();
    }

    void OnTick()
    {
        if (_model is null)
        {
            return;
        }

        var token = $"{DateTime.Today:yyyyMMdd}|{_month:yyyyMM}|{_model.DateFace}|{_model.DateFormat}|{_model.ShowWeekday}|{_model.DateFontFamily}";
        if (token == _tickToken)
        {
            return;
        }

        Render();
    }

    void Render()
    {
        if (_model is null)
        {
            return;
        }

        var calendar = IsCalendar;
        DateRoot.Visibility = calendar ? Visibility.Collapsed : Visibility.Visible;
        CalendarRoot.Visibility = calendar ? Visibility.Visible : Visibility.Collapsed;
        var family = string.IsNullOrWhiteSpace(_model.DateFontFamily) ? "Segoe UI Variable Display" : _model.DateFontFamily;
        ApplyFont(new FontFamily(family));
        if (calendar)
        {
            RenderCalendar();
        }
        else
        {
            RenderDate();
        }

        Fit(force: false);
        _tickToken = $"{DateTime.Today:yyyyMMdd}|{_month:yyyyMM}|{_model.DateFace}|{_model.DateFormat}|{_model.ShowWeekday}|{_model.DateFontFamily}";
    }

    bool IsCalendar => _model?.DateFace.Equals("Calendar", StringComparison.OrdinalIgnoreCase) == true;

    void RenderDate()
    {
        var now = DateTime.Now;
        DateText.Text = FormatDate(now, _model!.DateFormat);
        DayText.Text = now.ToString("dddd", CultureInfo.CurrentCulture);
        DayText.Visibility = _model.ShowWeekday ? Visibility.Visible : Visibility.Collapsed;
    }

    void RenderCalendar()
    {
        var today = DateTime.Today;
        MonthTitle.Text = _month.ToString("MMMM yyyy", CultureInfo.CurrentCulture);
        FillDays(today);
    }

    void OnPreviousMonth(object sender, RoutedEventArgs e) => MoveMonth(-1);

    void OnNextMonth(object sender, RoutedEventArgs e) => MoveMonth(1);

    void MoveMonth(int months)
    {
        _month = _month.AddMonths(months);
        _fitKey = "";
        Render();
    }

    void OnNavEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        button.Opacity = 1;
        button.Background = _hoverBrush;
        SetContentBrush(button, _inkBrush.Color);
    }

    void OnNavExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        button.Opacity = 1;
        button.Background = _clearBrush;
        SetContentBrush(button, _dayBrush.Color);
    }

    void Fit(bool force)
    {
        if (IsCalendar)
        {
            FitCalendar(force);
        }
        else
        {
            FitDate(force);
        }
    }

    void FitDate(bool force)
    {
        if (_fitting || _model is null || ActualWidth < 40 || ActualHeight < 40)
        {
            return;
        }

        var showDay = _model.ShowWeekday;
        var sample = WidestDate(_model.DateFormat);
        var daySample = showDay ? LongestWeekday() : "";
        var key = $"{ActualWidth:0}|{ActualHeight:0}|{sample}|{daySample}|{showDay}|{_model.DateFontFamily}|date";
        if (!force && key == _fitKey)
        {
            return;
        }

        _fitting = true;
        var visibleDate = DateText.Text;
        var visibleDay = DayText.Text;
        try
        {
            DateText.Text = sample;
            if (showDay)
            {
                DayText.Text = daySample;
            }

            var width = Math.Max(40, ActualWidth - 32);
            var height = Math.Max(40, ActualHeight - 16);
            double low = 8;
            double high = Math.Max(48, height);
            double best = low;
            var infinite = new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity);
            for (var step = 0; step < 14; step++)
            {
                var mid = (low + high) / 2;
                ApplyDateSize(mid);
                DateText.Measure(infinite);
                var neededWidth = DateText.DesiredSize.Width;
                var neededHeight = DateText.DesiredSize.Height;
                if (showDay)
                {
                    DayText.Measure(infinite);
                    neededWidth = Math.Max(neededWidth, DayText.DesiredSize.Width);
                    neededHeight += DayText.DesiredSize.Height + DayText.Margin.Top;
                }

                if (neededWidth <= width && neededHeight <= height)
                {
                    best = mid;
                    low = mid;
                }
                else
                {
                    high = mid;
                }
            }

            ApplyDateSize(best);
            _fitKey = key;
        }
        finally
        {
            DateText.Text = visibleDate;
            DayText.Text = visibleDay;
            _fitting = false;
        }
    }

    void FitCalendar(bool force)
    {
        if (_fitting || _model is null || ActualWidth < 40 || ActualHeight < 40 || _days[0] is null)
        {
            return;
        }

        var key = $"{ActualWidth:0}|{ActualHeight:0}|{_model.DateFontFamily}|calendar";
        if (!force && key == _fitKey)
        {
            return;
        }

        _fitting = true;
        var visibleTitle = MonthTitle.Text;
        try
        {
            var width = Math.Max(40, ActualWidth - 16);
            var height = Math.Max(48, ActualHeight - 10);
            MonthTitle.Text = WidestMonthTitle(_month.Year);
            double low = 11;
            double high = Math.Max(28, width / 6);
            double best = low;
            var infinite = new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity);
            for (var step = 0; step < 12; step++)
            {
                var mid = (low + high) / 2;
                ApplyNavSize(mid);
                if (HeaderFits(width, infinite))
                {
                    best = mid;
                    low = mid;
                }
                else
                {
                    high = mid;
                }
            }

            ApplyNavSize(best);
            var cellWidth = width / 7.0;
            var weekdayFont = Math.Clamp(Math.Round(best * 0.4, MidpointRounding.AwayFromZero), 9, 18);
            foreach (var label in _weekdayLabels)
            {
                label.FontSize = weekdayFont;
            }

            ApplyWeekdayLabels(cellWidth);
            var headerSlot = new Windows.Foundation.Size(width, double.PositiveInfinity);
            NavRow.Measure(headerSlot);
            WeekdayHeader.Measure(headerSlot);
            var used = NavRow.DesiredSize.Height + WeekdayHeader.DesiredSize.Height + 10;
            var gridHeight = Math.Max(24, height - used);
            var cell = Math.Min(cellWidth, gridHeight / 6.0);
            var dayFont = Math.Max(8, Math.Floor(cell * 0.5));
            var inset = Math.Max(1, Math.Round(cell * 0.1));
            foreach (var day in _days)
            {
                day.Label.FontSize = dayFont;
                day.Badge.Margin = new Thickness(inset);
            }

            _fitKey = key;
        }
        finally
        {
            MonthTitle.Text = visibleTitle;
            _fitting = false;
        }
    }

    bool HeaderFits(double width, Windows.Foundation.Size infinite)
    {
        MonthBackButton.Measure(infinite);
        MonthTitle.Measure(infinite);
        MonthForwardButton.Measure(infinite);
        var monthWidth = MonthBackButton.DesiredSize.Width + MonthTitle.DesiredSize.Width + MonthForwardButton.DesiredSize.Width + 4;
        return monthWidth <= width;
    }

    void ApplyDateSize(double dateSize)
    {
        DateText.FontSize = dateSize;
        var day = Math.Max(8, Math.Round(dateSize * 0.46, MidpointRounding.AwayFromZero));
        if (day > dateSize)
        {
            day = dateSize;
        }

        DayText.FontSize = day;
        DayText.Margin = new Thickness(0, Math.Max(2, dateSize * 0.08), 0, 0);
    }

    void ApplyNavSize(double titleSize)
    {
        MonthTitle.FontSize = titleSize;
        MonthBackText.FontSize = titleSize;
        MonthForwardText.FontSize = titleSize;
    }

    void ApplyFont(FontFamily font)
    {
        DateText.FontFamily = font;
        DayText.FontFamily = font;
        MonthTitle.FontFamily = font;
        foreach (var label in _weekdayLabels)
        {
            if (label is not null)
            {
                label.FontFamily = font;
            }
        }

        foreach (var day in _days)
        {
            if (day is not null)
            {
                day.Label.FontFamily = font;
            }
        }
    }

    void BuildCalendar()
    {
        AddColumns(WeekdayHeader);
        AddColumns(DayHost);
        for (var row = 0; row < 6; row++)
        {
            DayHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        }

        for (var column = 0; column < 7; column++)
        {
            var label = new TextBlock
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Foreground = _weekdayBrush,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
                MaxLines = 1,
                Text = " "
            };
            Grid.SetColumn(label, column);
            WeekdayHeader.Children.Add(label);
            _weekdayLabels[column] = label;
        }

        for (var i = 0; i < _days.Length; i++)
        {
            var badge = new Viewbox
            {
                Visibility = Visibility.Collapsed,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Child = new Ellipse
                {
                    Width = 16,
                    Height = 16,
                    Fill = _badgeBrush
                }
            };
            var label = new TextBlock
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Foreground = _dayBrush,
                Text = " "
            };
            var cell = new Grid();
            cell.Children.Add(badge);
            cell.Children.Add(label);
            Grid.SetColumn(cell, i % 7);
            Grid.SetRow(cell, i / 7);
            DayHost.Children.Add(cell);
            _days[i] = new DayCell(badge, label);
        }
    }

    static void AddColumns(Grid grid)
    {
        for (var column = 0; column < 7; column++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }
    }

    void ApplyWeekdayLabels(double cellWidth)
    {
        var culture = CultureInfo.CurrentCulture;
        var start = (int)culture.DateTimeFormat.FirstDayOfWeek;
        var abbreviated = culture.DateTimeFormat.AbbreviatedDayNames;
        var shortest = culture.DateTimeFormat.ShortestDayNames;
        if (Widest(abbreviated) <= cellWidth)
        {
            return;
        }

        Widest(shortest);

        double Widest(string[] names)
        {
            double widest = 0;
            var infinite = new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity);
            for (var column = 0; column < 7; column++)
            {
                var label = _weekdayLabels[column];
                label.Text = names[(start + column) % 7];
                label.Measure(infinite);
                widest = Math.Max(widest, label.DesiredSize.Width);
            }

            return widest;
        }
    }

    void FillDays(DateTime today)
    {
        var start = (int)CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
        var shift = ((int)_month.DayOfWeek - start + 7) % 7;
        var first = _month.AddDays(-shift);
        for (var i = 0; i < _days.Length; i++)
        {
            var date = first.AddDays(i);
            var cell = _days[i];
            var isToday = date.Date == today;
            var inMonth = date.Month == _month.Month && date.Year == _month.Year;
            cell.Label.Text = date.Day.ToString(CultureInfo.CurrentCulture);
            cell.Badge.Visibility = isToday ? Visibility.Visible : Visibility.Collapsed;
            cell.Label.Foreground = isToday ? _inkBrush : _dayBrush;
            cell.Label.FontWeight = isToday ? FontWeights.SemiBold : FontWeights.Normal;
            cell.Label.Opacity = isToday || inMonth ? 1 : 0.35;
        }
    }

    static void SetContentBrush(Button button, Color color)
    {
        if (button.Content is TextBlock text)
        {
            text.Foreground = new SolidColorBrush(color);
        }
    }

    static string FormatDate(DateTime time, string? pattern)
    {
        var used = string.IsNullOrWhiteSpace(pattern) ? "MMM dd, yyyy" : pattern;
        try
        {
            return time.ToString(used, CultureInfo.CurrentCulture);
        }
        catch (FormatException)
        {
            return time.ToString("MMM dd, yyyy", CultureInfo.CurrentCulture);
        }
    }

    static string WidestDate(string? pattern)
    {
        var best = "";
        void Consider(DateTime date)
        {
            var text = FormatDate(date, pattern);
            if (text.Length > best.Length)
            {
                best = text;
            }
        }

        for (var month = 1; month <= 12; month++)
        {
            Consider(new DateTime(2028, month, DateTime.DaysInMonth(2028, month)));
        }

        var september = new DateTime(2028, 9, 1);
        for (var day = 0; day < 7; day++)
        {
            Consider(september.AddDays(day));
        }

        return best;
    }

    static string WidestMonthTitle(int year)
    {
        var culture = CultureInfo.CurrentCulture;
        var best = "";
        for (var month = 1; month <= 12; month++)
        {
            var text = new DateTime(year, month, 1).ToString("MMMM yyyy", culture);
            if (text.Length > best.Length)
            {
                best = text;
            }
        }

        return best;
    }

    static string LongestWeekday()
    {
        var names = CultureInfo.CurrentCulture.DateTimeFormat.DayNames;
        return names.OrderByDescending(name => name?.Length ?? 0).FirstOrDefault() ?? "Wednesday";
    }

    sealed class DayCell(Viewbox badge, TextBlock label)
    {
        public Viewbox Badge { get; } = badge;
        public TextBlock Label { get; } = label;
    }
}
