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
    internal class VoiceBar
    {
        private readonly Client client;
        private float currentAmplitude = 0f;
        private Color lastBarBaseColor = Color.White;
        private static Sprite cachedSpectatorIcon;

        public float FadeTimer { get; set; }
        public Client Client => client;

        public VoiceBar(Client client)
        {
            this.client = client;
            FadeTimer = VoiceChatUI.GetCurrentFadeOutTime();
            if (VoiceChatUI.CurrentDisplayMode == VoiceBarDisplayMode.Enhanced)
            {
                lastBarBaseColor = GetCurrentBarBaseColor();
            }
        }

        public void Update(float deltaTime, Client client)
        {
            if (client?.VoipSound != null)
            {
                currentAmplitude = Math.Min(client.VoipSound.CurrentAmplitude * 2.0f, 1.0f);
                if (VoiceChatUI.CurrentDisplayMode == VoiceBarDisplayMode.Enhanced)
                {
                    lastBarBaseColor = GetCurrentBarBaseColor();
                }
            }
            else
            {
                currentAmplitude = 0f;
            }
        }

        public void Draw(SpriteBatch spriteBatch, Vector2 position)
        {
            float fadeOutTime = Math.Max(VoiceChatUI.GetCurrentFadeOutTime(), 0.0001f);
            float alpha = Math.Min(FadeTimer / fadeOutTime, 1.0f);

            if (VoiceChatUI.CurrentDisplayMode == VoiceBarDisplayMode.Enhanced)
            {
                DrawEnhanced(spriteBatch, position, alpha);
            }
            else
            {
                DrawCurrent(spriteBatch, position, alpha);
            }
        }

        private void DrawCurrent(SpriteBatch spriteBatch, Vector2 position, float alpha)
        {
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

        private void DrawEnhanced(SpriteBatch spriteBatch, Vector2 position, float alpha)
        {
            Color barColor = GetBarColor(alpha);
            Color outlineColor = new Color((byte)(0.5f * 255), (byte)(0.57f * 255), (byte)(0.6f * 255), (byte)(alpha * 255));
            Color textColor = GetNameColor(alpha);

            Sprite jobIcon = GetJobIcon();
            Sprite spectatorIcon = null;
            if (jobIcon == null && IsSpectatorOrJobless())
            {
                spectatorIcon = GetSpectatorIcon();
            }

            const float iconSize = 16f;
            const float iconSpacing = 3f;
            const float spectatorIconScale = 0.24f;
            const float textScale = 1.0f;

            string playerName = GetDisplayName();
            Vector2 textSize = GUIStyle.SmallFont.MeasureString(playerName) * textScale;
            float iconWidth = (jobIcon != null || IsSpectatorOrJobless()) ? (iconSize + iconSpacing) : 0f;
            float totalLabelWidth = textSize.X + iconWidth;

            Vector2 labelStartPos = new Vector2(position.X - totalLabelWidth - 5f, position.Y);
            Vector2 textPos = new Vector2(labelStartPos.X + iconWidth, position.Y);

            float verticalOffset = (VoiceChatUI.BarHeight - iconSize) / 2f + 6f;
            Vector2 iconPos = new Vector2(labelStartPos.X, position.Y + verticalOffset);

            if (jobIcon != null)
            {
                float baseSize = Math.Max(1f, Math.Max(jobIcon.SourceRect.Width, jobIcon.SourceRect.Height));
                float iconScale = Math.Min(1f, iconSize / baseSize);
                jobIcon.Draw(spriteBatch, iconPos + new Vector2(1.5f, 1.5f), Color.Black * (alpha * 0.85f), scale: iconScale);
                jobIcon.Draw(spriteBatch, iconPos, textColor, scale: iconScale);
            }
            else if (spectatorIcon != null)
            {
                spectatorIcon.Draw(spriteBatch, iconPos + new Vector2(1.5f, 1.5f), Color.Black * (alpha * 0.85f), scale: spectatorIconScale);
                spectatorIcon.Draw(spriteBatch, iconPos, textColor, scale: spectatorIconScale);
            }
            else if (IsSpectatorOrJobless())
            {
                const string fallbackIcon = "o";
                GUI.DrawString(spriteBatch, iconPos + Vector2.One, fallbackIcon, Color.Black * alpha, null, 0, GUIStyle.SmallFont);
                GUI.DrawString(spriteBatch, iconPos, fallbackIcon, textColor, null, 0, GUIStyle.SmallFont);
            }

            Vector2 textOrigin = Vector2.Zero;
            Vector2 textScaleVector = new Vector2(textScale, textScale);
            GUIStyle.SmallFont.DrawString(spriteBatch, playerName, new Vector2(textPos.X + 1, textPos.Y + 1), Color.Black * alpha, 0f, textOrigin, textScaleVector, SpriteEffects.None, 0f);
            GUIStyle.SmallFont.DrawString(spriteBatch, playerName, textPos, textColor, 0f, textOrigin, textScaleVector, SpriteEffects.None, 0f);

            Vector2 barPos = new Vector2(position.X, -position.Y);
            GUI.DrawProgressBar(spriteBatch, barPos, new Vector2(VoiceChatUI.BarWidth, VoiceChatUI.BarHeight),
                currentAmplitude, barColor, outlineColor);
        }

        private Color GetNameColor(float alpha)
        {
            Color baseColor;
            var jobPrefab = client?.Character?.Info?.Job?.Prefab;
            if (jobPrefab != null)
            {
                baseColor = jobPrefab.UIColor;
            }
            else
            {
                baseColor = ChatMessage.MessageColor[(int)ChatMessageType.Dead];
            }
            return new Color(baseColor.R, baseColor.G, baseColor.B, (byte)(alpha * 255));
        }

        private Sprite GetJobIcon()
        {
            return client?.Character?.Info?.Job?.Prefab?.IconSmall;
        }

        private bool IsSpectatorOrJobless()
        {
            return client?.Spectating == true || client?.Character?.Info?.Job?.Prefab?.IconSmall == null;
        }

        private string GetDisplayName()
        {
            return client?.Name ?? "Unknown";
        }

        private Sprite GetSpectatorIcon()
        {
            if (cachedSpectatorIcon != null) { return cachedSpectatorIcon; }
            try
            {
                cachedSpectatorIcon =
                    GUIStyle.GetComponentStyle("SpectateIcon")?.GetDefaultSprite() ??
                    GUIStyle.GetComponentStyle("spectateicon")?.GetDefaultSprite();
                return cachedSpectatorIcon;
            }
            catch
            {
                return null;
            }
        }

        private Color GetCurrentBarBaseColor()
        {
            bool isSpectatorOrDead =
                client?.Spectating == true ||
                client?.Character == null ||
                client.Character.IsDead ||
                client.Character.Removed ||
                client?.Character?.Info?.Job?.Prefab == null;

            if (isSpectatorOrDead)
            {
                return ChatMessage.MessageColor[(int)ChatMessageType.Dead];
            }

            if (client?.VoipSound != null && client.VoipSound.UsingRadio)
            {
                return ChatMessage.MessageColor[(int)ChatMessageType.Radio];
            }

            return ChatMessage.MessageColor[(int)ChatMessageType.Default];
        }

        private Color GetBarColor(float alpha)
        {
            return new Color(lastBarBaseColor.R, lastBarBaseColor.G, lastBarBaseColor.B, (byte)(alpha * 255));
        }
    }
#endif
}
