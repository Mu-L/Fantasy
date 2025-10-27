using System;
using System.Collections.Generic;
using Fantasy.Assembly;
using Fantasy.Async;
using Fantasy.Entitas.Interface;
// ReSharper disable ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
// ReSharper disable ForCanBeConvertedToForeach
#pragma warning disable CS0649 // Field is never assigned to, and will always have its default value
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
#pragma warning disable CS8602 // Dereference of a possibly null reference.

namespace Fantasy.Entitas
{
    /// <summary>
    /// 更新队列节点，用于存储需要每帧更新的实体信息
    /// </summary>
    internal sealed class UpdateQueueNode
    {
        /// <summary>
        /// 实体类型
        /// </summary>
        public long TypeHashCode;
        /// <summary>
        /// 实体运行时ID
        /// </summary>
        public long RunTimeId;
    }
    /// <summary>
    /// 实体组件系统管理器，负责管理所有实体的生命周期和系统调度
    /// 支持程序集热重载、实体生命周期事件和每帧更新循环
    /// </summary>
#if FANTASY_UNITY
    public sealed class EntityComponent : Entity, ISceneUpdate, ISceneLateUpdate, IAssemblyLifecycle
#else
    public sealed class EntityComponent : Entity, ISceneUpdate, IAssemblyLifecycle
#endif
    {
        /// <summary>
        /// 已加载的程序集清单ID集合
        /// </summary>
        private readonly HashSet<long> _assemblyManifests = new();
        /// <summary>
        /// 实体唤醒系统字典，Key为实体类型TypeHashCode，Value为对应的唤醒系统
        /// </summary>
        private readonly Dictionary<long, Action<Entity>> _awakeSystems = new();
        /// <summary>
        /// 实体更新系统字典，Key为实体类型TypeHashCode，Value为对应的更新系统
        /// </summary>
        private readonly Dictionary<long, Action<Entity>> _updateSystems = new();
        /// <summary>
        /// 实体销毁系统字典，Key为实体类型TypeHashCode，Value为对应的销毁系统
        /// </summary>
        private readonly Dictionary<long, Action<Entity>> _destroySystems = new();
        /// <summary>
        /// 实体反序列化系统字典，Key为实体类型TypeHashCode，Value为对应的反序列化系统
        /// </summary>
        private readonly Dictionary<long, Action<Entity>> _deserializeSystems = new();
        /// <summary>
        /// 更新队列，使用链表实现循环遍历和O(1)删除
        /// </summary>
        private readonly LinkedList<UpdateQueueNode> _updateQueue = new();
        /// <summary>
        /// 更新节点字典，Key为实体RuntimeId，Value为对应的链表节点，用于快速查找和删除
        /// </summary>
        private readonly Dictionary<long, LinkedListNode<UpdateQueueNode>> _updateNodes = new();
#if FANTASY_UNITY
        private readonly Dictionary<long, Action<Entity>> _lateUpdateSystems = new();
        /// <summary>
        /// Late更新队列，使用链表实现循环遍历和O(1)删除
        /// </summary>
        private readonly LinkedList<UpdateQueueNode> _lateUpdateQueue = new();
        /// <summary>
        /// Late更新节点字典，Key为实体RuntimeId，Value为对应的链表节点，用于快速查找和删除
        /// </summary>
        private readonly Dictionary<long, LinkedListNode<UpdateQueueNode>> _lateUpdateNodes = new();
#endif
        /// <summary>
        /// 销毁时会清理组件里的所有数据
        /// </summary>
        public override void Dispose()
        {
            if (IsDisposed)
            {
                return;
            }

            _assemblyManifests.Clear();
            _awakeSystems.Clear();
            _updateSystems.Clear();
            _destroySystems.Clear();

            _updateQueue.Clear();
            _updateNodes.Clear();
#if FANTASY_UNITY
            _lateUpdateSystems.Clear();
            _lateUpdateQueue.Clear();
            _lateUpdateNodes.Clear();
#endif
            AssemblyLifecycle.Remove(this);
            base.Dispose();
        }

        #region AssemblyManifest
        
        /// <summary>
        /// 初始化EntityComponent，将其注册到程序集系统中
        /// </summary>
        /// <returns>返回初始化后的EntityComponent实例</returns>
        internal async FTask<EntityComponent> Initialize()
        {
            await AssemblyLifecycle.Add(this);
            return this;
        }

        /// <summary>
        /// 加载程序集，注册该程序集中的所有实体系统
        /// 支持热重载：如果程序集已加载，会先卸载再重新加载
        /// </summary>
        /// <param name="assemblyManifest">程序集清单对象，包含程序集的元数据和注册器</param>
        /// <returns>异步任务</returns>
        public FTask OnLoad(AssemblyManifest assemblyManifest)
        {
            var task = FTask.Create(false);
            var assemblyManifestId = assemblyManifest.AssemblyManifestId;
            Scene?.ThreadSynchronizationContext.Post(() =>
            {
                // 如果程序集已加载，先卸载旧的
                if (_assemblyManifests.Contains(assemblyManifestId))
                {
                    OnUnLoadInner(assemblyManifest);
                }
#if FANTASY_NET
                assemblyManifest.EntitySystemRegistrar.RegisterSystems(
                    _awakeSystems,
                    _updateSystems,
                    _destroySystems,
                    _deserializeSystems);
#endif
#if FANTASY_UNITY
                assemblyManifest.EntitySystemRegistrar.RegisterSystems(
                    _awakeSystems,
                    _updateSystems,
                    _destroySystems,
                    _deserializeSystems,
                    _lateUpdateSystems);
#endif
                _assemblyManifests.Add(assemblyManifestId);
                task.SetResult();
            });
            return task;
        }
        
        /// <summary>
        /// 卸载程序集，取消注册该程序集中的所有实体系统
        /// </summary>
        /// <param name="assemblyManifest">程序集清单对象，包含程序集的元数据和注册器</param>
        /// <returns>异步任务</returns>
        public FTask OnUnload(AssemblyManifest assemblyManifest)
        {
            var task = FTask.Create(false);
            Scene?.ThreadSynchronizationContext.Post(() =>
            {
                OnUnLoadInner(assemblyManifest);
                task.SetResult();
            });
            return task;
        }

        /// <summary>
        /// 卸载程序集的内部实现
        /// 会清理该程序集注册的所有系统，并移除更新队列中对应的实体
        /// </summary>
        /// <param name="assemblyManifest">程序集清单对象，包含程序集的元数据和注册器</param>
        private void OnUnLoadInner(AssemblyManifest assemblyManifest)
        {
#if FANTASY_NET
            assemblyManifest.EntitySystemRegistrar.UnRegisterSystems(
                _awakeSystems,
                _updateSystems,
                _destroySystems,
                _deserializeSystems);
#endif
#if FANTASY_UNITY
            assemblyManifest.EntitySystemRegistrar.UnRegisterSystems(
                _awakeSystems,
                _updateSystems,
                _destroySystems,
                _deserializeSystems,
                _lateUpdateSystems);
#endif
            _assemblyManifests.Remove(assemblyManifest.AssemblyManifestId);
            // 清理更新队列中已失效的节点（系统被卸载后，对应实体的更新系统不再存在）
            var node = _updateQueue.First;
            while (node != null)
            {
                var next = node.Next;
                if (!_updateSystems.ContainsKey(node.Value.TypeHashCode))
                {
                    _updateQueue.Remove(node);
                    _updateNodes.Remove(node.Value.RunTimeId);
                }
                node = next;
            }
#if FANTASY_UNITY
            var lateNode = _lateUpdateQueue.First;
            while (lateNode != null)
            {
                var next = lateNode.Next;
                if (!_lateUpdateSystems.ContainsKey(lateNode.Value.TypeHashCode))
                {
                    _lateUpdateQueue.Remove(lateNode);
                    _lateUpdateNodes.Remove(lateNode.Value.RunTimeId);
                }
                lateNode = next;
            }
#endif
        }

        #endregion

        #region Event

        /// <summary>
        /// 触发实体的唤醒事件，调用对应的AwakeSystem
        /// </summary>
        /// <param name="entity">需要唤醒的实体</param>
        public void Awake(Entity entity)
        {
            if (!_awakeSystems.TryGetValue(entity.TypeHashCode, out var awakeSystem))
            {
                return;
            }

            try
            {
                awakeSystem(entity);
            }
            catch (Exception e)
            {
                Log.Error($"{entity.Type.FullName} Error {e}");
            }
        }

        /// <summary>
        /// 触发实体的销毁事件，调用对应的DestroySystem
        /// </summary>
        /// <param name="entity">需要销毁的实体</param>
        public void Destroy(Entity entity)
        {
            if (!_destroySystems.TryGetValue(entity.TypeHashCode, out var system))
            {
                return;
            }

            try
            {
                system(entity);
            }
            catch (Exception e)
            {
                Log.Error($"{entity.Type.FullName} Destroy Error {e}");
            }
        }

        /// <summary>
        /// 触发实体的反序列化事件，调用对应的DeserializeSystem
        /// </summary>
        /// <param name="entity">需要反序列化的实体</param>
        public void Deserialize(Entity entity)
        {
            if (!_deserializeSystems.TryGetValue(entity.TypeHashCode, out var system))
            {
                return;
            }

            try
            {
                system(entity);
            }
            catch (Exception e)
            {
                Log.Error($"{entity.Type.FullName} Deserialize Error {e}");
            }
        }

        #endregion

        #region Update

        /// <summary>
        /// 注册实体到每帧更新循环
        /// 实体将在每帧Update时执行对应的UpdateSystem
        /// </summary>
        /// <param name="entity">需要注册更新的实体</param>
        public void RegisterUpdate(Entity entity)
        {
            var typeHashCode = entity.TypeHashCode;
            // 检查该实体类型是否有对应的更新系统
            if (!_updateSystems.ContainsKey(typeHashCode))
            {
                return;
            }

            var runtimeId = entity.RuntimeId;
            // 防止重复注册
            if (_updateNodes.ContainsKey(runtimeId))
            {
                return;
            }

            // 创建节点并加入链表尾部
            var nodeData = new UpdateQueueNode { TypeHashCode = typeHashCode, RunTimeId = runtimeId };
            var node = _updateQueue.AddLast(nodeData);
            _updateNodes.Add(runtimeId, node);
        }

        /// <summary>
        /// 从每帧更新循环中注销实体
        /// 实体将不再执行UpdateSystem
        /// </summary>
        /// <param name="entity">需要注销更新的实体</param>
        public void UnregisterUpdate(Entity entity)
        {
            if (!_updateNodes.Remove(entity.RuntimeId, out var node))
            {
                return;
            }
            
            // 利用链表节点实现O(1)时间复杂度删除
            _updateQueue.Remove(node);
        }

        /// <summary>
        /// 每帧更新循环，遍历所有已注册的实体并调用对应的UpdateSystem
        /// 使用链表实现循环队列，已删除的实体会自动清理
        /// </summary>
        public void Update()
        {
            var scene = Scene;
            var node = _updateQueue.First;
            var count = _updateQueue.Count;

            // 遍历当前所有节点，count确保只遍历本帧的节点
            while (count-- > 0 && node != null)
            {
                var next = node.Next; // 提前保存下一个节点，防止当前节点被删除
                var data = node.Value;

                // 检查更新系统是否存在（可能被热重载卸载）
                if (!_updateSystems.TryGetValue(data.TypeHashCode, out var updateSystem))
                {
                    node = next;
                    continue;
                }

                var entity = scene.GetEntity(data.RunTimeId);

                // 如果实体已销毁，自动清理
                if (entity == null || entity.IsDisposed)
                {
                    _updateQueue.Remove(node);
                    _updateNodes.Remove(data.RunTimeId);
                }
                else
                {
                    try
                    {
                        updateSystem.Invoke(entity);
                    }
                    catch (Exception e)
                    {
                        Log.Error($"Update Error {e}");
                    }
                }

                node = next;
            }
        }

        #endregion

        #region LateUpdate
#if FANTASY_UNITY
        /// <summary>
        /// 注册实体到每帧更新循环
        /// 实体将在每帧LateUUpdate时执行对应的LateUUpdateSystem
        /// </summary>
        /// <param name="entity">需要注册更新的实体</param>
        public void RegisterLateUpdate(Entity entity)
        {
            var typeHashCode = entity.TypeHashCode;
            // 检查该实体类型是否有对应的更新系统
            if (!_lateUpdateSystems.ContainsKey(typeHashCode))
            {
                return;
            }

            var runtimeId = entity.RuntimeId;
            // 防止重复注册
            if (_lateUpdateNodes.ContainsKey(runtimeId))
            {
                return;
            }

            // 创建节点并加入链表尾部
            var nodeData = new UpdateQueueNode { TypeHashCode = typeHashCode, RunTimeId = runtimeId };
            var node = _lateUpdateQueue.AddLast(nodeData);
            _lateUpdateNodes.Add(runtimeId, node);
        }

        /// <summary>
        /// 从每帧更新循环中注销实体
        /// 实体将不再执行LateUpdateSystem
        /// </summary>
        /// <param name="entity">需要注销更新的实体</param>
        public void UnregisterLateUpdate(Entity entity)
        {
            if (!_lateUpdateNodes.Remove(entity.RuntimeId, out var node))
            {
                return;
            }
            
            // 利用链表节点实现O(1)时间复杂度删除
            _lateUpdateQueue.Remove(node);
        }
        
        /// <summary>
        /// 每帧更新循环，遍历所有已注册的实体并调用对应的LateUpdateSystem
        /// 使用链表实现循环队列，已删除的实体会自动清理
        /// </summary>
        public void LateUpdate()
        {
            var scene = Scene;
            var node = _lateUpdateQueue.First;
            var count = _lateUpdateQueue.Count;

            // 遍历当前所有节点，count确保只遍历本帧的节点
            while (count-- > 0 && node != null)
            {
                var next = node.Next; // 提前保存下一个节点，防止当前节点被删除
                var data = node.Value;

                // 检查更新系统是否存在（可能被热重载卸载）
                if (!_lateUpdateSystems.TryGetValue(data.TypeHashCode, out var lateUpdateSystem))
                {
                    node = next;
                    continue;
                }

                var entity = scene.GetEntity(data.RunTimeId);

                // 如果实体已销毁，自动清理
                if (entity == null || entity.IsDisposed)
                {
                    _lateUpdateQueue.Remove(node);
                    _lateUpdateNodes.Remove(data.RunTimeId);
                }
                else
                {
                    try
                    {
                        lateUpdateSystem.Invoke(entity);
                    }
                    catch (Exception e)
                    {
                        Log.Error($"Update Error {e}");
                    }
                }

                node = next;
            }
        }
#endif
        #endregion
    }
}
