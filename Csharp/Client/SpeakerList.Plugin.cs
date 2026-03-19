using Barotrauma;
using HarmonyLib;

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
}
