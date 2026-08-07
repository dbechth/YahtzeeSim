using YahtzeeSim.Maui.Engine;

namespace YahtzeeSim.Maui;

public partial class MainPage : ContentPage
{
    private readonly YahtzeeGameEngine _engine = new();
    private Dictionary<ScoreCategory, double> _probabilities = new();
    private string _currentPenColorHex = "#1D2939";
    private bool _isInitializing;
    private TaskCompletionSource<string?>? _colorPickerTcs;
    private double _scoreFontSize = 12;
    private double _headerFontSize = 12;
    private double _diceFontSize = 40;
    private double _diceSize = 72;
    private double _lastLayoutWidth;
    private bool _isGameStarted;
    private bool _isRollAnimating;
    private bool _scoreGridRefreshQueued;

    public MainPage()
    {
        InitializeComponent();
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (width <= 0 || height <= 0)
        {
            return;
        }

        bool tinyScreen = width < 680 || height < 760;
        ScoreScroll.Orientation = tinyScreen ? ScrollOrientation.Vertical : ScrollOrientation.Neither;
        ScoreScroll.VerticalScrollBarVisibility = tinyScreen ? ScrollBarVisibility.Default : ScrollBarVisibility.Never;
        ScoreScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Never;

        double nextScoreFont = Math.Clamp(width / 110d, 9d, 15d);
        double nextHeaderFont = Math.Clamp(nextScoreFont + 0.8d, 10d, 16d);
        double nextDiceSize = Math.Clamp(width / 6.1d, 66d, 122d);
        double nextDiceFont = Math.Clamp(nextDiceSize * 0.85d, 40d, 86d);

        bool narrowTopBar = width < 420;
        AppTitleLabel.FontSize = narrowTopBar ? 20 : 24;
        TopBarGrid.ColumnSpacing = narrowTopBar ? 5 : 8;
        StartGameButton.HeightRequest = narrowTopBar ? 38 : 42;
        StartGameButton.Padding = narrowTopBar ? new Thickness(8, 0) : new Thickness(12, 0);

        bool changed = Math.Abs(_scoreFontSize - nextScoreFont) > 0.2d
            || Math.Abs(_headerFontSize - nextHeaderFont) > 0.2d
            || Math.Abs(_diceFontSize - nextDiceFont) > 0.2d
            || Math.Abs(_diceSize - nextDiceSize) > 0.2d;

        _scoreFontSize = nextScoreFont;
        _headerFontSize = nextHeaderFont;
        _diceSize = nextDiceSize;
        _diceFontSize = nextDiceFont;

        ApplyDiceSizing();
        bool layoutWidthChanged = Math.Abs(_lastLayoutWidth - width) > 1;
        _lastLayoutWidth = width;

        if (changed || layoutWidthChanged)
        {
            RequestScoreGridRefresh();
        }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (_isInitializing)
        {
            return;
        }

        _isInitializing = true;
        _engine.Reset(GameMode.Single);
        _isGameStarted = false;
        RefreshAll(recalculateProbabilities: false);
        RequestScoreGridRefresh();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _isRollAnimating = false;
        // Ensure dice are restored to normal state if page closes during roll
        RefreshDice();
    }

    private async void StartGameButton_Clicked(object? sender, EventArgs e)
    {
        string modeSelection = await DisplayActionSheet("Choose Game Mode", "Cancel", null, "Single", "Full Sheet");
        if (modeSelection == "Cancel" || string.IsNullOrWhiteSpace(modeSelection))
        {
            return;
        }

        var selectedMode = modeSelection == "Full Sheet" ? GameMode.FullSheet : GameMode.Single;
        _engine.Reset(selectedMode);
        _isGameStarted = true;
        StartGameButton.Text = "New Game";

        RefreshAll(recalculateProbabilities: false);
        await PromptForPenColorAsync("Pick Pen Color", false);
        RefreshAll(recalculateProbabilities: true);
    }

