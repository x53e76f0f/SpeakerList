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
        private static void EnsureSettingsMenuCreated()
        {
            if (settingsMenuRoot != null || GUI.Canvas == null) { return; }

            settingsMenuRoot = new GUIFrame(new RectTransform(Vector2.One, GUI.Canvas), style: null)
            {
                CanBeFocused = false,
                Visible = false,
                Color = Color.Transparent
            };

            var backgroundBlocker = new GUIButton(new RectTransform(Vector2.One, settingsMenuRoot.RectTransform), style: null)
            {
                Color = Color.Transparent,
                HoverColor = Color.Transparent,
                PressedColor = Color.Transparent,
                OnClicked = (_, _) =>
                {
                    if (isSettingsDisplayModeListOpen)
                    {
                        SetDisplayModeListVisible(false);
                    }
                    // Keep the menu open on background click; explicit close buttons only.
                    return true;
                }
            };

            new GUIFrame(new RectTransform(Vector2.One, backgroundBlocker.RectTransform), style: "GUIBackgroundBlocker");
            int width = Math.Max(GUI.IntScale(480), 440);
            int height = Math.Max(GUI.IntScale(320), 300);
            int headerHeight = GUI.IntScale(30);

            var frame = new GUIFrame(new RectTransform(new Point(width, height), backgroundBlocker.RectTransform, Anchor.Center), style: null)
            {
                CanBeFocused = true,
                Color = SettingsWindowBg,
                OutlineColor = SettingsBorder
            };

            var header = new GUIFrame(
                new RectTransform(new Vector2(1f, 0f), frame.RectTransform, Anchor.TopLeft)
                {
                    MinSize = new Point(0, headerHeight)
                }, style: null)
            {
                Color = SettingsHeaderBg
            };

            new GUITextBlock(
                new RectTransform(new Vector2(0.85f, 1f), header.RectTransform, Anchor.CenterLeft),
                "SpeakerList Settings",
                font: GUIStyle.SmallFont,
                textAlignment: Alignment.CenterLeft)
            {
                TextColor = SettingsTextMain,
                CanBeFocused = false,
                Padding = new Vector4(GUI.IntScale(10), 0, 0, 0)
            };

            var closeBtn = new GUIButton(
                new RectTransform(new Point(headerHeight, headerHeight), header.RectTransform, Anchor.TopRight),
                "x", style: null)
            {
                Color = Color.Transparent,
                HoverColor = new Color(70, 70, 70, 220),
                PressedColor = new Color(96, 96, 96, 230),
                TextColor = SettingsTextDim,
                Font = GUIStyle.SmallFont,
                ToolTip = "Close"
            };
            closeBtn.OnClicked = (_, _) =>
            {
                CloseSettingsMenu();
                return true;
            };

            var displaySection = new GUIFrame(
                new RectTransform(new Vector2(0.92f, 0.28f), frame.RectTransform, Anchor.TopCenter)
                {
                    RelativeOffset = new Vector2(0f, 0.14f)
                }, style: null)
            {
                Color = SettingsSectionBg,
                OutlineColor = SettingsSectionBorder
            };

            new GUITextBlock(
                new RectTransform(new Vector2(0.92f, 0.30f), displaySection.RectTransform, Anchor.TopCenter),
                "Display",
                font: GUIStyle.SmallFont,
                textAlignment: Alignment.CenterLeft)
            {
                TextColor = SettingsTextDim,
                Padding = new Vector4(GUI.IntScale(8), 0, 0, 0),
                CanBeFocused = false
            };

            new GUITextBlock(
                new RectTransform(new Vector2(0.40f, 0.34f), displaySection.RectTransform, Anchor.CenterLeft),
                "Display Mode",
                font: GUIStyle.SmallFont,
                textAlignment: Alignment.CenterLeft)
            {
                TextColor = SettingsTextMain,
                Padding = new Vector4(GUI.IntScale(6), 0, 0, 0),
                CanBeFocused = false
            };

            settingsDisplayModeButton = new GUIButton(
                new RectTransform(new Vector2(0.48f, 0.42f), displaySection.RectTransform, Anchor.CenterRight),
                string.Empty,
                style: null)
            {
                Color = SettingsControlBg,
                HoverColor = SettingsControlHover,
                PressedColor = SettingsControlHover,
                TextColor = SettingsTextMain,
                ToolTip = "Voice bar render mode"
            };
            settingsDisplayModeButton.OnClicked = (_, _) =>
            {
                SetDisplayModeListVisible(!isSettingsDisplayModeListOpen);
                return true;
            };
            new GUITextBlock(
                new RectTransform(new Vector2(0.90f, 1f), settingsDisplayModeButton.RectTransform, Anchor.CenterLeft),
                string.Empty,
                font: GUIStyle.SmallFont,
                textAlignment: Alignment.CenterLeft)
            {
                TextColor = SettingsTextMain,
                Padding = new Vector4(GUI.IntScale(6), 0, 0, 0),
                CanBeFocused = false
            };
            new GUITextBlock(
                new RectTransform(new Vector2(0.10f, 1f), settingsDisplayModeButton.RectTransform, Anchor.CenterRight),
                "v",
                font: GUIStyle.SmallFont,
                textAlignment: Alignment.Center)
            {
                TextColor = SettingsTextDim,
                CanBeFocused = false
            };

            settingsDisplayModeList = new GUIFrame(
                new RectTransform(new Point(Math.Max(180, GUI.IntScale(220)), Math.Max(56, GUI.IntScale(58))), settingsMenuRoot.RectTransform, Anchor.TopLeft),
                style: null)
            {
                Visible = false,
                Color = SettingsSectionBg,
                OutlineColor = SettingsBorder
            };

            settingsDisplayModeCurrentButton = new GUIButton(
                new RectTransform(new Vector2(1f, 0.5f), settingsDisplayModeList.RectTransform, Anchor.TopCenter),
                "Current",
                style: null)
            {
                Color = SettingsControlBg,
                HoverColor = SettingsControlHover,
                PressedColor = SettingsControlHover,
                TextColor = SettingsTextMain,
                Font = GUIStyle.SmallFont
            };
            settingsDisplayModeCurrentButton.OnClicked = (_, _) =>
            {
                DebugConsole.Log($"VoiceChatMonitor: dropdown select {displayMode} -> {VoiceBarDisplayMode.Current}");
                SetDisplayMode(VoiceBarDisplayMode.Current);
                SetDisplayModeListVisible(false);
                return true;
            };

            settingsDisplayModeEnhancedButton = new GUIButton(
                new RectTransform(new Vector2(1f, 0.5f), settingsDisplayModeList.RectTransform, Anchor.BottomCenter),
                "Enhanced",
                style: null)
            {
                Color = SettingsControlBg,
                HoverColor = SettingsControlHover,
                PressedColor = SettingsControlHover,
                TextColor = SettingsTextMain,
                Font = GUIStyle.SmallFont
            };
            settingsDisplayModeEnhancedButton.OnClicked = (_, _) =>
            {
                DebugConsole.Log($"VoiceChatMonitor: dropdown select {displayMode} -> {VoiceBarDisplayMode.Enhanced}");
                SetDisplayMode(VoiceBarDisplayMode.Enhanced);
                SetDisplayModeListVisible(false);
                return true;
            };

            var moveHudButton = new GUIButton(
                new RectTransform(new Vector2(0.42f, 0.28f), displaySection.RectTransform, Anchor.BottomRight)
                {
                    RelativeOffset = new Vector2(0f, -0.06f)
                },
                "Move HUD",
                style: null)
            {
                Color = new Color(42, 42, 42, 255),
                HoverColor = new Color(68, 68, 68, 255),
                PressedColor = new Color(88, 88, 88, 255),
                TextColor = SettingsTextMain,
                Font = GUIStyle.SmallFont,
                ToolTip = "Close settings and drag the speaker HUD"
            };
            moveHudButton.OnClicked = (_, _) =>
            {
                StartHudMoveMode();
                return true;
            };

            var pathSection = new GUIFrame(
                new RectTransform(new Vector2(0.94f, 0.36f), frame.RectTransform, Anchor.TopCenter)
                {
                    RelativeOffset = new Vector2(0f, 0.47f)
                }, style: null)
            {
                Color = SettingsSectionBg,
                OutlineColor = SettingsSectionBorder
            };

            new GUITextBlock(
                new RectTransform(new Vector2(0.96f, 0.24f), pathSection.RectTransform, Anchor.TopCenter),
                "Storage",
                font: GUIStyle.SmallFont,
                textAlignment: Alignment.CenterLeft)
            {
                TextColor = SettingsTextDim,
                Padding = new Vector4(GUI.IntScale(8), 0, 0, 0),
                CanBeFocused = false
            };

            new GUITextBlock(
                new RectTransform(new Vector2(0.96f, 0.22f), pathSection.RectTransform, Anchor.TopCenter)
                {
                    RelativeOffset = new Vector2(0f, 0.25f)
                },
                "Settings File Path",
                font: GUIStyle.SmallFont,
                textAlignment: Alignment.CenterLeft)
            {
                TextColor = SettingsTextMain,
                Padding = new Vector4(GUI.IntScale(6), 0, 0, 0),
                CanBeFocused = false
            };

            settingsPathTextBox = new GUITextBox(
                new RectTransform(new Vector2(0.96f, 0.28f), pathSection.RectTransform, Anchor.TopCenter)
                {
                    RelativeOffset = new Vector2(0f, 0.54f)
                },
                settingsPath,
                font: GUIStyle.SmallFont,
                createPenIcon: false)
            {
                Readonly = true,
                OverflowClip = true,
                Wrap = false,
                ToolTip = settingsPath
            };
            settingsPathTextBox.TextBlock.CanBeFocused = false;
            settingsPathTextBox.TextColor = SettingsTextMain;
            settingsPathTextBox.ClampText = true;
            settingsPathTextBox.TextBlock.OverflowClip = true;

            var closeBottomBtn = new GUIButton(
                new RectTransform(new Vector2(0.38f, 0.11f), frame.RectTransform, Anchor.BottomCenter)
                {
                    RelativeOffset = new Vector2(0f, -0.035f)
                },
                TextManager.Get("Close"),
                style: null)
            {
                Color = new Color(42, 42, 42, 255),
                HoverColor = new Color(68, 68, 68, 255),
                PressedColor = new Color(88, 88, 88, 255),
                TextColor = SettingsTextMain
            };
            closeBottomBtn.OnClicked = (_, _) =>
            {
                CloseSettingsMenu();
                return true;
            };

            // Keep the display section above the storage section.
            displaySection.SetAsLastChild();

            RefreshSettingsMenuControls();
        }

        private static string GetDisplayModeLabel(VoiceBarDisplayMode mode)
        {
            return mode == VoiceBarDisplayMode.Enhanced ? "Enhanced" : "Current";
        }

        private static void RepositionDisplayModeList()
        {
            if (settingsDisplayModeList == null || settingsDisplayModeButton == null) { return; }
            Rectangle buttonRect = settingsDisplayModeButton.Rect;
            int itemHeight = Math.Max(GUI.IntScale(28), 24);
            int listHeight = itemHeight * 2;
            int listWidth = Math.Max(buttonRect.Width, Math.Max(180, GUI.IntScale(220)));
            int x = buttonRect.X;
            int y = buttonRect.Bottom + 2;

            int maxX = Math.Max(0, GameMain.GraphicsWidth - listWidth - 4);
            int maxY = Math.Max(0, GameMain.GraphicsHeight - listHeight - 4);
            x = Math.Clamp(x, 0, maxX);
            y = Math.Clamp(y, 0, maxY);

            settingsDisplayModeList.RectTransform.AbsoluteOffset = new Point(x, y);
            settingsDisplayModeList.RectTransform.Resize(new Point(listWidth, listHeight));
            settingsDisplayModeCurrentButton?.RectTransform.Resize(new Point(listWidth, itemHeight));
            settingsDisplayModeEnhancedButton?.RectTransform.Resize(new Point(listWidth, itemHeight));
        }

        private static void SetDisplayModeListVisible(bool visible)
        {
            isSettingsDisplayModeListOpen = visible;
            if (settingsDisplayModeList == null) { return; }
            if (visible)
            {
                RepositionDisplayModeList();
                settingsDisplayModeList.Visible = true;
                settingsDisplayModeList.SetAsLastChild();
            }
            else
            {
                settingsDisplayModeList.Visible = false;
            }
        }

        private static string BuildCenteredEllipsizedText(string text, float maxWidth, GUIFont font)
        {
            if (string.IsNullOrEmpty(text)) { return string.Empty; }
            if (font.MeasureString(text).X <= maxWidth) { return text; }

            const string ellipsis = "...";
            if (font.MeasureString(ellipsis).X >= maxWidth) { return ellipsis; }

            int leftCount = text.Length / 2;
            int rightCount = text.Length - leftCount;

            while (leftCount > 0 && rightCount > 0)
            {
                string candidate = text.Substring(0, leftCount) + ellipsis + text.Substring(text.Length - rightCount, rightCount);
                if (font.MeasureString(candidate).X <= maxWidth) { return candidate; }

                if (leftCount >= rightCount)
                {
                    leftCount--;
                }
                else
                {
                    rightCount--;
                }
            }

            return ellipsis;
        }

        private static void UpdateSettingsPathDisplayText()
        {
            if (settingsPathTextBox == null) { return; }
            float maxTextWidth = Math.Max(18f, settingsPathTextBox.Rect.Width - GUI.IntScale(12));
            string displayText = BuildCenteredEllipsizedText(settingsPath, maxTextWidth, GUIStyle.SmallFont);
            settingsPathTextBox.Text = displayText;
            settingsPathTextBox.ToolTip = settingsPath;
        }

        private static void RefreshSettingsMenuControls()
        {
            if (settingsDisplayModeButton != null)
            {
                settingsDisplayModeButton.Text = GetDisplayModeLabel(displayMode);
            }
            if (settingsDisplayModeCurrentButton != null)
            {
                bool selectedCurrent = displayMode == VoiceBarDisplayMode.Current;
                settingsDisplayModeCurrentButton.Color = selectedCurrent ? new Color(40, 58, 54, 255) : SettingsControlBg;
            }
            if (settingsDisplayModeEnhancedButton != null)
            {
                bool selectedEnhanced = displayMode == VoiceBarDisplayMode.Enhanced;
                settingsDisplayModeEnhancedButton.Color = selectedEnhanced ? new Color(40, 58, 54, 255) : SettingsControlBg;
            }
            UpdateSettingsPathDisplayText();
        }

        private static void OpenSettingsMenu()
        {
            if (GUI.Canvas == null) { return; }
            EnsureSettingsMenuCreated();
            if (settingsMenuRoot == null) { return; }

            if (settingsPathTextBox != null)
            {
                UpdateSettingsPathDisplayText();
            }
            RefreshSettingsMenuControls();
            SetDisplayModeListVisible(false);

            settingsMenuRoot.Visible = true;
            settingsMenuRoot.AddToGUIUpdateList(order: 2);
            DebugConsole.Log("VoiceChatMonitor: opening SpeakerList settings menu");
        }

        private static void CloseSettingsMenu()
        {
            if (settingsMenuRoot == null) { return; }
            SetDisplayModeListVisible(false);
            settingsMenuRoot.Visible = false;
        }

        private static void RemoveSettingsMenu()
        {
            if (settingsMenuRoot == null) { return; }
            GUI.RemoveFromUpdateList(settingsMenuRoot, true);
            settingsMenuRoot.RectTransform.Parent = null;
            settingsMenuRoot = null;
            settingsPathTextBox = null;
            settingsDisplayModeButton = null;
            settingsDisplayModeList = null;
            settingsDisplayModeCurrentButton = null;
            settingsDisplayModeEnhancedButton = null;
            isSettingsDisplayModeListOpen = false;
        }
    }
#endif
}
