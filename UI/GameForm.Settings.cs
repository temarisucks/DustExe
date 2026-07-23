namespace Dust;

internal sealed partial class GameForm
{
    private void ApplyDisplaySettings(bool recenter = true)
    {
        _settings.Normalize();
        _audio.Volume = _settings.Volume;

        SuspendLayout();
        try
        {
            WindowState = FormWindowState.Normal;
            FormBorderStyle = FormBorderStyle.None;

            if (_settings.Fullscreen)
            {
                var reference = IsUsableBounds(_windowedBounds) ? _windowedBounds : Bounds;
                var screen = IsUsableBounds(reference)
                    ? Screen.FromRectangle(reference)
                    : Screen.FromControl(this);
                Bounds = screen.Bounds;
                return;
            }

            var resolution = SettingsCatalog.Resolutions[_settings.ResolutionIndex];
            var referenceBounds = IsUsableBounds(_windowedBounds) ? _windowedBounds : Bounds;
            var targetScreen = IsUsableBounds(referenceBounds)
                ? Screen.FromRectangle(referenceBounds)
                : Screen.FromControl(this);
            var workArea = targetScreen.WorkingArea;

            var minimumWidth = Math.Min(MinimumSize.Width, workArea.Width);
            var minimumHeight = Math.Min(MinimumSize.Height, workArea.Height);
            var width = Math.Clamp(resolution.Width, minimumWidth, workArea.Width);
            var height = Math.Clamp(resolution.Height, minimumHeight, workArea.Height);

            int x;
            int y;
            if (recenter || !IsUsableBounds(referenceBounds))
            {
                x = workArea.Left + (workArea.Width - width) / 2;
                y = workArea.Top + (workArea.Height - height) / 2;
            }
            else
            {
                // Keep the old window center when a different resolution was
                // selected while fullscreen, and restore exactly when it was not.
                x = referenceBounds.Left + (referenceBounds.Width - width) / 2;
                y = referenceBounds.Top + (referenceBounds.Height - height) / 2;
            }

            Bounds = ClampToWorkArea(new Rectangle(x, y, width, height), workArea);
            _windowedBounds = Bounds;
        }
        finally
        {
            ResumeLayout(performLayout: true);
        }
    }

    private void ToggleFullscreen()
    {
        if (!_settings.Fullscreen)
        {
            var candidate = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            if (IsUsableBounds(candidate))
                _windowedBounds = candidate;
        }

        _settings.Fullscreen = !_settings.Fullscreen;
        ApplyDisplaySettings(recenter: false);
        SaveSettings();
        Invalidate();
    }

    private void AdjustSetting(int row, int direction)
    {
        var step = Math.Sign(direction);
        if (step == 0) return;

        switch (row)
        {
            case 0:
                _settings.Brightness = Math.Clamp(_settings.Brightness + step * 10, 50, 150);
                break;

            case 1:
                _settings.Volume = Math.Clamp(_settings.Volume + step * 10, 0, 100);
                _audio.Volume = _settings.Volume;
                break;

            case 2:
            {
                var count = SettingsCatalog.Resolutions.Length;
                if (count == 0) return;
                var current = Math.Clamp(_settings.ResolutionIndex, 0, count - 1);
                _settings.ResolutionIndex = (current + step + count) % count;
                if (!_settings.Fullscreen)
                    ApplyDisplaySettings();
                break;
            }

            case 3:
                ToggleFullscreen();
                return;

            default:
                return;
        }

        SaveSettings();
        Invalidate();
    }

    private void SaveSettings()
    {
        _settings.SetDroneCustomization(_drone, _playerColor, _playerFrameColor);
        _settings.Normalize();
        _audio.Volume = _settings.Volume;
        GameSettingsStore.Save(_settings);
    }

    private void QueueSettingsSave()
    {
        _settings.SetDroneCustomization(_drone, _playerColor, _playerFrameColor);
        _settings.Normalize();
        _audio.Volume = _settings.Volume;
        GameSettingsStore.QueueSave(_settings);
    }

    private void DrawBrightnessOverlay(Graphics g)
    {
        var brightness = Math.Clamp(_settings.Brightness, 50, 150);
        if (brightness == 100) return;

        Color tint;
        if (brightness < 100)
        {
            var strength = (100 - brightness) / 50f;
            tint = Color.FromArgb((int)MathF.Round(145 * strength), Color.Black);
        }
        else
        {
            var strength = (brightness - 100) / 50f;
            tint = Color.FromArgb((int)MathF.Round(72 * strength), 239, 235, 202);
        }

        using var overlay = new SolidBrush(tint);
        g.FillRectangle(overlay, 0, 0, DesignWidth, DesignHeight);
    }

    private static bool IsUsableBounds(Rectangle bounds) => bounds.Width > 0 && bounds.Height > 0;

    private static Rectangle ClampToWorkArea(Rectangle bounds, Rectangle workArea)
    {
        var width = Math.Min(Math.Max(1, bounds.Width), workArea.Width);
        var height = Math.Min(Math.Max(1, bounds.Height), workArea.Height);
        var maxX = workArea.Right - width;
        var maxY = workArea.Bottom - height;
        var x = Math.Clamp(bounds.X, workArea.Left, maxX);
        var y = Math.Clamp(bounds.Y, workArea.Top, maxY);
        return new Rectangle(x, y, width, height);
    }
}
