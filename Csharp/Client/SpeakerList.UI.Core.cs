using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Barotrauma;
using Barotrauma.Networking;
using Barotrauma.Sounds;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Test
{
#if CLIENT
    internal enum VoiceBarDisplayMode
    {
        Current,
        Enhanced
    }

    internal static partial class VoiceChatUI
    {
        private static readonly Dictionary<Client, VoiceBar> activeVoiceBars = new Dictionary<Client, VoiceBar>();
        private static readonly Dictionary<Client, GUIButton> clickButtons = new Dictionary<Client, GUIButton>(); // Invisible click buttons
        private static GUIFrame buttonContainer; // Container for buttons above all elements
        private static GUIButton settingsButton;
        private static GUIFrame settingsMenuRoot;
        private static GUITextBox settingsPathTextBox;
        private static GUIButton settingsDisplayModeButton;
        private static GUITextBlock settingsDisplayModeButtonLabel;
        private static GUIFrame settingsDisplayModeList;
        private static GUIButton settingsDisplayModeCurrentButton;
        private static GUIButton settingsDisplayModeEnhancedButton;
        private static bool isSettingsDisplayModeListOpen;
        private static readonly Dictionary<string, float> savedVolumes = new Dictionary<string, float>(); // Key: AccountID or SessionId as string
        private static readonly Dictionary<Client, float> lastKnownVolumes = new Dictionary<Client, float>(); // Track volume changes
        private static VoiceBarDisplayMode displayMode = VoiceBarDisplayMode.Current;
        private static string settingsPath = string.Empty;
        private static string legacySettingsPath = string.Empty;
        private static bool loadedFromLegacyPath;
        private static Vector2 hudAnchorNormalized = new Vector2(-1f, -1f);
        private static bool isHudDragPendingStart;
        private static bool isHudDragging;
        private static Point hudDragStartMouse;
        private static Point hudDragStartOrigin;
        private const float VoiceThreshold = 0.01f; // Minimum amplitude for displaying
        private const float CurrentFadeOutTime = 0.9f; // Fade out time for the old mode
        private const float EnhancedFadeOutTime = 2.1f; // Fade out time for the enhanced mode

        // Drawing parameters (same as for the boss bars on the right)
        public const float BarWidth = 150f;   // Bar width
        public const float BarHeight = 20f;   // Bar height
        public const float BarSpacing = 10f;  // Space between bars
        public const float TextHeight = 15f;  // Text height
        public const float RightMargin = 10f; // Margin from the right
        public const float TopMargin = 100f;  // Margin from the top (to not overlap other UI elements)
        private const int SettingsButtonSize = 16;
        private const int SettingsButtonOffsetX = -6; // Tune this to move the side settings icon horizontally.
        private static readonly Color SettingsWindowBg = new Color(15, 15, 15, 242);
        private static readonly Color SettingsHeaderBg = new Color(7, 7, 7, 255);
        private static readonly Color SettingsBorder = new Color(88, 88, 88, 255);
        private static readonly Color SettingsSectionBg = new Color(20, 20, 20, 238);
        private static readonly Color SettingsSectionBorder = new Color(62, 62, 62, 255);
        private static readonly Color SettingsTextMain = new Color(224, 224, 224, 255);
        private static readonly Color SettingsTextDim = new Color(148, 148, 148, 255);
        private static readonly Color SettingsControlBg = new Color(14, 14, 14, 255);
        private static readonly Color SettingsControlHover = new Color(26, 26, 26, 255);

        public static VoiceBarDisplayMode CurrentDisplayMode => displayMode;

        public static float GetCurrentFadeOutTime()
        {
            return displayMode == VoiceBarDisplayMode.Enhanced ? EnhancedFadeOutTime : CurrentFadeOutTime;
        }

        public static void Initialize()
        {
            settingsPath = GetPrimarySettingsPath();
            legacySettingsPath = GetLegacySettingsPath();
            DebugConsole.Log($"VoiceChatMonitor: Settings path = {settingsPath}");
            LoadVolumes();
            
            // Create container for buttons above all elements
            if (GUI.Canvas != null)
            {
                buttonContainer = new GUIFrame(new RectTransform(Vector2.One, GUI.Canvas), style: null)
                {
                    CanBeFocused = false
                };
            }
        }

        public static void Dispose()
        {
            SaveVolumes();
            ClearAllBars();
        }

        private static bool IsNormalizedCoordValid(float value) => value >= 0f && value <= 1f;

        private static Point GetDefaultHudOriginPx()
        {
            float screenWidth = GameMain.GraphicsWidth;
            int x = (int)(screenWidth - BarWidth - RightMargin);
            int y = (int)TopMargin;
            return ClampHudOriginToScreen(new Point(x, y));
        }

        private static Point ClampHudOriginToScreen(Point origin)
        {
            int screenWidth = Math.Max(1, GameMain.GraphicsWidth);
            int screenHeight = Math.Max(1, GameMain.GraphicsHeight);
            int maxX = Math.Max(0, screenWidth - (int)BarWidth - SettingsButtonSize - 8);
            int maxY = Math.Max(0, screenHeight - (int)(BarHeight + TextHeight + 8));
            int clampedX = Math.Clamp(origin.X, 0, maxX);
            int clampedY = Math.Clamp(origin.Y, 0, maxY);
            return new Point(clampedX, clampedY);
        }

        private static Point GetHudOriginPx()
        {
            if (!IsNormalizedCoordValid(hudAnchorNormalized.X) || !IsNormalizedCoordValid(hudAnchorNormalized.Y))
            {
                return GetDefaultHudOriginPx();
            }

            int screenWidth = Math.Max(1, GameMain.GraphicsWidth);
            int screenHeight = Math.Max(1, GameMain.GraphicsHeight);
            int x = (int)Math.Round(hudAnchorNormalized.X * screenWidth);
            int y = (int)Math.Round(hudAnchorNormalized.Y * screenHeight);
            return ClampHudOriginToScreen(new Point(x, y));
        }

        private static void SetHudOriginPx(Point origin, bool persist)
        {
            Point clamped = ClampHudOriginToScreen(origin);
            int screenWidth = Math.Max(1, GameMain.GraphicsWidth);
            int screenHeight = Math.Max(1, GameMain.GraphicsHeight);
            hudAnchorNormalized = new Vector2(
                Math.Clamp(clamped.X / (float)screenWidth, 0f, 1f),
                Math.Clamp(clamped.Y / (float)screenHeight, 0f, 1f));

            if (persist)
            {
                SaveVolumes();
            }
        }

        private static void StartHudMoveMode()
        {
            CloseSettingsMenu();
            isHudDragPendingStart = true;
            isHudDragging = false;
            DebugConsole.Log("VoiceChatMonitor: HUD move mode enabled. Hold and drag with left mouse.");
        }

        private static void UpdateHudDrag()
        {
            if (!isHudDragPendingStart && !isHudDragging) { return; }

            if (isHudDragPendingStart)
            {
                if (PlayerInput.PrimaryMouseButtonHeld())
                {
                    isHudDragPendingStart = false;
                    isHudDragging = true;
                    hudDragStartMouse = PlayerInput.MousePosition.ToPoint();
                    hudDragStartOrigin = GetHudOriginPx();
                }
                return;
            }

            if (!PlayerInput.PrimaryMouseButtonHeld())
            {
                isHudDragging = false;
                SetHudOriginPx(GetHudOriginPx(), persist: true);
                DebugConsole.Log("VoiceChatMonitor: HUD position saved.");
                return;
            }

            Point currentMouse = PlayerInput.MousePosition.ToPoint();
            Point delta = currentMouse - hudDragStartMouse;
            Point targetOrigin = hudDragStartOrigin + delta;
            SetHudOriginPx(targetOrigin, persist: false);
        }

        public static void SetDisplayMode(VoiceBarDisplayMode mode)
        {
            if (displayMode == mode) { return; }
            displayMode = mode;
            float fadeOutTime = GetCurrentFadeOutTime();
            foreach (var bar in activeVoiceBars.Values)
            {
                bar.FadeTimer = Math.Min(bar.FadeTimer, fadeOutTime);
            }
            SaveVolumes();
            DebugConsole.Log($"VoiceChatMonitor: display mode changed to {displayMode}");
            RefreshSettingsMenuControls();
        }

        public static bool IsClientSpeaking(Client client)
        {
            if (client?.VoipSound == null) return false;
            if (!client.VoipSound.IsPlaying) return false;

            float amplitude = client.VoipSound.CurrentAmplitude;
            float gain = client.VoipSound.Gain;
            float categoryGain = GameMain.SoundManager?.GetCategoryGainMultiplier(SoundManager.SoundCategoryVoip) ?? 1.0f;
            float totalVolume = amplitude * gain * categoryGain * client.VoiceVolume;

            return totalVolume > VoiceThreshold;
        }

        public static void Update(float deltaTime)
        {
            if (GameMain.Client?.ConnectedClients == null)
            {
                return;
            }

            UpdateHudDrag();

            // Track volume changes for all connected clients
            foreach (var client in GameMain.Client.ConnectedClients)
            {
                if (client == null) continue;
                
                // Check if volume changed (e.g., through vanilla menu)
                if (lastKnownVolumes.TryGetValue(client, out float lastVolume))
                {
                    if (Math.Abs(client.VoiceVolume - lastVolume) > 0.001f)
                    {
                        // Volume changed - save it
                        SaveVolume(client, client.VoiceVolume);
                        DebugConsole.Log($"Volume changed for {client.Name}: {lastVolume} -> {client.VoiceVolume}");
                    }
                }
                else
                {
                    // First time seeing this client - load saved volume
                    float savedVolume = GetSavedVolume(client);
                    if (Math.Abs(savedVolume - 1.0f) > 0.001f)
                    {
                        client.VoiceVolume = savedVolume;
                        DebugConsole.Log($"Loaded saved volume for {client.Name}: {savedVolume}");
                    }
                    // Don't save here - only save when volume actually changes
                }
                
                // Update last known volume
                lastKnownVolumes[client] = client.VoiceVolume;
            }

            // Update existing bars and remove inactive ones
            var clientsToRemove = new List<Client>();
            foreach (var kvp in activeVoiceBars.ToList())
            {
                var client = kvp.Key;
                var bar = kvp.Value;

                if (client == null || client.VoipSound == null || !IsClientSpeaking(client))
                {
                    bar.FadeTimer -= deltaTime;
                    if (bar.FadeTimer <= 0)
                    {
                        clientsToRemove.Add(client);
                    }
                }
                else
                {
                    bar.FadeTimer = GetCurrentFadeOutTime();
                    bar.Update(deltaTime, client);
                }
            }

            // Remove inactive bars
            foreach (var client in clientsToRemove)
            {
                RemoveBar(client);
            }

            // Add new bars for currently speaking clients
            foreach (var client in GameMain.Client.ConnectedClients)
            {
                if (client == null || client.SessionId == GameMain.Client.SessionId) continue; // Skip self
                if (client.VoipSound == null) continue;
                if (client.Muted || client.MutedLocally) continue;

                bool isSpeaking = IsClientSpeaking(client);
                if (client.VoipSound.IsPlaying && client.VoipSound.CurrentAmplitude > 0.001f)
                {
                    if (isSpeaking && !activeVoiceBars.ContainsKey(client))
                    {
                        AddBar(client);
                    }
                }
            }
            
            // Update positions of all click buttons
            foreach (var kvp in activeVoiceBars)
            {
                UpdateClickButtonPosition(kvp.Key);
            }

            UpdateSettingsButtonPosition();
            
            // Update visibility of buttons depending on GUI state
            UpdateButtonVisibility();
            
            // Important: add the container to the update list every frame to ensure button handling
            // This is similar to how it's done for other elements in HandlePersistingElements
            if (buttonContainer != null && (activeVoiceBars.Count > 0 || isHudDragPendingStart || isHudDragging))
            {
                buttonContainer.AddToGUIUpdateList(order: 1);
            }

            if (settingsMenuRoot?.Visible == true)
            {
                if (isSettingsDisplayModeListOpen)
                {
                    RepositionDisplayModeList();
                }
                UpdateSettingsPathDisplayText();
                settingsMenuRoot.AddToGUIUpdateList(order: 2);
            }
        }

        private static void AddBar(Client client)
        {
            if (activeVoiceBars.ContainsKey(client)) return;
            if (GUI.Canvas == null) return;

            try
            {
                var bar = new VoiceBar(client);
                activeVoiceBars[client] = bar;

                // Create invisible button for handling clicks
                var clickButton = CreateClickButton(client);
                clickButtons[client] = clickButton;
                
                // Update the position right after creation
                UpdateClickButtonPosition(client);

                // Apply saved volume (already loaded in Update method, but ensure it's set)
                float savedVolume = GetSavedVolume(client);
                if (Math.Abs(savedVolume - 1.0f) > 0.001f)
                {
                    client.VoiceVolume = savedVolume;
                }
                
                // Initialize last known volume
                lastKnownVolumes[client] = client.VoiceVolume;
            }
            catch (Exception ex)
            {
                DebugConsole.Log($"Failed to add voice bar for {client.Name}: {ex.Message}");
            }
        }

        private static GUIButton CreateClickButton(Client client)
        {
            // Calculate absolute position
            Point hudOrigin = GetHudOriginPx();
            float startX = hudOrigin.X;
            
            // Find index in list
            int index = activeVoiceBars.Values.ToList().Count;
            float currentY = hudOrigin.Y + index * (BarHeight + TextHeight + BarSpacing);
            
            // Calculate size of clickable area
            Vector2 textSize = GUIStyle.SmallFont.MeasureString(client.Name ?? "Unknown");
            float padding = 5f;
            float totalWidth = textSize.X + 5 + BarWidth + padding * 2;
            float totalHeight = Math.Max(textSize.Y, BarHeight) + padding * 2;
            
            // Create button in container (use TopLeft for simplicity)
            if (buttonContainer == null)
            {
                buttonContainer = new GUIFrame(new RectTransform(Vector2.One, GUI.Canvas), style: null)
                {
                    CanBeFocused = false
                };
            }
            
            var button = new GUIButton(
                new RectTransform(
                    new Point((int)totalWidth, (int)totalHeight), 
                    buttonContainer.RectTransform, 
                    Anchor.TopLeft),
                style: null)
            {
                CanBeFocused = true,
                Enabled = true,
                Visible = true,
                HoverCursor = CursorState.Hand,
                UpdateOrder = 1, // High priority - handled last (but in reverse order this means first!)
                OnClicked = (btn, userdata) =>
                {
                    DebugConsole.Log($"Voice bar clicked for {client.Name}");
                    VoiceChatUI.OpenVolumeSettings(client);
                    return true;
                }
            };
            
            // Make the button transparent (temporarily can be made visible for debugging)
            // For debugging, uncomment the following line:
            // button.Color = new Color(255, 0, 0, 50); // Semi-transparent red
            button.Color = Color.Transparent;
            button.HoverColor = Color.Transparent;
            button.PressedColor = Color.Transparent;
            button.Frame.Color = Color.Transparent;
            button.Frame.HoverColor = Color.Transparent;
            button.Frame.PressedColor = Color.Transparent;
            button.Frame.CanBeFocused = false;
            
            // Set absolute position (from top left)
            button.RectTransform.AbsoluteOffset = new Point((int)(startX - padding), (int)(currentY - padding));
            
            // Add to GUI update list with high priority (order: 1 means it goes to lastAdditions)
            button.AddToGUIUpdateList(order: 1);
            
            DebugConsole.Log($"Created click button for {client.Name} at position ({startX - padding}, {currentY - padding}), size ({totalWidth}, {totalHeight}), UpdateOrder: {button.UpdateOrder}");
            
            return button;
        }

        private static void UpdateClickButtonPosition(Client client)
        {
            if (!clickButtons.TryGetValue(client, out var button)) return;
            
            // Calculate position
            Point hudOrigin = GetHudOriginPx();
            float startX = hudOrigin.X;
            
            // Find index in list
            var barsList = activeVoiceBars.Values.ToList();
            int index = barsList.FindIndex(b => b.Client == client);
            if (index < 0) return;
            
            float currentY = hudOrigin.Y + index * (BarHeight + TextHeight + BarSpacing);
            float padding = 5f;
            
            // Update the button's (absolute) position from top left corner
            button.RectTransform.AbsoluteOffset = new Point((int)(startX - padding), (int)(currentY - padding));
            
            // Ensure button is visible and enabled
            button.Visible = true;
            button.Enabled = true;
        }

        private static void EnsureSettingsButton()
        {
            if (GUI.Canvas == null || buttonContainer == null || settingsButton != null) { return; }

            settingsButton = new GUIButton(
                new RectTransform(new Point(SettingsButtonSize, SettingsButtonSize), buttonContainer.RectTransform, Anchor.TopLeft),
                text: string.Empty,
                style: null)
            {
                CanBeFocused = true,
                Enabled = true,
                Visible = true,
                HoverCursor = CursorState.Hand,
                ToolTip = "SpeakerList Settings",
                UpdateOrder = 1,
                Color = Color.Transparent,
                HoverColor = new Color(70, 70, 70, 210),
                PressedColor = new Color(96, 96, 96, 230),
                OnClicked = (_, _) =>
                {
                    OpenSettingsMenu();
                    return true;
                }
            };
            settingsButton.Frame.Color = Color.Transparent;
            settingsButton.Frame.HoverColor = Color.Transparent;
            settingsButton.Frame.PressedColor = Color.Transparent;
            settingsButton.Frame.CanBeFocused = false;

            new GUIImage(new RectTransform(new Vector2(0.72f), settingsButton.RectTransform, Anchor.Center), style: "GUIButtonInfo")
            {
                CanBeFocused = false
            };

            settingsButton.AddToGUIUpdateList(order: 1);
        }

        private static void UpdateSettingsButtonPosition()
        {
            if (activeVoiceBars.Count == 0)
            {
                RemoveSettingsButton();
                return;
            }

            EnsureSettingsButton();
            if (settingsButton == null) { return; }

            // Keep it at the right side, but above the first voice bar to avoid overlap with
            // the invisible per-bar click area.
            Point hudOrigin = GetHudOriginPx();
            int x = (int)(hudOrigin.X + BarWidth + SettingsButtonOffsetX);
            int y = (int)Math.Max(6, hudOrigin.Y - SettingsButtonSize - 6);
            settingsButton.RectTransform.AbsoluteOffset = new Point(x, y);
            settingsButton.Visible = true;
            settingsButton.Enabled = true;
        }

        private static void RemoveSettingsButton()
        {
            if (settingsButton == null) { return; }
            GUI.RemoveFromUpdateList(settingsButton, true);
            settingsButton.RectTransform.Parent = null;
            settingsButton = null;
        }

        private static void RemoveBar(Client client)
        {
            if (activeVoiceBars.TryGetValue(client, out var bar))
            {
                activeVoiceBars.Remove(client);
            }
            
            // Remove the click button
            if (clickButtons.TryGetValue(client, out var button))
            {
                GUI.RemoveFromUpdateList(button, true);
                button.RectTransform.Parent = null;
                clickButtons.Remove(client);
            }
            
            // Clean up last known volume tracking when client disconnects
            if (client != null && !GameMain.Client?.ConnectedClients?.Contains(client) == true)
            {
                lastKnownVolumes.Remove(client);
            }
        }

        private static void ClearAllBars()
        {
            // Remove all buttons
            foreach (var button in clickButtons.Values)
            {
                GUI.RemoveFromUpdateList(button, true);
                button.RectTransform.Parent = null;
            }
            clickButtons.Clear();
            activeVoiceBars.Clear();
            
            // Clear the container
            if (buttonContainer != null)
            {
                buttonContainer.ClearChildren();
            }
            RemoveSettingsButton();
            RemoveSettingsMenu();
        }

        public static List<VoiceBar> GetActiveBars()
        {
            return activeVoiceBars.Values.ToList();
        }

        public static void Draw(SpriteBatch spriteBatch)
        {
            if (GUI.DisableHUD) return;

            var barsToDraw = activeVoiceBars.Values.ToList();
            if (barsToDraw.Count == 0 && !isHudDragPendingStart && !isHudDragging) return;

            Point hudOrigin = GetHudOriginPx();
            float startX = hudOrigin.X;
            float currentY = hudOrigin.Y;

            foreach (var bar in barsToDraw)
            {
                bar.Draw(spriteBatch, new Vector2(startX, currentY));
                
                // Update the click button position
                UpdateClickButtonPosition(bar.Client);
                
                currentY += BarHeight + TextHeight + BarSpacing;
            }

            if (isHudDragPendingStart || isHudDragging)
            {
                string hint = isHudDragging
                    ? "Move SpeakerList HUD and release to save"
                    : "Hold left mouse and drag SpeakerList HUD";
                Vector2 hintPos = new Vector2(startX, Math.Max(6f, hudOrigin.Y - 24f));
                GUIStyle.SmallFont.DrawString(spriteBatch, hint, hintPos + Vector2.One, Color.Black * 0.8f);
                GUIStyle.SmallFont.DrawString(spriteBatch, hint, hintPos, SettingsTextMain);
            }
        }

        public static void UpdateButtonVisibility()
        {
            // Hide/show buttons depending on GUI state
            bool shouldBeVisible = !GUI.InputBlockingMenuOpen && !GUI.DisableHUD;
            foreach (var button in clickButtons.Values)
            {
                button.Visible = shouldBeVisible;
                button.Enabled = shouldBeVisible;
            }

            bool settingsVisible = shouldBeVisible && activeVoiceBars.Count > 0;
            if (isHudDragging || isHudDragPendingStart)
            {
                settingsVisible = true;
            }
            if (settingsButton != null)
            {
                settingsButton.Visible = settingsVisible;
                settingsButton.Enabled = settingsVisible;
            }
        }

        public static void OpenVolumeSettings(Client client)
        {
            if (client == null || GameMain.Client == null) return;
            
            if (GameMain.NetLobbyScreen != null)
            {
                GameMain.NetLobbyScreen.SelectPlayer(client);
            }
            else
            {
                // If NetLobbyScreen is not available (e.g. in-game), use custom dialog
                DebugConsole.Log($"NetLobbyScreen is not available, using custom dialog.");
                CreateVolumeDialog(client);
            }
        }

        private static void CreateVolumeDialog(Client client)
        {
            if (client == null || GUI.Canvas == null) return;

            double creationTime = Timing.TotalTime;

            // 1. Create a transparent blocker for the whole screen.
            // This is the standard Barotrauma way to make a modal window closing on side click.
            var backgroundBlocker = new GUIButton(new RectTransform(Vector2.One, GUI.Canvas), style: null)
            {
                OnClicked = (btn, userdata) =>
                {
                    // Ignore click in the same frame the menu was opened
                    if (Timing.TotalTime <= creationTime) return false;

                    // Close all menu on background click
                    btn.Parent.RemoveChild(btn);
                    return true;
                }
            };

            // 2. Add background dim (semi-transparent black layer)
            new GUIFrame(new RectTransform(Vector2.One, backgroundBlocker.RectTransform), style: "GUIBackgroundBlocker");

            // 3. Main settings window
            // Use fixed pixel size (Point) for interface stability
            var frameSize = new Point(500, 320);
            var frame = new GUIFrame(new RectTransform(frameSize, backgroundBlocker.RectTransform, Anchor.Center));
            frame.CanBeFocused = true; // Ensure clicks inside window do not close it via backgroundBlocker

            // Content group (10% side padding)
            var content = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.85f), frame.RectTransform, Anchor.Center))
            {
                Stretch = true,
                RelativeSpacing = 0.05f
            };

            // Title - Player name
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.2f), content.RectTransform),
                client.Name, font: GUIStyle.LargeFont, textAlignment: Alignment.Center);

            // Mute checkbox (local mute)
            new GUITickBox(new RectTransform(new Vector2(1.0f, 0.15f), content.RectTransform), TextManager.Get("Mute"))
            {
                Selected = client.MutedLocally,
                OnSelected = (tickBox) =>
                {
                    client.MutedLocally = tickBox.Selected;
                    return true;
                }
            };

            // Volume section: Text + Percentage
            var volumeLayout = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.35f), content.RectTransform), isHorizontal: false);
            var volumeTextLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.5f), volumeLayout.RectTransform), isHorizontal: true, childAnchor: Anchor.CenterLeft);
            
            new GUITextBlock(new RectTransform(new Vector2(0.6f, 1f), volumeTextLayout.RectTransform), TextManager.Get("VoiceChatVolume"));
            var percentageText = new GUITextBlock(new RectTransform(new Vector2(0.4f, 1f), volumeTextLayout.RectTransform),
                ToolBox.GetFormattedPercentage(client.VoiceVolume), textAlignment: Alignment.Right);

            // Slider (ScrollBar with GUISlider style)
            var volumeSlider = new GUIScrollBar(new RectTransform(new Vector2(1f, 0.5f), volumeLayout.RectTransform), barSize: 0.1f, style: "GUISlider")
            {
                Range = new Vector2(0f, 1f),
                // Normalize value (0.0 - 2.0 -> 0.0 - 1.0)
                BarScroll = client.VoiceVolume / Client.MaxVoiceChatBoost,
                OnMoved = (_, barScroll) =>
                {
                    float newVolume = barScroll * Client.MaxVoiceChatBoost;
                    client.VoiceVolume = newVolume;
                    percentageText.Text = ToolBox.GetFormattedPercentage(newVolume);
                    
                    // Save to your mod XML config
                    SaveVolume(client, newVolume);
                    return true;
                }
            };

            // Close button (at the bottom)
            new GUIButton(new RectTransform(new Vector2(0.5f, 0.15f), content.RectTransform), TextManager.Get("Close"), style: "GUIButtonSmall")
            {
                OnClicked = (btn, obj) =>
                {
                    backgroundBlocker.Parent.RemoveChild(backgroundBlocker);
                    return true;
                }
            };
        }
    }
#endif
}
