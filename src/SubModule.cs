using System;
using SandBox;
using SandBox.View.Map;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.Core;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace CPRB.Bannerlord
{
    // Colored Political Regions & Borders: every region of the campaign map takes the colour of the
    // kingdom (or clan) that owns its settlement, with dark borders and a glow of each side's colour.
    // Zoomed out: political map. Zoomed in: normal terrain, the borders stay.
    // Ctrl + 1 on the campaign map: colour strength in percent, saved in the settings file.
    public sealed class SubModule : MBSubModuleBase
    {
        public const string Version = "1.0.1";
        public const string Title = "Colored Political Regions & Borders";

        private static SubModule _instance;
        private Settings _settings;
        private MapOverlay _overlay;
        private MapScene _overlayScene;
        private bool _disabled;
        private int _errors;
        private bool _announced;

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            _instance = this;
            Log.Start();
            _settings = Settings.Load();
            Log.Info("settings " + Settings.FilePath + ": opacity " + _settings.Opacity + " %");
        }

        public override void OnGameEnd(Game game)
        {
            base.OnGameEnd(game);
            // the map scene goes away with the game; its decals with it
            _overlay = null;
            _overlayScene = null;
        }

        protected override void OnApplicationTick(float dt)
        {
            base.OnApplicationTick(dt);
            if (_disabled)
                return;
            try
            {
                Tick(dt);
            }
            catch (Exception e)
            {
                Log.Error("tick", e);
                if (++_errors >= 5)
                    Disable("too many errors");
            }
        }

        private void Tick(float dt)
        {
            Campaign campaign = Campaign.Current;
            if (campaign == null)
            {
                _overlay = null;
                _overlayScene = null;
                return;
            }
            // In a battle (or any other mission) the map is not drawn, and before a battle the game frees
            // the map's GPU memory. The overlay waits and makes its meshes again when the map is back.
            if (Mission.Current != null)
            {
                if (_overlay != null)
                    _overlay.OnMission();
                return;
            }
            var mapScene = campaign.MapSceneWrapper as MapScene;
            if (mapScene == null || mapScene.Scene == null || MapScreen.Instance == null)
                return;
            if (_overlay == null || !ReferenceEquals(_overlayScene, mapScene))
            {
                Log.Info("campaign map found");
                _overlay = new MapOverlay(_settings, mapScene);
                _overlayScene = mapScene;
            }
            _overlay.Tick(dt);
            HandleHotkey();
        }

        // Ctrl + 1 on the campaign map. (1 alone pauses the game, which is fine while the window is open.)
        private void HandleHotkey()
        {
            if (!Input.IsKeyPressed(InputKey.D1))
                return;
            if (!Input.IsKeyDown(InputKey.LeftControl) && !Input.IsKeyDown(InputKey.RightControl))
                return;
            Game game = Game.Current;
            if (game == null || game.GameStateManager == null || !(game.GameStateManager.ActiveState is MapState))
                return;
            if (InformationManager.IsAnyInquiryActive())
                return;
            InformationManager.ShowTextInquiry(new TextInquiryData(
                Title,
                "Map colour strength in percent (0 - 100).\nNow: " + _settings.Opacity + " %",
                true, true, "OK", "Cancel",
                text =>
                {
                    int value;
                    if (TryPercent(text, out value))
                        SetOpacity(value);
                },
                null,
                false,
                text =>
                {
                    int value;
                    bool ok = TryPercent(text, out value);
                    return new Tuple<bool, string>(ok, ok ? "" : "Enter a number from 0 to 100");
                },
                "",
                _settings.Opacity.ToString()), true);
        }

        private static bool TryPercent(string text, out int value)
        {
            value = 0;
            if (text == null)
                return false;
            string t = text.Trim().TrimEnd('%').Trim();
            return int.TryParse(t, out value) && value >= 0 && value <= 100;
        }

        private void SetOpacity(int value)
        {
            _settings.Opacity = value;
            _settings.Save();
            if (_overlay != null)
                _overlay.Refresh();
            Log.Info("opacity " + value + " %");
            Message("Map colour strength: " + value + " %");
        }

        public static void Message(string text)
        {
            InformationManager.DisplayMessage(new InformationMessage("CPR&B: " + text, Color.FromUint(0xFFE8D27Au)));
        }

        // first time the map is ready in this session
        public static void Announce()
        {
            if (_instance == null || _instance._announced)
                return;
            _instance._announced = true;
            Message("political map ready. Ctrl + 1: colour strength.");
        }

        public static void Disable(string reason)
        {
            Log.Info("switched off: " + reason);
            if (_instance == null)
                return;
            _instance._disabled = true;
            _instance._overlay = null;
            try
            {
                Message("switched off (" + reason + "). Details: " + (Log.Path ?? "no log"));
            }
            catch
            {
            }
        }
    }
}
