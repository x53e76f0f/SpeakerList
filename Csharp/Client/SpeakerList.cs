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
    public class VoiceChatMonitor : IAssemblyPlugin
    {
        private Harmony? harmony;

        public void Initialize()
        {
            harmony = new Harmony("LineofSight.VoiceChatMonitor");
        }

        public void OnLoadCompleted()
        {
            harmony?.PatchAll();
            VoiceChatUI.Initialize();
        }

        public void Dispose()
        {
            harmony?.UnpatchSelf();
            VoiceChatUI.Dispose();
        }

        public void PreInitPatching() { }
    }

#if CLIENT
    internal static class VoiceChatUI
    {
        private static readonly Dictionary<Client, VoiceBar> activeVoiceBars = new Dictionary<Client, VoiceBar>();
        private static readonly Dictionary<Client, GUIButton> clickButtons = new Dictionary<Client, GUIButton>(); // Invisible click buttons
        private static GUIFrame buttonContainer; // Container for buttons above all elements
        private static readonly Dictionary<string, float> savedVolumes = new Dictionary<string, float>();
        private static string settingsPath = string.Empty;
        private const float VoiceThreshold = 0.01f; // Minimum amplitude for displaying
        private const float FadeOutTime = 0.9f; // Fade out time after speaking ends
        
        // Drawing parameters (same as for the boss bars on the right)
        public const float BarWidth = 150f;   // Bar width
        public const float BarHeight = 20f;   // Bar height
        public const float BarSpacing = 10f;  // Space between bars
        public const float TextHeight = 15f;  // Text height
        public const float RightMargin = 10f; // Margin from the right
        public const float TopMargin = 100f;  // Margin from the top (to not overlap other UI elements)

        public static void Initialize()
        {
            // Path for saving settings: game save folder + mod subfolder
            settingsPath = Path.Combine(SaveUtil.GetSaveFolder(SaveUtil.SaveType.Singleplayer), "VoiceChatMonitor", "volumes.xml");
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

        private static void LoadVolumes()
        {
            try
            {
                if (File.Exists(settingsPath))
                {
                    XDocument doc = XDocument.Load(settingsPath);
                    savedVolumes.Clear();
                    foreach (var element in doc.Root?.Elements("player") ?? Enumerable.Empty<XElement>())
                    {
                        string name = element.GetAttributeString("name", "");
                        float volume = element.GetAttributeFloat("volume", 1.0f);
                        if (!string.IsNullOrEmpty(name))
                        {
                            savedVolumes[name] = volume;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                DebugConsole.Log($"Failed to load voice volumes: {ex.Message}");
            }
        }

        private static void SaveVolumes()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
                XDocument doc = new XDocument(new XElement("volumes"));
                foreach (var kvp in savedVolumes)
                {
                    doc.Root!.Add(new XElement("player",
                        new XAttribute("name", kvp.Key),
                        new XAttribute("volume", kvp.Value)));
                }
                doc.Save(settingsPath);
            }
            catch (Exception ex)
            {
                DebugConsole.Log($"Failed to save voice volumes: {ex.Message}");
            }
        }

        public static float GetSavedVolume(string playerName)
        {
            return savedVolumes.TryGetValue(playerName, out float volume) ? volume : 1.0f;
        }

        public static void SaveVolume(string playerName, float volume)
        {
            savedVolumes[playerName] = volume;
            SaveVolumes();
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
                    bar.FadeTimer = FadeOutTime;
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
            
            // Update visibility of buttons depending on GUI state
            UpdateButtonVisibility();
            
            // Important: add the container to the update list every frame to ensure button handling
            // This is similar to how it's done for other elements in HandlePersistingElements
            if (buttonContainer != null && activeVoiceBars.Count > 0)
            {
                buttonContainer.AddToGUIUpdateList(order: 1);
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

                // Apply saved volume
                if (savedVolumes.TryGetValue(client.Name, out float savedVolume))
                {
                    client.VoiceVolume = savedVolume;
                }
            }
            catch (Exception ex)
            {
                DebugConsole.Log($"Failed to add voice bar for {client.Name}: {ex.Message}");
            }
        }

        private static GUIButton CreateClickButton(Client client)
        {
            // Calculate absolute position
            float screenWidth = GameMain.GraphicsWidth;
            float startX = screenWidth - BarWidth - RightMargin;
            
            // Find index in list
            int index = activeVoiceBars.Values.ToList().Count;
            float currentY = TopMargin + index * (BarHeight + TextHeight + BarSpacing);
            
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
            float screenWidth = GameMain.GraphicsWidth;
            float startX = screenWidth - BarWidth - RightMargin;
            
            // Find index in list
            var barsList = activeVoiceBars.Values.ToList();
            int index = barsList.FindIndex(b => b.Client == client);
            if (index < 0) return;
            
            float currentY = TopMargin + index * (BarHeight + TextHeight + BarSpacing);
            float padding = 5f;
            
            // Update the button's (absolute) position from top left corner
            button.RectTransform.AbsoluteOffset = new Point((int)(startX - padding), (int)(currentY - padding));
            
            // Ensure button is visible and enabled
            button.Visible = true;
            button.Enabled = true;
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
        }

        public static List<VoiceBar> GetActiveBars()
        {
            return activeVoiceBars.Values.ToList();
        }

        public static void Draw(SpriteBatch spriteBatch)
        {
            if (GUI.DisableHUD) return;

            var barsToDraw = activeVoiceBars.Values.ToList();
            if (barsToDraw.Count == 0) return;

            float screenWidth = GameMain.GraphicsWidth;
            float startX = screenWidth - BarWidth - RightMargin;
            float currentY = TopMargin;

            foreach (var bar in barsToDraw)
            {
                bar.Draw(spriteBatch, new Vector2(startX, currentY));
                
                // Update the click button position
                UpdateClickButtonPosition(bar.Client);
                
                currentY += BarHeight + TextHeight + BarSpacing;
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
                    SaveVolume(client.Name, newVolume);
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

    // Class for displaying the voice bar (manual drawing)
    internal class VoiceBar
    {
        private readonly Client client;
        private float currentAmplitude = 0f;
        public float FadeTimer { get; set; }
        public Client Client => client;

        public VoiceBar(Client client)
        {
            this.client = client;
            FadeTimer = VoiceChatUI.FadeOutTime;
        }

        public void Update(float deltaTime, Client client)
        {
            if (client?.VoipSound != null)
            {
                currentAmplitude = Math.Min(client.VoipSound.CurrentAmplitude * 2.0f, 1.0f);
            }
            else
            {
                currentAmplitude = 0f;
            }
        }

        public void Draw(SpriteBatch spriteBatch, Vector2 position)
        {
            // Calculate alpha channel based on FadeTimer
            float alpha = Math.Min(FadeTimer / VoiceChatUI.FadeOutTime, 1.0f);
            var greenColor = GUIStyle.Green.Value;
            Color barColor = new Color(greenColor.R, greenColor.G, greenColor.B, (byte)(alpha * 255));
            Color outlineColor = new Color((byte)(0.5f * 255), (byte)(0.57f * 255), (byte)(0.6f * 255), (byte)(alpha * 255));
            Color textColor = new Color((byte)255, (byte)255, (byte)255, (byte)(alpha * 255));

            // Draw player name to the right of the bar
            string playerName = client.Name ?? "Unknown";
            Vector2 textSize = GUIStyle.SmallFont.MeasureString(playerName);
            Vector2 textPos = new Vector2(position.X - textSize.X - 5, position.Y);
            // Draw shadow first by drawing the text with an offset
            GUI.DrawString(spriteBatch, new Vector2(textPos.X + 1, textPos.Y + 1), playerName, Color.Black * alpha, null, 0, GUIStyle.SmallFont);
            // Draw main text
            GUI.DrawString(spriteBatch, textPos, playerName, textColor, null, 0, GUIStyle.SmallFont);

            // Draw progress bar (like boss bar on the right)
            // GUI.DrawProgressBar uses negative Y coordinate for inversion
            // So we pass position with the Y inverted
            Vector2 barPos = new Vector2(position.X, -position.Y);
            GUI.DrawProgressBar(spriteBatch, barPos, new Vector2(VoiceChatUI.BarWidth, VoiceChatUI.BarHeight), 
                currentAmplitude, barColor, outlineColor);
        }
    }

    [HarmonyPatch(typeof(GameScreen), nameof(GameScreen.Update))]
    internal static class VoiceChatUpdatePatch
    {
        static void Postfix(GameScreen __instance, double deltaTime)
        {
            if (__instance == null || GameMain.Client == null) return;
            VoiceChatUI.Update((float)deltaTime);
        }
    }

    [HarmonyPatch(typeof(NetLobbyScreen), nameof(NetLobbyScreen.Update))]  
    internal static class NetLobbyVoiceChatUpdatePatch  
    {  
        static void Postfix(NetLobbyScreen __instance, double deltaTime)  
        {  
            if (__instance == null || GameMain.Client == null) return;  
            VoiceChatUI.Update((float)deltaTime);
        }  
    } 

    [HarmonyPatch(typeof(GUI), "DrawCursor")]
    internal static class GUIDrawCursorPatch
    {
        static void Prefix(SpriteBatch spriteBatch)
        {
            VoiceChatUI.Draw(spriteBatch);
        }
    }

    // Patch for Character.Update - force health bar visible for characters that are speaking
    [HarmonyPatch(typeof(Character), nameof(Character.Update))]
    internal static class CharacterUpdatePatch
    {
        private static readonly FieldInfo hudInfoVisibleField = typeof(Character).GetField("hudInfoVisible", 
            BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly PropertyInfo useBossHealthBarProperty = typeof(CharacterParams).GetProperty("UseBossHealthBar", 
            BindingFlags.Public | BindingFlags.Instance);

        static void Postfix(Character __instance)
        {
            if (hudInfoVisibleField == null || useBossHealthBarProperty == null) return;
            if (__instance == null || __instance.Removed) return;
            if (!__instance.IsPlayer) return; // Only for players

            try
            {
                // Check if this player is speaking
                var client = GameMain.Client?.ConnectedClients?.FirstOrDefault(c => c.Character == __instance);
                if (client == null || client.VoipSound == null || !VoiceChatUI.IsClientSpeaking(client))
                {
                    return;
                }

                // Force health bar visibility
                bool hudInfoVisible = (bool)(hudInfoVisibleField.GetValue(__instance) ?? false);
                if (!hudInfoVisible)
                {
                    hudInfoVisibleField.SetValue(__instance, true);
                    
                    // Set UseBossHealthBar using reflection
                    if (__instance.Params != null)
                    {
                        var setter = useBossHealthBarProperty.GetSetMethod(nonPublic: true);
                        //setter?.Invoke(__instance.Params, new object[] { true });
                    }
                }
            }
            catch (Exception)
            {
                // Silently fail if reflection fails
            }
        }
    }
#endif
}
