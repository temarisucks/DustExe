namespace Dust;

internal sealed partial class GameForm
{
    private int _menuMusicGeneration;

    private static bool IsMenuFamilyMode(ScreenMode mode) => mode is
        ScreenMode.TutorialOffer or ScreenMode.Tutorial or
        ScreenMode.Title or ScreenMode.RunSettings or ScreenMode.Customize or
        ScreenMode.Settings or ScreenMode.Achievements or ScreenMode.OnlineAccount or
        ScreenMode.LobbyBrowser or ScreenMode.LobbyRoom;

    private void RequestMenuMusic()
    {
        if (IsDisposed || !IsMenuFamilyMode(_mode)) return;
        var generation = ++_menuMusicGeneration;
        _ = PrepareMenuMusicForModeAsync(generation);
    }

    private async Task PrepareMenuMusicForModeAsync(int generation)
    {
        var ready = await _audio.PrepareMenuMusicAsync();
        if (!ready || IsDisposed || generation != _menuMusicGeneration ||
            !IsMenuFamilyMode(_mode)) return;
        _audio.PlayMenuMusic();
    }

    private void StopMusicForGameTransition()
    {
        _menuMusicGeneration++;
        _audio.StopMusic();
    }
}