    private void DieButton_Clicked(object? sender, EventArgs e)
    {
        if (!_isGameStarted)
        {
            return;
        }

        if (sender is not Button button || button.CommandParameter is not string rawId || !int.TryParse(rawId, out int dieId))
        {
            return;
        }

        if (_engine.ToggleHold(dieId))
        {
            // Keep hold feedback instant; probabilities and score table update on roll/score boundaries.
            RefreshDice();
        }
    }

    private void RollButton_Pressed(object? sender, EventArgs e)
    {
        if (!_isGameStarted || !_engine.CanRoll)
        {
            return;
        }

        // Blank out non-held dice to indicate roll is about to happen
        var buttons = new[] { Die1Button, Die2Button, Die3Button, Die4Button, Die5Button };
        for (int i = 0; i < buttons.Length; i++)
        {
            var die = _engine.Dice[i];
            var button = buttons[i];

            if (!die.IsHeld)
            {
                // Blank out the die and use a neutral color
                button.Text = string.Empty;
                button.BackgroundColor = Color.FromArgb("#D1D5DB");
                button.BorderColor = Color.FromArgb("#6B7280");
            }
        }
    }

    private void RollButton_Released(object? sender, EventArgs e)
    {
        if (!_isGameStarted || !_engine.CanRoll)
        {
            return;
        }

        // Execute the roll
        if (_engine.RollDice())
        {
            RefreshAll(recalculateProbabilities: true);
        }
        else
        {
            RefreshDice();
            RefreshStatus();
        }
    }

    private void UndoButton_Clicked(object? sender, EventArgs e)
    {
        if (!_isGameStarted)
        {
            return;
        }

        if (_engine.Undo())
        {
            RefreshAll(recalculateProbabilities: true);
        }
    }

    private async Task OnScoreClickedAsync(ScoreCategory category, int gameIndex)
    {
        if (!_isGameStarted)
        {
            return;
        }

        if (!_engine.IsCardEnabled(gameIndex))
        {
            return;
        }

        if (_engine.RollCount <= 0)
        {
            await DisplayAlert("Not Ready", "Roll at least once before selecting a score cell.", "OK");
            return;
        }

        if (_engine.TryApplyScore(gameIndex, category, _currentPenColorHex, out bool wasScratch, out _, out bool wasYahtzee))
        {
            if (wasScratch)
            {
                await DisplayAlert("Scratch", "That category did not meet minimum rules, so it was recorded as X (stored as 0).", "OK");
            }

            if (wasYahtzee)
            {
                await DisplayAlert("Yahtzee!", "YAHTZEE rolled. Pick a pen color for your next entry.", "OK");
                await PromptForPenColorAsync("Pick Pen Color", true);
            }

            RefreshAll(recalculateProbabilities: true);
        }
        else if (!_engine.AwaitingScoreSelection)
        {
            await DisplayAlert("Not Valid Yet", "That category is not valid yet. Keep rolling or choose a valid category to end early.", "OK");
        }
    }

    private async Task PromptForPenColorAsync(string title, bool allowKeepCurrent)
    {
        ColorPickerTitle.Text = title;
        KeepColorButton.IsVisible = allowKeepCurrent;

        _colorPickerTcs = new TaskCompletionSource<string?>();
        ColorPickerOverlay.IsVisible = true;

        string? selection = await _colorPickerTcs.Task;
        ColorPickerOverlay.IsVisible = false;

        if (!string.IsNullOrWhiteSpace(selection))
        {
            _currentPenColorHex = selection;
        }
    }

    private void ColorSwatch_Clicked(object? sender, EventArgs e)
    {
        if (sender is Button button && button.CommandParameter is string hex)
        {
            _colorPickerTcs?.TrySetResult(hex);
        }
    }

    private void KeepColorButton_Clicked(object? sender, EventArgs e)
    {
        _colorPickerTcs?.TrySetResult(null);
    }

