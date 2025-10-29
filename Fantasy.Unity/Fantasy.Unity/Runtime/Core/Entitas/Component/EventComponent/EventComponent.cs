using System;
using System.Collections.Generic;
using Fantasy.Assembly;
using Fantasy.Async;
using Fantasy.DataStructure.Collection;
using Fantasy.Entitas;
// ReSharper disable MethodOverloadWithOptionalParameter

namespace Fantasy.Event
{
    /// <summary>
    /// 事件组件系统，负责管理和调度所有事件
    /// </summary>
    public sealed class EventComponent : Entity, IAssemblyLifecycle
    {
        private readonly HashSet<long> _assemblyManifests = new();
        private readonly OneToManyList<RuntimeTypeHandle, IEvent> _events = new();
        private readonly OneToManyList<RuntimeTypeHandle, IEvent> _asyncEvents = new();

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
            _events.Clear();
            _asyncEvents.Clear();
            AssemblyLifecycle.Remove(this);
            base.Dispose();
        }

        #region AssemblyManifest

        /// <summary>
        /// 初始化EventComponent，将其注册到程序集系统中
        /// </summary>
        /// <returns>返回初始化后的EventComponent实例</returns>
        internal async FTask<EventComponent> Initialize()
        {
            await AssemblyLifecycle.Add(this);
            return this;
        }
        
        /// <summary>
        /// 加载程序集，注册该程序集中的所有事件系统
        /// 支持热重载：如果程序集已加载，会先卸载再重新加载
        /// </summary>
        /// <param name="assemblyManifest">程序集清单对象，包含程序集的元数据和注册器</param>
        /// <returns>异步任务</returns>
        public async FTask OnLoad(AssemblyManifest assemblyManifest)
        {
            var tcs = FTask.Create(false);
            var assemblyManifestId = assemblyManifest.AssemblyManifestId;
            Scene?.ThreadSynchronizationContext.Post(() =>
            {
                // 如果程序集已加载，先卸载旧的
                if (_assemblyManifests.Contains(assemblyManifestId))
                {
                    OnUnLoadInner(assemblyManifest);
                }
                assemblyManifest.EventSystemRegistrar.Register(
                    _events,
                    _asyncEvents);
                _assemblyManifests.Add(assemblyManifestId);
                tcs.SetResult();
            });
            await tcs;
        }
        
        /// <summary>
        /// 卸载程序集，取消注册该程序集中的所有实体系统
        /// </summary>
        /// <param name="assemblyManifest">程序集清单对象，包含程序集的元数据和注册器</param>
        /// <returns>异步任务</returns>
        public async FTask OnUnload(AssemblyManifest assemblyManifest)
        {
            var tcs = FTask.Create(false);
            Scene?.ThreadSynchronizationContext.Post(() =>
            {
                OnUnLoadInner(assemblyManifest);
                tcs.SetResult();
            });
            await tcs;
        }
        
        /// <summary>
        /// 卸载程序集的内部实现
        /// 会清理该程序集注册的所有系统
        /// </summary>
        /// <param name="assemblyManifest">程序集清单对象，包含程序集的元数据和注册器</param>
        private void OnUnLoadInner(AssemblyManifest assemblyManifest)
        {
            assemblyManifest.EventSystemRegistrar.UnRegister(
                _events,
                _asyncEvents);
            _assemblyManifests.Remove(assemblyManifest.AssemblyManifestId);
        }

        #endregion

        #region Publish

        /// <summary>
        /// 发布同步事件（struct类型）
        /// </summary>
        /// <typeparam name="TEventData">事件数据类型（值类型）</typeparam>
        /// <param name="eventData">事件数据</param>
        public void Publish<TEventData>(TEventData eventData) where TEventData : struct
        {
            if (!_events.TryGetValue(typeof(TEventData).TypeHandle, out var list))
            {
                return;
            }

            foreach (var @event in list)
            {
                try
                {
                    ((IEvent<TEventData>)@event).Invoke(eventData);
                }
                catch (Exception e)
                {
                    Log.Error(e);
                }
            }
        }

        /// <summary>
        /// 发布同步事件（Entity类型）
        /// </summary>
        /// <typeparam name="TEventData">事件数据类型（Entity类型）</typeparam>
        /// <param name="eventData">事件数据</param>
        /// <param name="isDisposed">事件处理完成后是否自动销毁Entity</param>
        public void Publish<TEventData>(TEventData eventData, bool isDisposed = true) where TEventData : Entity
        {
            if (!_events.TryGetValue(typeof(TEventData).TypeHandle, out var list))
            {
                return;
            }

            foreach (var @event in list)
            {
                try
                {
                    // 转换为泛型接口，Entity是引用类型但仍避免虚方法调用开销
                    ((IEvent<TEventData>)@event).Invoke(eventData);
                }
                catch (Exception e)
                {
                    Log.Error(e);
                }
            }

            if (isDisposed)
            {
                eventData.Dispose();
            }
        }
        
        /// <summary>
        /// 发布异步事件（struct类型）
        /// </summary>
        /// <typeparam name="TEventData">事件数据类型（值类型）</typeparam>
        /// <param name="eventData">事件数据</param>
        public async FTask PublishAsync<TEventData>(TEventData eventData) where TEventData : struct
        {
            if (!_asyncEvents.TryGetValue(typeof(TEventData).TypeHandle, out var list))
            {
                return;
            }

            using var tasks = ListPool<FTask>.Create();

            foreach (var @event in list)
            {
                tasks.Add(((IAsyncEvent<TEventData>)@event).InvokeAsync(eventData));
            }

            await FTask.WaitAll(tasks);
        }

        /// <summary>
        /// 发布异步事件（Entity类型）
        /// </summary>
        /// <typeparam name="TEventData">事件数据类型（Entity类型）</typeparam>
        /// <param name="eventData">事件数据</param>
        /// <param name="isDisposed">事件处理完成后是否自动销毁Entity</param>
        public async FTask PublishAsync<TEventData>(TEventData eventData, bool isDisposed = true) where TEventData : Entity
        {
            if (!_asyncEvents.TryGetValue(typeof(TEventData).TypeHandle, out var list))
            {
                return;
            }

            using var tasks = ListPool<FTask>.Create();

            foreach (var @event in list)
            {
                try
                {
                    tasks.Add(((IAsyncEvent<TEventData>)@event).InvokeAsync(eventData));
                }
                catch (Exception e)
                {
                    Log.Error(e);
                }
            }

            await FTask.WaitAll(tasks);

            if (isDisposed)
            {
                eventData.Dispose();
            }
        }

        #endregion
    }
}

