using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.EntityIdPlus;
using Celeste.Mod.SpeedrunTool.SaveLoad.RestoreActions;
using Celeste.Mod.SpeedrunTool.SaveLoad.RestoreActions.Base;
using FMOD.Studio;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad {
    internal static class CopyCore {
        private static readonly HashSet<object> CopyingObjects = new HashSet<object>(new ReferenceEqualityComparer());
        private static readonly Dictionary<object, object> CreatedObjectsDict = new Dictionary<object, object>(new ReferenceEqualityComparer());

        public static void ClearCachedObjects() {
            CopyingObjects.Clear();
            CreatedObjectsDict.Clear();
        }
        
        private class ReferenceEqualityComparer : EqualityComparer<object> {
            public override bool Equals(object x, object y) {
                return ReferenceEquals(x, y);
            }

            public override int GetHashCode(object obj) {
                if (obj == null) return 0;
                // The RuntimeHelpers.GetHashCode method always calls the Object.GetHashCode method non-virtually, 
                // even if the object's type has overridden the Object.GetHashCode method.
                return RuntimeHelpers.GetHashCode(obj);
            }
        }

        public static void DeepCopyFields<T>(object destObj, object sourceObj, bool onlySimpleType = false) {
            DeepCopyFields(destObj, typeof(T), sourceObj, onlySimpleType);
        }

        public static void DeepCopyFields(object destObj, object sourceObj, bool onlySimpleType = false) {
            DeepCopyFields(destObj, destObj.GetType(), sourceObj, onlySimpleType);
        }

        private static void DeepCopyFields(object destObj, Type type, object sourceObj,
            bool onlySimpleType = false) {
            if (destObj.GetType() != sourceObj.GetType()) {
                throw new ArgumentException("destObj and sourceObj not the same type.");
            }

            if (destObj.GetType() == typeof(object) || type == typeof(object)) return;

            const BindingFlags anyDeclaredOnlyFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                                                 BindingFlags.DeclaredOnly;


            if (CopyingObjects.Contains(destObj)) {
                // StackOverflow Exception is watching you.
                $"Prevents copying {destObj} because it is being copied.".Log();
                return;
            }

            CopyingObjects.Add(destObj);

            List<Type> types = new List<Type>();
            while (type.IsSubclassOf(typeof(object))) {
                types.Add(type);
                type = type.BaseType;
            }

            // 可能没有必要，反转顺序从顶层类型也就是父类开始到子类进行复制
            types.Reverse();

            foreach (Type declaringType in types) {
                FieldInfo[] fields = declaringType.GetFieldInfos(anyDeclaredOnlyFlags);

                foreach (FieldInfo fieldInfo in fields) {
                    Type fieldType = fieldInfo.FieldType;
                    string fieldName = fieldInfo.Name;
                    object destValue = destObj.GetField(declaringType, fieldName);
                    object sourceValue = sourceObj.GetField(declaringType, fieldName);

                    if (!fieldType.IsSimple() && onlySimpleType) continue;

                    CopyField(declaringType, fieldType, fieldName, destObj, destValue, sourceValue);
                }
            }

            CopyingObjects.Remove(destObj);
        }

        private static void CopyField(Type currentObjType, Type fieldType, string fieldName, object destObj,
            object destValue, object sourceValue) {
            if (destValue == sourceValue) return;

            if (sourceValue == null) {
                // sourceObj.sourceValue => Component.Entity 为空代表已经未添加到 Entity 中或已经被移除了
                if (currentObjType == typeof(Component) && fieldName == "<Entity>k__BackingField" &&
                    destObj is Component destObjComponent && destObjComponent.Entity != null) {
                    destObjComponent.RemoveSelf();
                    return;
                }

                // Component 需要从 Entity 中移除（适用于第六章 Boss 会在 Sprite 和 PlayerSprite 之间切换，使字段变为 null）
                if (destValue is Component destComponent && destComponent.Entity != null) {
                    destComponent.RemoveSelf();
                }

                // null 也是有意义的
                destObj.SetField(currentObjType, fieldName, null);
                return;
            }

            if (fieldType.IsSimple()) {
                // 简单类型直接复制
                destObj.SetField(currentObjType, fieldName, sourceValue);
            } else if (fieldType.IsList(out Type genericType)) {
                bool destValueIsNull = destValue == null;
                destValue = CopyList(destValue, sourceValue, genericType);
                if (destValueIsNull) {
                    destObj.SetField(currentObjType, fieldName, destValue);
                }
            } else if (fieldType.IsArray && fieldType.GetElementType() is Type elementType) {
                // 一维数组且数量相同
                if (destValue is Array destArray && destArray.Rank == 1 && sourceValue is Array sourceArray &&
                    destArray.Length == sourceArray.Length) {
                    for (int i = 0; i < destArray.Length; i++) {
                        if (elementType.IsSimple()) {
                            destArray.SetValue(sourceArray.GetValue(i), i);
                        } else if (destArray.GetValue(i) != null && sourceArray.GetValue(i) != null) {
                            TryDeepCopyMembers(destArray.GetValue(i), sourceArray.GetValue(i));
                        }
                    }
                }
            } else if (fieldType.IsHashSet(out Type hashElementType)) {
                if (destValue == null) {
                    destValue = Activator.CreateInstance(fieldType);
                    destObj.SetField(currentObjType, fieldName, destValue);
                }

                if (hashElementType.IsSimple()) {
                    // 列表里是简单数据，则清除后全部加入
                    destValue.InvokeMethod("Clear");
                    if (sourceValue is IEnumerable sourceEnumerable) {
                        IEnumerator enumerator = sourceEnumerable.GetEnumerator();
                        while (enumerator.MoveNext()) {
                            destValue.InvokeMethod("Add", enumerator.Current);
                        }
                    }
                } else if (hashElementType.IsSameOrSubclassOf(typeof(Entity))) {
                    // Player.triggersInside Hashset<Trigger>
                    destValue.InvokeMethod("Clear");
                    if (sourceValue is IEnumerable sourceEnumerable) {
                        IEnumerator enumerator = sourceEnumerable.GetEnumerator();
                        while (enumerator.MoveNext()) {
                            if (enumerator.Current.TryFindOrCloneObject() is Entity entity) {
                                destValue.InvokeMethod("Add", entity);
                            } else {
                                ($"Copy HashSet<{hashElementType}> element failed: {enumerator.Current}\n" +
                                 $"destObj={destObj}\tcurrentObjType={currentObjType}\tmemberType={fieldType}\tmemberName={fieldName}\tdestValue={destValue ?? "null"}\tsourceValue={sourceValue}"
                                    ).Log();
                            }
                        }
                    }
                }
            } else if (destValue is Stack<IEnumerator> destEnumerators &&
                       sourceValue is Stack<IEnumerator> sourceEnumerators) {
                // 用于恢复 Coroutine
                Stack<IEnumerator> temp = new Stack<IEnumerator>();
                foreach (IEnumerator functionCall in sourceEnumerators) {
                    if (TryFindOrCloneObject(functionCall) is IEnumerator destFunctionCall) {
                        temp.Push(destFunctionCall);
                    }
                }

                destEnumerators.Clear();
                foreach (IEnumerator enumerator in temp) {
                    destEnumerators.Push(enumerator);
                }
            } else {
                // 复杂类型
                if (destValue != null) {
                    TryDeepCopyMembers(destValue, sourceValue);
                } else {
                    // 为空则根据情况创建新实例或者查找当前场景的实例
                    destValue = TryFindOrCloneObject(sourceValue);
                    if (destValue != null) {
                        destObj.SetField(currentObjType, fieldName, destValue);
                    } else {
                        $"TryFindOrCloneObject Failed: destObj={destObj}\tcurrentObjType={currentObjType}\tmemberType={fieldType}\tmemberName={fieldName}\tdestValue={destValue ?? "null"}\tsourceValue={sourceValue}"
                            .Log();
                    }
                }
            }
        }

        private static object CopyList(object destValue, object sourceValue, Type genericType) {
            if (destValue == null) {
                destValue = Activator.CreateInstance(sourceValue.GetType());
            }

            if (!(destValue is IList destList) || !(sourceValue is IList sourceList)) {
                return destValue;
            }

            if (genericType.IsSimple()) {
                // 列表里是简单数据，则清除后全部假如
                destList.Clear();
                foreach (object obj in sourceList) {
                    destList.Add(obj);
                }
            } else {
                // 列表里是复杂类型
                if (destList.Count == sourceList.Count) {
                    for (int i = 0; i < destList.Count; i++) {
                        TryDeepCopyMembers(destList[i], sourceList[i]);
                    }
                } else {
                    // 数量不一致时
                    // 例如 FinalBoos 的 fallingBlocks
                    destList.Clear();
                    foreach (object o in sourceList) {
                        if (TryFindOrCloneObject(o) is object destElement) {
                            destList.Add(destElement);
                        }
                    }
                }
            }

            return destValue;
        }

        public static void TryDeepCopyMembers(object destValue, object sourceValue) {
            if (sourceValue.IsCompilerGenerated()) {
                DeepCopyFields(destValue, sourceValue);
            } else if (sourceValue is Component) {
                if (sourceValue is StateMachine // only copy some fields later
                    || sourceValue is DustGraphic // sometimes game crash after savestate
                    || sourceValue is VertexLight // switch between room will make light disappear
                    || sourceValue is TalkComponent // TalkComponent.UI 在保存后变为 null 导致重复创建出现两个对话图案
                ) {
                    DeepCopyFields<Component>(destValue, sourceValue);
                } else {
                    DeepCopyFields(destValue, sourceValue);
                }

                switch (destValue) {
                    case StateMachine destMachine when sourceValue is StateMachine sourceMachine:
                        object destCoroutine = destMachine.GetField("currentCoroutine");
                        object sourceCoroutine = sourceMachine.GetField("currentCoroutine");
                        DeepCopyFields(destCoroutine, sourceCoroutine);
                        destMachine.SetField("state", sourceMachine.State);
                        break;
                    case DustGraphic destDustGraphic when sourceValue is DustGraphic sourceDustGraphic:
                        destDustGraphic.EyeDirection = sourceDustGraphic.EyeDirection;
                        destDustGraphic.EyeTargetDirection = sourceDustGraphic.EyeTargetDirection;
                        destDustGraphic.EyeFlip = sourceDustGraphic.EyeFlip;
                        break;
                    case VertexLight destLight when sourceValue is VertexLight sourceLight:
                        destLight.Alpha = sourceLight.Alpha;
                        destLight.Position = sourceLight.Position;
                        destLight.Color = sourceLight.Color;
                        destLight.StartRadius = sourceLight.StartRadius;
                        destLight.EndRadius = sourceLight.EndRadius;
                        break;
                    case Sprite destSprite when sourceValue is Sprite sourceSprite:
                        sourceSprite.InvokeMethod("CloneInto", destSprite);
                        break;
                    case SoundSource destSound
                        when sourceValue is SoundSource sourceSound && sourceSound.LoadPlayingValue():
                        destSound.Play(destSound.EventName);
                        destSound.SetTime(sourceSound);
                        destSound.Pause(); // 先暂停等待 Player 复活完毕再继续播放
                        SoundSourceAction.PlayingSoundSources.Add(destSound);
                        break;
                }
            } else if (destValue is Collider) {
                DeepCopyFields(destValue, sourceValue);
            } else if (destValue is Entity destEntity
                       && sourceValue is Entity sourceEntity
                       ) {
                if (destEntity.GetEntityId2() != sourceEntity.GetEntityId2()) {
                    $"TryDeepCopyMembers: {destEntity} has different EntityId2.".DebugLog();
                }
            }
        }

        public static object TryFindOrCloneObject(this object sourceValue) {
            object destValue = TryFindObject(sourceValue);
            if (destValue != null) return destValue;

            destValue = TryCloneObject(sourceValue);

            if (destValue == null) {
                $"TryFindOrCloneObject Failed: {sourceValue}".Log();
            }

            return destValue;
        }

        private static object TryFindObject(object sourceValue) {
            object destValue = null;
            if (sourceValue is Delegate @delegate && @delegate.Target == null) {
                // If mod Hook a method return IEnumerator will produce something like this.
                // Still not know much about delegate, maybe because the delegate method orig is static.
                destValue = sourceValue;
            } else if (sourceValue is Level) {
                destValue = Engine.Scene.GetLevel();
            } else if (sourceValue is Session) {
                destValue = Engine.Scene.GetSession();
            } else if (sourceValue is Entity entity) {
                if (sourceValue is SolidTiles) {
                    destValue = Engine.Scene.GetLevel()?.SolidTiles;
                } else if (Engine.Scene.FindFirst(entity.GetEntityId2()) is Entity destEntity) {
                    destValue = destEntity;
                }
            } else if (sourceValue is Leader) {
                destValue = Engine.Scene.GetPlayer()?.Leader;
            } else if (sourceValue is Follower savedFollower) {
                Entity followEntity = Engine.Scene.FindFirst(savedFollower.Entity.GetEntityId2());
                if (followEntity == null) {
                    $"Can't find the follower entity: {savedFollower.Entity}".Log();
                    return null;
                }

                FieldInfo followerFieldInfo = followEntity.GetType()
                    .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                    .FirstOrDefault(fieldInfo => fieldInfo.FieldType == typeof(Follower));
                if (followerFieldInfo?.GetValue(followEntity) is Follower follower) {
                    destValue = follower;
                }
            } else if (sourceValue is Holdable sourceHoldable) {
                destValue = (TryFindObject(sourceHoldable.Entity) as Entity)?.Get<Holdable>();
            } else if (sourceValue is Component sourceComponent && sourceComponent.Entity != null &&
                       TryFindObject(sourceComponent.Entity) is Entity sourceComponentEntity) {
                if (sourceComponent.IsFindabel() &&
                    sourceComponentEntity.FindComponent(sourceComponent) is Component foundComponent) {
                    $"Find {sourceValue} instead of recreating a new one".Log(LogLevel.Info);
                    destValue = foundComponent;
                }
            }

            return destValue;
        }

        private static object TryCloneObject(object sourceValue) {
            // 不要把这步移到 TryFindOrCloneObject 开头，钥匙开门时保存会出现重复的门
            if (CreatedObjectsDict.ContainsKey(sourceValue)) {
                return CreatedObjectsDict[sourceValue];
            }

            object destValue = null;
            Type sourceType = sourceValue.GetType();

            if (sourceValue.IsCompilerGenerated()) {
                destValue = sourceValue.CloneCompilerGeneratedObject();
            } else if (sourceValue is Delegate @delegate) {
                destValue = @delegate.CloneDelegate();
            } else if (sourceValue is EventInstance eventInstance) {
                destValue = eventInstance.Clone();
            } else if (sourceValue is DisplacementRenderer.Burst burst) {
                destValue = burst.Clone();
            } else if (sourceValue is Entity sourceEntity) {
                // 还原 Coroutine 时有些 Entity 创建了而未被添加到 Level 中，他们无法在 EntitiesSavedButNotLoaded 中还原，
                // 所以在这里克隆一个但是同样不添加到 Level 中
                destValue = RestoreEntityUtils.CloneEntity(sourceEntity, "TryCloneObject");
            } else if (sourceValue is Component sourceComponent) {
                Component destComponent = null;
                Entity sourceComponentEntityEntity = sourceComponent.Entity;
                Entity destEntity = Engine.Scene.FindFirst(sourceComponentEntityEntity?.GetEntityId2());
                if (sourceValue is BloomPoint sourceBloomPoint) {
                    destComponent = new BloomPoint(sourceBloomPoint.Position, sourceBloomPoint.Alpha,
                        sourceBloomPoint.Radius);
                } else if (sourceValue is PlayerSprite sourcePlayerSprite) {
                    destComponent = new PlayerSprite(sourcePlayerSprite.Mode);
                } else if (sourceValue is PlayerHair sourcePlayerHair) {
                    if (destEntity?.Get<PlayerSprite>() is PlayerSprite destPlayerSprite) {
                        destComponent = new PlayerHair(destPlayerSprite);
                    } else {
                        destPlayerSprite = new PlayerSprite(sourcePlayerHair.Sprite.Mode);
                        TryDeepCopyMembers(destPlayerSprite, sourcePlayerHair.Sprite);
                        destEntity?.Add(destPlayerSprite);
                        destComponent = new PlayerHair(destPlayerSprite);
                    }
                } else if (sourceValue is Sprite sourceSprite) {
                    destComponent = sourceSprite.InvokeMethod("CreateClone") as Sprite;
                } else if (sourceValue is Tween sourceTween) {
                    destComponent = Tween.Create(sourceTween.Mode, sourceTween.Easer, sourceTween.Duration,
                        sourceTween.Active);
                } else if (sourceValue is Alarm sourceAlarm) {
                    destComponent = Alarm.Create(sourceAlarm.Mode, null, sourceAlarm.Duration, sourceAlarm.Active);
                } else if (sourceValue is Image sourceImage) {
                    // TODO 找到更好的还原 Image 方法
                    return new Image(sourceImage.Texture);
                } else if (sourceValue is Coroutine sourceCoroutine) {
                    destComponent = new Coroutine(sourceCoroutine.RemoveOnComplete);
                } else if (sourceValue is Follower sourceFollower) {
                    destComponent = new Follower(sourceFollower.ParentEntityID);
                } else if (sourceValue is TalkComponent sourceTalkComponent) {
                    destComponent = new TalkComponent(sourceTalkComponent.Bounds, sourceTalkComponent.DrawAt,
                        sourceTalkComponent.OnTalk, sourceTalkComponent.HoverUI);
                }

                if (destComponent == null) {
                    destComponent = sourceValue.ForceCreateInstance("TryCloneObject Component") as Component;
                }

                if (destComponent != null) {
                    destEntity?.Add(destComponent);
                }

                destValue = destComponent;
            } else if (sourceValue is MTexture) {
                destValue = sourceValue;
            } else if (sourceType.IsList(out Type genericType)) {
                destValue = CopyList(null, sourceValue, genericType);
            }

            // 尝试给未处理的类型创建实例
            if (destValue == null) {
                destValue = sourceValue.ForceCreateInstance("TryCloneObject End");
            }

            if (destValue != null) {
                CreatedObjectsDict[sourceValue] = destValue;
            }

            if (destValue != null) {
                TryDeepCopyMembers(destValue, sourceValue);
            }

            return destValue;
        }

        private static object CloneCompilerGeneratedObject(Type type) {
            if (!type.IsCompilerGenerated()) return null;
            // Delegate
            object newObj = type.GetConstructor(new Type[] { })?.Invoke(new object[] { });

            // Coroutine
            if (newObj == null) {
                newObj = type.GetConstructor(new[] {typeof(int)})?.Invoke(new object[] {0});
            }

            if (newObj == null) {
                $"CreateCompilerGeneratedCopy Failed: {type}".Log();
                return null;
            }

            foreach (FieldInfo fieldInfo in type.GetFields().Where(info => info.FieldType.IsCompilerGenerated())) {
                object newFieldObj = type == fieldInfo.FieldType
                    ? newObj
                    : CloneCompilerGeneratedObject(fieldInfo.FieldType);
                fieldInfo.SetValue(newObj, newFieldObj);
            }

            return newObj;
        }

        // 编译器自动生成的类型，先创建实例，最后统一复制字段
        private static object CloneCompilerGeneratedObject(this object obj) {
            return CloneCompilerGeneratedObject(obj.GetType());
        }

        private static object CloneDelegate(this Delegate @delegate) {
            if (@delegate.Target == null) return null;
            object target = @delegate.Target.TryFindOrCloneObject();
            return @delegate.Method.CreateDelegate(@delegate.GetType(), target);
        }
    }
}