    private void RefreshAll(bool recalculateProbabilities)
    {
        if (_isGameStarted && recalculateProbabilities && _engine.RollCount > 0)
        {
            _probabilities = _engine.CalculateProbabilities();
        }
        else
        {
            _probabilities = ScoreRules.ScoringCategories.ToDictionary(c => c, _ => 0d);
        }

        RefreshDice();
        RefreshStatus();
        RequestScoreGridRefresh();
    }

    private void RequestScoreGridRefresh()
    {
        if (_scoreGridRefreshQueued)
        {
            return;
        }

        _scoreGridRefreshQueued = true;
        _ = RefreshScoreGridAfterLayoutAsync();
    }

    private async Task RefreshScoreGridAfterLayoutAsync()
    {
        try
        {
            await Task.Delay(40);
            BuildScoreGrid();
        }
        finally
        {
            _scoreGridRefreshQueued = false;
        }
    }

    private void RefreshStatus()
    {
        if (!_isGameStarted)
        {
            StatusLabel.Text = "Tap New Game to choose Single or Full Sheet.";
        }
        else
        {
            string modeText = _engine.Mode == GameMode.FullSheet ? "Full Sheet" : "Single";
            StatusLabel.Text = _engine.RollCount switch
            {
                0 => $"{modeText}: Roll to start your turn",
                _ when _engine.AwaitingScoreSelection => "Roll 3 reached: pick any score cell",
                _ => $"{modeText}: Roll {_engine.RollCount} of {YahtzeeGameEngine.RollsPerTurn}. Pick a valid category to end early or roll again"
            };
        }

        RollButton.IsEnabled = _isGameStarted && _engine.CanRoll;

        UndoButton.IsEnabled = _isGameStarted && _engine.CanUndo;
        RefreshEndSummary();
    }

    private void RefreshDice()
    {
        var buttons = new[] { Die1Button, Die2Button, Die3Button, Die4Button, Die5Button };

        for (int i = 0; i < buttons.Length; i++)
        {
            var die = _engine.Dice[i];
            var button = buttons[i];
            button.Text = die.Value <= 0 ? string.Empty : ToDieGlyph(die.Value);
            button.BackgroundColor = die.IsHeld ? Color.FromArgb("#F4A261") : Color.FromArgb("#E9F5DB");
            button.TextColor = Color.FromArgb("#1F2937");
            button.BorderColor = die.IsHeld ? Color.FromArgb("#B45309") : Color.FromArgb("#7D8F69");
            button.BorderWidth = die.IsHeld ? 3 : 1;
            button.IsEnabled = _isGameStarted && _engine.RollCount > 0 && !_engine.AwaitingScoreSelection && !_isRollAnimating;
        }

        ApplyDiceSizing();
    }

    private void ApplyDiceSizing()
    {
        var buttons = new[] { Die1Button, Die2Button, Die3Button, Die4Button, Die5Button };
        foreach (var button in buttons)
        {
            button.HeightRequest = _diceSize;
            button.FontSize = _diceFontSize;
            // Use negative top padding to vertically center the glyph within the button
            button.Padding = DeviceInfo.Platform == DevicePlatform.Android
                ? new Thickness(0, -4, 0, 0)
                : new Thickness(0, -2, 0, 0);
        }
    }

    private static string ToDieGlyph(int value)
    {
        return value switch
        {
            1 => "⚀",
            2 => "⚁",
            3 => "⚂",
            4 => "⚃",
            5 => "⚄",
            6 => "⚅",
            _ => string.Empty
        };
    }

