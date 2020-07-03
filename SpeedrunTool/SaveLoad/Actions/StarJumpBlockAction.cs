using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class StarJumpBlockAction : AbstractEntityAction {
        private Dictionary<EntityID, StarJumpBlock> savedStarJumpBlocks = new Dictionary<EntityID, StarJumpBlock>();

        public override void OnQuickSave(Level level) {
            savedStarJumpBlocks = level.Entities.GetDictionary<StarJumpBlock>();
        }

        private void RestoreStarJumpBlockPosition(On.Celeste.StarJumpBlock.orig_ctor_EntityData_Vector2 orig,
            StarJumpBlock self, EntityData data,
            Vector2 offset) {
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);
            orig(self, data, offset);

            if (!IsLoadStart) return;
            
            if (savedStarJumpBlocks.ContainsKey(entityId)) {
                var savedBlock = savedStarJumpBlocks[entityId];
                self.Position = savedBlock.Position;
                self.CopyFields(savedBlock, "sinks", "yLerp", "sinkTimer");
            }
        }

        public override void OnClear() {
            savedStarJumpBlocks.Clear();
        }

        public override void OnLoad() {
            On.Celeste.StarJumpBlock.ctor_EntityData_Vector2 += RestoreStarJumpBlockPosition;
        }

        public override void OnUnload() {
            On.Celeste.StarJumpBlock.ctor_EntityData_Vector2 -= RestoreStarJumpBlockPosition;
        }
    }
}