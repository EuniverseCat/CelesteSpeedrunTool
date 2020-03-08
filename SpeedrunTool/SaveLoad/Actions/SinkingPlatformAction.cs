using System.Collections;
using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class SinkingPlatformAction : AbstractEntityAction {
        private readonly Dictionary<EntityID, SinkingPlatform> sinkingPlatforms = new Dictionary<EntityID, SinkingPlatform>();

        public override void OnQuickSave(Level level) {
            sinkingPlatforms.AddRange(level.Entities.FindAll<SinkingPlatform>());
        }

        private void RestoreSinkingPlatformPosition(On.Celeste.SinkingPlatform.orig_ctor_EntityData_Vector2 orig,
            SinkingPlatform self, EntityData data, Vector2 offset) {
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);
            orig(self, data, offset);

            if (IsLoadStart && sinkingPlatforms.ContainsKey(entityId)) {
                self.Add(new Coroutine(SetPosition(self, sinkingPlatforms[entityId])));
            }
        }

        private IEnumerator SetPosition(SinkingPlatform platform, SinkingPlatform savedPlatform) {
            platform.Position = savedPlatform.Position;
            yield break;
        }


        public override void OnClear() {
            sinkingPlatforms.Clear();
        }

        public override void OnLoad() {
            On.Celeste.SinkingPlatform.ctor_EntityData_Vector2 += RestoreSinkingPlatformPosition;
        }

        public override void OnUnload() {
            On.Celeste.SinkingPlatform.ctor_EntityData_Vector2 -= RestoreSinkingPlatformPosition;
        }
    }
}