    private void BuildScoreGrid()
    {
        ScoreGrid.Children.Clear();
        ScoreGrid.RowDefinitions.Clear();
        ScoreGrid.ColumnDefinitions.Clear();

        double viewportWidth = ScoreScroll.Width > 0 ? ScoreScroll.Width : Width;
        double availableWidth = Math.Max(220, viewportWidth - 8);
        double totalSpacing = ScoreGrid.ColumnSpacing * (YahtzeeGameEngine.GameCount + 1);
        double contentWidth = Math.Max(160, availableWidth - totalSpacing);

        double categoryWidth = Math.Clamp(contentWidth * 0.29, 56, 96);
        double probWidth = Math.Clamp(contentWidth * 0.11, 24, 52);
        double gameWidth = (contentWidth - categoryWidth - probWidth) / YahtzeeGameEngine.GameCount;

        if (gameWidth < 22)
        {
            categoryWidth = 52;
            probWidth = 22;
            gameWidth = (contentWidth - categoryWidth - probWidth) / YahtzeeGameEngine.GameCount;
        }

        gameWidth = Math.Max(20, gameWidth);
        ScoreGrid.WidthRequest = availableWidth;

        ScoreGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(categoryWidth)));
        ScoreGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(probWidth)));
        for (int i = 0; i < YahtzeeGameEngine.GameCount; i++)
        {
            ScoreGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(gameWidth)));
        }

        double interactiveRowHeight = GetInteractiveRowHeight();

        ScoreGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        AddHeaderCell("Category", 0, 0);
        AddHeaderCell("Prob", 0, 1);

        for (int game = 0; game < YahtzeeGameEngine.GameCount; game++)
        {
            AddHeaderCell($"G{game + 1}", 0, game + 2);
        }

        int row = 1;
        row = AddUpperPaceRow(row, interactiveRowHeight);

        foreach (var category in ScoreRules.ScoringCategories)
        {
            ScoreGrid.RowDefinitions.Add(new RowDefinition(new GridLength(interactiveRowHeight)));
            AddCellLabel(ScoreRules.GetDisplayName(category), row, 0, FontAttributes.Bold, "#1F2937");

            string probabilityText = _engine.RollCount > 0 && _probabilities.TryGetValue(category, out var p)
                ? p.ToString("P1")
                : string.Empty;
            AddCellLabel(probabilityText, row, 1, FontAttributes.None, "#475569");

            for (int game = 0; game < YahtzeeGameEngine.GameCount; game++)
            {
                AddScoreButton(row, game + 2, category, game, interactiveRowHeight);
            }

            row++;

            if (category == ScoreRules.UpperCategories.Last())
            {
                ScoreGrid.RowDefinitions.Add(new RowDefinition(new GridLength(4)));
                var separator = new Border
                {
                    BackgroundColor = Color.FromArgb("#7F5539"),
                    StrokeThickness = 0,
                    Margin = new Thickness(0, 2),
                    HorizontalOptions = LayoutOptions.Fill,
                    VerticalOptions = LayoutOptions.Fill
                };

                Grid.SetRow(separator, row);
                Grid.SetColumnSpan(separator, YahtzeeGameEngine.GameCount + 2);
                ScoreGrid.Children.Add(separator);
                row++;
            }
        }

        ScoreGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        AddCellLabel("Upper", row, 0, FontAttributes.Bold, "#1F2937");
        AddCellLabel(string.Empty, row, 1, FontAttributes.None, "#1F2937");
        for (int game = 0; game < YahtzeeGameEngine.GameCount; game++)
        {
            AddCellLabel(_engine.Cards[game].UpperSubtotal?.ToString() ?? "--", row, game + 2, FontAttributes.Bold, "#1F2937");
        }
        row++;

        ScoreGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        AddCellLabel("Lower", row, 0, FontAttributes.Bold, "#1F2937");
        AddCellLabel(string.Empty, row, 1, FontAttributes.None, "#1F2937");
        for (int game = 0; game < YahtzeeGameEngine.GameCount; game++)
        {
            AddCellLabel(_engine.Cards[game].LowerSubtotal?.ToString() ?? "--", row, game + 2, FontAttributes.Bold, "#1F2937");
        }
        row++;

        ScoreGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        AddCellLabel("Bonus", row, 0, FontAttributes.Bold, "#1F2937");
        AddCellLabel(string.Empty, row, 1, FontAttributes.None, "#1F2937");
        for (int game = 0; game < YahtzeeGameEngine.GameCount; game++)
        {
            AddCellLabel(_engine.Cards[game].UpperBonus?.ToString() ?? "--", row, game + 2, FontAttributes.Bold, "#1F2937");
        }
        row++;

        ScoreGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        AddCellLabel("Game Total", row, 0, FontAttributes.Bold, "#1F2937");
        AddCellLabel(string.Empty, row, 1, FontAttributes.None, "#1F2937");
        for (int game = 0; game < YahtzeeGameEngine.GameCount; game++)
        {
            AddCellLabel(_engine.Cards[game].GameTotal?.ToString() ?? "--", row, game + 2, FontAttributes.Bold, "#1F2937");
        }

        int cardTotal = _engine.Cards.Sum(c => c.GameTotal ?? 0);
        bool showCardTotal = _engine.Mode == GameMode.Single
            ? _engine.Cards[0].IsComplete
            : _engine.Cards.All(c => c.IsComplete);

        ScoreGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        AddCellLabel("Card Total", row + 1, 0, FontAttributes.Bold, "#1F2937");
        AddCellLabel(string.Empty, row + 1, 1, FontAttributes.None, "#1F2937");
        AddCellLabel(showCardTotal ? cardTotal.ToString() : "--", row + 1, 2, FontAttributes.Bold, "#1F2937");
        for (int game = 1; game < YahtzeeGameEngine.GameCount; game++)
        {
            AddCellLabel(string.Empty, row + 1, game + 2, FontAttributes.None, "#1F2937");
        }
    }

    private int AddUpperPaceRow(int row, double interactiveRowHeight)
    {
        ScoreGrid.RowDefinitions.Add(new RowDefinition(new GridLength(interactiveRowHeight)));
        AddCellLabel("Upper Pace", row, 0, FontAttributes.Bold, "#1E3A5F", "#E7F0FA");
        AddCellLabel("vs 3x target", row, 1, FontAttributes.None, "#1E3A5F", "#E7F0FA");

        for (int game = 0; game < YahtzeeGameEngine.GameCount; game++)
        {
            int net = ScoreRules.UpperCategories.Sum(c => ScoreRules.GetUpperPaceDelta(_engine.Cards[game], c));
            string sign = net > 0 ? "+" : string.Empty;
            string text = _engine.IsCardEnabled(game) ? $"{sign}{net}" : "--";
            string tone = net > 0 ? "#166534" : net < 0 ? "#991B1B" : "#5C3A21";

            AddCellLabel(text, row, game + 2, FontAttributes.Bold, tone, "#E7F0FA");
        }

        return row + 1;
    }

    private double GetInteractiveRowHeight()
    {
        double available = ScoreScroll.Height;
        if (available <= 0)
        {
            return Math.Max(42, _scoreFontSize * 2.7);
        }

        int interactiveRows = 1 + ScoreRules.ScoringCategories.Count;
        double computed = (available - 40) / interactiveRows;
        return Math.Clamp(computed, 42, 64);
    }

    private void RefreshEndSummary()
    {
        bool allDone = _engine.Mode == GameMode.FullSheet && _engine.Cards.All(c => c.IsComplete);
        EndSummaryPanel.IsVisible = allDone;

        if (!allDone)
        {
            return;
        }

        var lines = new List<string>();
        for (int i = 0; i < YahtzeeGameEngine.GameCount; i++)
        {
            var card = _engine.Cards[i];
            lines.Add($"Game {i + 1}: U {card.UpperSubtotal ?? 0}, L {card.LowerSubtotal ?? 0}, B {card.UpperBonus ?? 0}, T {card.GameTotal ?? 0}");
        }

        int cardTotal = _engine.Cards.Sum(c => c.GameTotal ?? 0);
        lines.Add($"Series Card Total: {cardTotal}");
        EndSummaryText.Text = string.Join("\n", lines);
    }

    private void AddScoreButton(int row, int column, ScoreCategory category, int gameIndex, double rowHeight)
    {
        var cell = _engine.Cards[gameIndex].Scores[category];
        bool enabled = _isGameStarted && _engine.CanSelectCategory(gameIndex, category);

        // Check if this category has a valid score with current dice
        bool isValid = false;
        if (_engine.RollCount > 0 && !cell.IsFilled)
        {
            var diceValues = _engine.Dice.Select(d => d.Value).ToArray();
            var evaluation = ScoreRules.EvaluateCategory(category, diceValues);
            isValid = evaluation.IsValid;
        }

        Color background;
        Color textColor;
        Color borderColor;

        if (cell.IsFilled)
        {
            background = Color.FromArgb(cell.PenColorHex);
            textColor = GetContrastingTextColor(cell.PenColorHex);
            borderColor = enabled ? Color.FromArgb("#1F2937") : Color.FromArgb("#374151");
        }
        else
        {
            // Show green only if the category has a valid score; otherwise gray
            background = isValid ? Color.FromArgb("#DCFCE7") : Color.FromArgb("#E5E7EB");
            textColor = Color.FromArgb("#1F2937");
            borderColor = isValid ? Color.FromArgb("#22C55E") : Color.FromArgb("#9CA3AF");
        }

        var button = new Button
        {
            Text = cell.DisplayText,
            Padding = new Thickness(0),
            Margin = new Thickness(0),
            MinimumHeightRequest = 0,
            MinimumWidthRequest = 0,
            WidthRequest = -1,
            HeightRequest = Math.Max(38, rowHeight - 2),
            FontSize = _scoreFontSize,
            BackgroundColor = background,
            TextColor = textColor,
            BorderColor = borderColor,
            BorderWidth = enabled ? 2 : 1,
            IsEnabled = enabled
        };

        button.Clicked += async (_, _) => await OnScoreClickedAsync(category, gameIndex);

        Grid.SetRow(button, row);
        Grid.SetColumn(button, column);
        ScoreGrid.Children.Add(button);
    }

    private void AddHeaderCell(string text, int row, int column)
    {
        var border = new Border
        {
            Stroke = Color.FromArgb("#6B7280"),
            StrokeThickness = 1,
            BackgroundColor = Color.FromArgb("#E5E7EB"),
            Padding = new Thickness(3, 1),
            Content = new Label
            {
                Text = text,
                FontAttributes = FontAttributes.Bold,
                FontSize = _headerFontSize,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center,
                TextColor = Color.FromArgb("#111827")
            }
        };

        Grid.SetRow(border, row);
        Grid.SetColumn(border, column);
        ScoreGrid.Children.Add(border);
    }

    private void AddCellLabel(string text, int row, int column, FontAttributes attributes, string colorHex, string backgroundHex = "#FFFBEB")
    {
        var border = new Border
        {
            Stroke = Color.FromArgb("#D1D5DB"),
            StrokeThickness = 1,
            BackgroundColor = Color.FromArgb(backgroundHex),
            Padding = new Thickness(column == 0 ? 3 : 2, 1),
            Content = new Label
            {
                Text = text,
                FontAttributes = attributes,
                FontSize = _scoreFontSize,
                TextColor = Color.FromArgb(colorHex),
                HorizontalTextAlignment = column == 0 ? TextAlignment.Start : TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center
            }
        };

        Grid.SetRow(border, row);
        Grid.SetColumn(border, column);
        ScoreGrid.Children.Add(border);
    }

    private static Color GetContrastingTextColor(string backgroundHex)
    {
        var color = Color.FromArgb(backgroundHex);

        // Relative luminance approximation for readable foreground selection.
        double luminance = (0.299 * color.Red) + (0.587 * color.Green) + (0.114 * color.Blue);
        return luminance > 0.55 ? Color.FromArgb("#111827") : Color.FromArgb("#FFFFFF");
    }
}
