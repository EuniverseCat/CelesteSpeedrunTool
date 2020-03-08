using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.Component;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class SpringAction : AbstractEntityAction {
        private Dictionary<EntityID, Spring> springs = new Dictionary<EntityID, Spring>();

        public override void OnQuickSave(Level level) {
            springs = level.Entities.GetDictionary<Spring>();
        }

        private void SpringOnCtorEntityDataVector2Orientations(
            On.Celeste.Spring.orig_ctor_EntityData_Vector2_Orientations orig, Spring self, EntityData data,
            Vector2 offset, Spring.Orientations orientation) {
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);
            orig(self, data, offset, orientation);

            if (IsLoadStart) {
                if (springs.ContainsKey(entityId)) {
                    var savedSpring = springs[entityId];
                    var platform = savedSpring.Get<StaticMover>()?.Platform;
                    
                    if (platform is CassetteBlock) {
                        return;
                    }
                    
                    if (platform is FloatySpaceBlock) {
                        self.Add(new RestorePositionComponent(self, savedSpring));
                    }
                    else {
                        self.Position = savedSpring.Position;
                    }
                }
                else {
                    self.Add(new RemoveSelfComponent());
                }
            }
        }

        public override void OnClear() {
            springs.Clear();
        }

        public override void OnLoad() {
            On.Celeste.Spring.ctor_EntityData_Vector2_Orientations += SpringOnCtorEntityDataVector2Orientations;
        }

        public override void OnUnload() {
            On.Celeste.Spring.ctor_EntityData_Vector2_Orientations -= SpringOnCtorEntityDataVector2Orientations;
        }
    }
}