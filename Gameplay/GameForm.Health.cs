namespace Dust;

internal sealed partial class GameForm
{
    private TimeSpan _failedTime;
    private bool _cargoLostOnFailure;

    private int RemainingHealth => Math.Max(0, GetMaximumHealth() - _damageTaken);

    private void EnterFailure()
    {
        if (_mode != ScreenMode.Playing) return;
        CloseMissionDossier(playSound: false);
        ResetMissionDossier();
        _failedTime = DateTime.Now - _startedAt;
        _mode = ScreenMode.Failed;
        _failurePending = false;
        _pendingWin = false;
        _againButton = RectangleF.Empty;
        _menuButton = RectangleF.Empty;
        _invulnerability = 0;
        _warningFlash = 0;
        RecordAchievementFailure();
        _audio.StopMusic();
        ResetHover();
    }

    private void RestartAfterFailure()
    {
        StartGame(preserveLevel: true);
    }
}
