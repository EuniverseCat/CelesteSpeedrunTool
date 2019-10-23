using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class TalkComponentUIAction : AbstractEntityAction {

        public override void OnQuickSave(Level level) {
        }

        private static void TalkComponentUiOnAwake(On.Celeste.TalkComponent.TalkComponentUI.orig_Awake orig, TalkComponent.TalkComponentUI self, Scene scene) {
            if (self.Handler?.Entity != null && scene != null) {
                orig(self, scene);
            }
        }

        public override void OnClear() {
        }

        public override void OnLoad() {
            On.Celeste.TalkComponent.TalkComponentUI.Awake += TalkComponentUiOnAwake;
        }

        public override void OnUnload() {
            On.Celeste.TalkComponent.TalkComponentUI.Awake -= TalkComponentUiOnAwake;
        }
    }
}