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
