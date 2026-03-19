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
    internal static partial class VoiceChatUI
    {
        private static string GetPrimarySettingsPath()
        {
            string? configDirectory = Path.GetDirectoryName(Path.GetFullPath(GameSettings.PlayerConfigPath));
            if (string.IsNullOrWhiteSpace(configDirectory))
            {
                configDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Daedalic Entertainment GmbH",
                    "Barotrauma");
            }
            return Path.Combine(configDirectory, "VoiceChatMonitor", "volumes.xml");
        }

        private static string GetLegacySettingsPath()
        {
            string localAppDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Daedalic Entertainment GmbH",
                "Barotrauma");
            return Path.Combine(localAppDataFolder, "VoiceChatMonitor", "volumes.xml");
        }

        private static string ToDisplayModeValue(VoiceBarDisplayMode mode)
        {
            return mode == VoiceBarDisplayMode.Enhanced ? "enhanced" : "current";
        }

        private static VoiceBarDisplayMode ParseDisplayMode(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) { return VoiceBarDisplayMode.Current; }
            string normalized = value.Trim().ToLowerInvariant();
            if (normalized is "enhanced" or "advanced" or "расширенный")
            {
                return VoiceBarDisplayMode.Enhanced;
            }
            return VoiceBarDisplayMode.Current;
        }

        private static void LoadVolumes()
        {
            try
            {
                savedVolumes.Clear();
                displayMode = VoiceBarDisplayMode.Current;
                hudAnchorNormalized = new Vector2(-1f, -1f);

                string pathToLoad = settingsPath;
                if (!File.Exists(pathToLoad) && File.Exists(legacySettingsPath))
                {
                    pathToLoad = legacySettingsPath;
                }

                loadedFromLegacyPath = string.Equals(pathToLoad, legacySettingsPath, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(pathToLoad, settingsPath, StringComparison.OrdinalIgnoreCase);

                if (File.Exists(pathToLoad))
                {
                    XDocument doc = XDocument.Load(pathToLoad);
                    XElement? settingsElement = doc.Root?.Element("settings");
                    displayMode = ParseDisplayMode(settingsElement?.GetAttributeString("displaymode", "current") ?? "current");
                    float hudX = settingsElement?.GetAttributeFloat("hudx", -1f) ?? -1f;
                    float hudY = settingsElement?.GetAttributeFloat("hudy", -1f) ?? -1f;
                    if (IsNormalizedCoordValid(hudX) && IsNormalizedCoordValid(hudY))
                    {
                        hudAnchorNormalized = new Vector2(hudX, hudY);
                    }
                    foreach (var element in doc.Root?.Elements("player") ?? Enumerable.Empty<XElement>())
                    {
                        // Try AccountID first, then SessionId, then name (for backward compatibility)
                        string accountId = element.GetAttributeString("accountid", "");
                        string sessionId = element.GetAttributeString("sessionid", "");
                        string name = element.GetAttributeString("name", "");
                        float volume = element.GetAttributeFloat("volume", 1.0f);
                        
                        string key = !string.IsNullOrEmpty(accountId) ? accountId 
                                   : !string.IsNullOrEmpty(sessionId) ? sessionId 
                                   : name;
                        
                        if (!string.IsNullOrEmpty(key))
                        {
                            savedVolumes[key] = volume;
                        }
                    }
                    DebugConsole.Log($"Loaded {savedVolumes.Count} voice volume settings from {pathToLoad} (mode: {displayMode})");
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
                string? directory = Path.GetDirectoryName(settingsPath);
                if (directory != null)
                {
                    Directory.CreateDirectory(directory);
                }
                
                XDocument doc = new XDocument(new XElement("volumes"));
                doc.Root!.Add(new XElement("settings",
                    new XAttribute("displaymode", ToDisplayModeValue(displayMode)),
                    new XAttribute("hudx", hudAnchorNormalized.X),
                    new XAttribute("hudy", hudAnchorNormalized.Y)));
                foreach (var kvp in savedVolumes)
                {
                    var playerElement = new XElement("player",
                        new XAttribute("volume", kvp.Value));
                    
                    // Try to find client to get AccountID and name
                    Client? client = GameMain.Client?.ConnectedClients?.FirstOrDefault(c => GetClientKey(c) == kvp.Key);
                    if (client != null)
                    {
                        if (client.AccountId.TryUnwrap(out var accountId))
                        {
                            // Use StringRepresentation for AccountID
                            playerElement.SetAttributeValue("accountid", accountId.StringRepresentation);
                        }
                        playerElement.SetAttributeValue("sessionid", client.SessionId.ToString());
                        playerElement.SetAttributeValue("name", client.Name ?? "");
                    }
                    else
                    {
                        // Fallback: use key as name for backward compatibility
                        playerElement.SetAttributeValue("name", kvp.Key);
                    }
                    
                    doc.Root!.Add(playerElement);
                }
                doc.Save(settingsPath);
                loadedFromLegacyPath = false;
                DebugConsole.Log($"Saved {savedVolumes.Count} voice volume settings to {settingsPath}");
            }
            catch (Exception ex)
            {
                DebugConsole.Log($"Failed to save voice volumes: {ex.Message}\nStackTrace: {ex.StackTrace}");
            }
        }
        
        private static string GetClientKey(Client client)  
        {  
            // Prefer AccountID, then SessionId, then name  
            if (client.AccountId.TryUnwrap(out var accountId))  
            {  
                return accountId.StringRepresentation;  
            }  
            return client.SessionId.ToString();  
        }

        public static float GetSavedVolume(Client client)
        {
            if (client == null) return 1.0f;
            string key = GetClientKey(client);
            return savedVolumes.TryGetValue(key, out float volume) ? volume : 1.0f;
        }

        public static void SaveVolume(Client client, float volume)
        {
            if (client == null) return;
            string key = GetClientKey(client);
            savedVolumes[key] = volume;
            DebugConsole.Log($"Saving volume for {client.Name} (key: {key}): {volume}");
            SaveVolumes();
        }

        public static string GetSettingsPath()
        {
            return settingsPath;
        }

    }
#endif
}
