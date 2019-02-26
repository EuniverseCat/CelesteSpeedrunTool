using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class BadelineBoostAction : AbstractEntityAction {
        private Dictionary<EntityID, Vector2[]> savedNodes = new Dictionary<EntityID, Vector2[]>();
//        private long _lastPlayTime;

        public override void OnQuickSave(Level level) {
            savedNodes = level.Tracker.GetCastEntities<BadelineBoost>().ToDictionary(boost => boost.GetEntityId(),
                boost => {
                    int nodeIndex = (int) boost.GetPrivateField("nodeIndex");
                    Vector2[] nodes = boost.GetPrivateField("nodes") as Vector2[];
                    Vector2[] result = nodes?.Skip(nodeIndex).ToArray();
                    return result ?? new Vector2[] { };
                });
        }

        private static void AttachEntityId(On.Celeste.BadelineBoost.orig_ctor_EntityData_Vector2 orig,
            BadelineBoost self, EntityData data, Vector2 offset) {
            self.SetEntityId(data);
            orig(self, data, offset);
        }

        private void RestoreBadelineBoostState(On.Celeste.BadelineBoost.orig_ctor_Vector2Array_bool orig,
            BadelineBoost self, Vector2[] nodes, bool lockCamera) {
            EntityID entityId = self.GetEntityId();
            if (IsLoadStart) {
                if (savedNodes.ContainsKey(entityId)) {
                    Vector2[] savedNodes = this.savedNodes[entityId];
                    if (savedNodes.Length == 0) {
                        orig(self, nodes.Skip(nodes.Length - 1).ToArray(), false);
                    }
                    else {
                        orig(self, savedNodes, savedNodes.Length != 1);
                    }
                }
                else {
                    orig(self, nodes.Skip(nodes.Length - 1).ToArray(), false);
                }
            }
            else {
                orig(self, nodes, lockCamera);
            }
        }

        private static void FixMultipleTriggers(On.Celeste.BadelineBoost.orig_OnPlayer orig, BadelineBoost self,
            Player player) {
            if (player.SceneAs<Level>().Frozen) {
                return;
            }

            orig(self, player);
        }

        public override void OnClear() {
            savedNodes.Clear();
        }

        public override void OnLoad() {
            On.Celeste.BadelineBoost.ctor_EntityData_Vector2 += AttachEntityId;
            On.Celeste.BadelineBoost.ctor_Vector2Array_bool += RestoreBadelineBoostState;
            On.Celeste.BadelineBoost.OnPlayer += FixMultipleTriggers;
        }

        public override void OnUnload() {
            On.Celeste.BadelineBoost.ctor_EntityData_Vector2 -= AttachEntityId;
            On.Celeste.BadelineBoost.ctor_Vector2Array_bool -= RestoreBadelineBoostState;
            On.Celeste.BadelineBoost.OnPlayer -= FixMultipleTriggers;
        }

        public override void OnInit() {
            typeof(BadelineBoost).AddToTracker();
        }
    }
}