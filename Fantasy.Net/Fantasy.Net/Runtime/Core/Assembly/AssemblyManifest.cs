using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Fantasy.Async;
using Fantasy.DataStructure.Collection;
using Fantasy.Entitas;
using Fantasy.Entitas.Interface;

// ReSharper disable ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
#pragma warning disable CS8602 // Dereference of a possibly null reference.
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
#pragma warning disable CS8601 // Possible null reference assignment.
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
#pragma warning disable CS8603 // Possible null reference return.

namespace Fantasy.Assembly
{
    /// <summary>
    /// 程序集清单类
    /// 封装程序集的元数据和各种系统注册器，用于统一管理程序集的生命周期和系统注册
    /// </summary>
    public sealed class AssemblyManifest
    {
        /// <summary>
        /// 程序集唯一标识符
        /// 通过程序集名称的哈希值生成
        /// </summary>
        public long AssemblyManifestId { get; private set; }

        /// <summary>
        /// 程序集实例
        /// </summary>
        public System.Reflection.Assembly Assembly { get; private set; }

        /// <summary>
        /// ProtoBuf 序列化类型注册器
        /// </summary>
        internal INetworkProtocolRegistrar NetworkProtocolRegistrar { get; set; }

        /// <summary>
        /// 事件系统注册器
        /// </summary>
        internal IEventSystemRegistrar EventSystemRegistrar { get; set; }

        /// <summary>
        /// 实体系统注册器
        /// </summary>
        internal IEntitySystemRegistrar EntitySystemRegistrar { get; set; }

        /// <summary>
        /// 消息分发器注册器
        /// </summary>
        internal IMessageHandlerResolver MessageHandlerResolver { get; set; }

        /// <summary>
        /// 实体类型集合注册器
        /// </summary>
        internal IEntityTypeCollectionRegistrar EntityTypeCollectionRegistrar { get; set; }
        
        /// <summary>
        /// 网络协议 OpCode 解析器接口
        /// </summary>
        internal INetworkProtocolOpCodeResolver NetworkProtocolOpCodeResolver { get; set; }
        
        
        /// <summary>
        /// 网络协议 Response 解析器接口
        /// </summary>
        internal INetworkProtocolResponseTypeResolver NetworkProtocolResponseTypeResolver { get; set; }
        
        /// <summary>
        /// 自定义接口
        /// </summary>
        internal ICustomInterfaceRegistrar CustomInterfaceRegistrar { get; set; }
#if FANTASY_NET
        /// <summary>
        /// 分表注册器
        /// </summary>
        internal ISeparateTableRegistrar SeparateTableRegistrar { get; set; }
        internal ISphereEventRegistrar SphereEventRegistrar { get; set; }
#endif
        private readonly OneToManyList<long, Type> CustomInterfaces = new();
#if FANTASY_WEBGL
        /// <summary>
        /// 程序集清单集合（WebGL 单线程版本）
        /// Key: 程序集唯一标识, Value: 程序集清单对象
        /// </summary>
        private static readonly Dictionary<long, AssemblyManifest> Manifests = new Dictionary<long, AssemblyManifest>();
#else
        /// <summary>
        /// 程序集清单集合（线程安全版本）
        /// Key: 程序集唯一标识, Value: 程序集清单对象
        /// </summary>
        internal static readonly ConcurrentDictionary<long, AssemblyManifest> Manifests = new ConcurrentDictionary<long, AssemblyManifest>();
#endif
        /// <summary>
        /// 清理程序集清单内部资源
        /// 释放所有注册器并清空引用
        /// </summary>
        internal void Clear()
        {
            Assembly = null;
            NetworkProtocolRegistrar = null;
            EventSystemRegistrar = null;
            EntitySystemRegistrar = null;
            MessageHandlerResolver = null;
            EntityTypeCollectionRegistrar = null;
#if FANTASY_NET
            SeparateTableRegistrar = null;
            SphereEventRegistrar = null;
#endif
        }
        
        #region static

#if FANTASY_NET
        /// <summary>
        /// 注册程序集清单
        /// 此方法由 Source Generator 生成的 ModuleInitializer 自动调用
        /// 直接创建并缓存完整的 AssemblyManifest
        /// </summary>
        /// <param name="assemblyManifestId">程序集唯一标识（通过程序集名称哈希生成）</param>
        /// <param name="assembly">程序集实例</param>
        /// <param name="networkProtocolRegistrar">网络协议注册器</param>
        /// <param name="eventSystemRegistrar">事件系统注册器</param>
        /// <param name="entitySystemRegistrar">实体系统注册器</param>
        /// <param name="messageHandlerResolver">消息分发器注册器</param>
        /// <param name="entityTypeCollectionRegistrar">实体类型集合注册器</param>
        /// <param name="separateTableRegistrar">分表注册器</param>
        /// <param name="networkProtocolOpCodeResolver">网络协议 OpCode 解析器接口</param>
        /// <param name="networkProtocolResponseTypeResolver">网络协议 Response 解析器接口</param>
        /// <param name="sphereEventRegistrar">领域事件系统注册器</param>
        /// <param name="customInterfaceRegistrar">自定接口注册器</param>
        public static void Register(
            long assemblyManifestId,
            System.Reflection.Assembly assembly,
            INetworkProtocolRegistrar networkProtocolRegistrar,
            IEventSystemRegistrar eventSystemRegistrar,
            IEntitySystemRegistrar entitySystemRegistrar,
            IMessageHandlerResolver messageHandlerResolver,
            IEntityTypeCollectionRegistrar entityTypeCollectionRegistrar,
            ISeparateTableRegistrar separateTableRegistrar,
            INetworkProtocolOpCodeResolver networkProtocolOpCodeResolver,
            INetworkProtocolResponseTypeResolver networkProtocolResponseTypeResolver,
            ISphereEventRegistrar sphereEventRegistrar,
            ICustomInterfaceRegistrar customInterfaceRegistrar)
        {
            var manifest = new AssemblyManifest
            {
                Assembly = assembly,
                AssemblyManifestId = assemblyManifestId,
                NetworkProtocolRegistrar = networkProtocolRegistrar,
                EventSystemRegistrar = eventSystemRegistrar,
                EntitySystemRegistrar = entitySystemRegistrar,
                MessageHandlerResolver = messageHandlerResolver,
                EntityTypeCollectionRegistrar = entityTypeCollectionRegistrar,
                SeparateTableRegistrar = separateTableRegistrar,
                NetworkProtocolOpCodeResolver = networkProtocolOpCodeResolver,
                NetworkProtocolResponseTypeResolver = networkProtocolResponseTypeResolver,
                SphereEventRegistrar = sphereEventRegistrar,
                CustomInterfaceRegistrar = customInterfaceRegistrar
            };
            
            customInterfaceRegistrar.Register(manifest.CustomInterfaces);
            Manifests.TryAdd(assemblyManifestId, manifest);
            AssemblyLifecycle.OnLoad(manifest).Coroutine();
        }
#endif
#if FANTASY_UNITY
        /// <summary>
        /// 注册程序集清单
        /// 此方法由 Source Generator 生成的 ModuleInitializer 自动调用
        /// 直接创建并缓存完整的 AssemblyManifest
        /// </summary>
        /// <param name="assemblyManifestId">程序集唯一标识（通过程序集名称哈希生成）</param>
        /// <param name="assembly">程序集实例</param>
        /// <param name="networkProtocolRegistrar">网络协议注册器</param>
        /// <param name="eventSystemRegistrar">事件系统注册器</param>
        /// <param name="entitySystemRegistrar">实体系统注册器</param>
        /// <param name="messageHandlerResolver">消息分发器注册器</param>
        /// <param name="entityTypeCollectionRegistrar">实体类型集合注册器</param>
        /// <param name="networkProtocolOpCodeResolver">网络协议 OpCode 解析器接口</param>
        /// <param name="networkProtocolResponseTypeResolver">网络协议 Response 解析器接口</param>
        /// <param name="customInterfaceRegistrar">自定接口注册器</param>
        public static void Register(
            long assemblyManifestId,
            System.Reflection.Assembly assembly,
            INetworkProtocolRegistrar networkProtocolRegistrar,
            IEventSystemRegistrar eventSystemRegistrar,
            IEntitySystemRegistrar entitySystemRegistrar,
            IMessageHandlerResolver messageHandlerResolver,
            IEntityTypeCollectionRegistrar entityTypeCollectionRegistrar,
            INetworkProtocolOpCodeResolver networkProtocolOpCodeResolver,
            INetworkProtocolResponseTypeResolver networkProtocolResponseTypeResolver,
            ICustomInterfaceRegistrar customInterfaceRegistrar)
        {
            var manifest = new AssemblyManifest
            {
                Assembly = assembly,
                AssemblyManifestId = assemblyManifestId,
                NetworkProtocolRegistrar = networkProtocolRegistrar,
                EventSystemRegistrar = eventSystemRegistrar,
                EntitySystemRegistrar = entitySystemRegistrar,
                MessageHandlerResolver = messageHandlerResolver,
                EntityTypeCollectionRegistrar = entityTypeCollectionRegistrar,
                NetworkProtocolOpCodeResolver = networkProtocolOpCodeResolver,
                NetworkProtocolResponseTypeResolver = networkProtocolResponseTypeResolver,
                CustomInterfaceRegistrar = customInterfaceRegistrar
            };
#if FANTASY_WEBGL
            Manifests[assemblyManifestId] = manifest;
#else
            Manifests.TryAdd(assemblyManifestId, manifest);
#endif
            AssemblyLifecycle.OnLoad(manifest).Coroutine();
        }
#endif
        /// <summary>
        /// 取消注册指定程序集的清单
        /// </summary>
        /// <param name="assemblyManifestId">程序集唯一标识</param>
        public static void Unregister(long assemblyManifestId)
        {
#if FANTASY_WEBGL
            if (Manifests.TryGetValue(assemblyManifestId, out var manifest))
            {
                AssemblyLifecycle.OnUnLoad(manifest).Coroutine();
                Manifests.Remove(assemblyManifestId);
                manifest.CustomInterfaceRegistrar.Register(manifest.CustomInterfaces);
            }
#else
            if (Manifests.TryRemove(assemblyManifestId, out var manifest))
            {
                AssemblyLifecycle.OnUnLoad(manifest).Coroutine();
                manifest.CustomInterfaceRegistrar.Register(manifest.CustomInterfaces);
            }
#endif
        }

        /// <summary>
        /// 获取当前框架注册的所有程序集清单
        /// 通过迭代器模式返回所有已注册的程序集清单对象
        /// </summary>
        public static IEnumerable<AssemblyManifest> GetAssemblyManifest
        {
            get
            {
                foreach (var (_, assemblyManifest) in Manifests)
                {
                    yield return assemblyManifest;
                }
            }
        }

        /// <summary>
        /// 获取所有程序集中实现ICustomRegistrar接口中指定类型的所有类型。
        /// </summary>
        /// <returns>所有程序集中实现ICustomRegistrar接口中指定类型的所有类型。</returns>
        public static IEnumerable<Type> ForEach<T>()
        {
            if (!typeof(T).IsInterface)
            {
                yield break;
            }
            
            var hashCode = TypeHashCache<T>.HashCode;
            foreach (var (_, assemblyManifest) in Manifests)
            {
                if (!assemblyManifest.CustomInterfaces.TryGetValue(hashCode, out var customRegistrars))
                {
                    continue;
                }

                foreach (var customRegistrar in customRegistrars)
                {
                    yield return customRegistrar;
                }
            }
        }
        
        /// <summary>
        /// 获取指定程序集中实现指定类型的所有类型。
        /// </summary>
        /// <param name="assemblyManifest">程序集清单。</param>
        /// <returns>指定程序集中实现指定类型的类型。</returns>
        public static IEnumerable<Type> ForEach<T>(AssemblyManifest assemblyManifest)
        {
            if (!typeof(T).IsInterface)
            {
                yield break;
            }


            var hashCode = TypeHashCache<T>.HashCode;
            
            if (!assemblyManifest.CustomInterfaces.TryGetValue(hashCode, out var customRegistrars))
            {
                yield break;
            }
            
            foreach (var type in customRegistrars)
            {
                yield return type;
            }
        }

        /// <summary>
        /// 释放所有程序集清单资源
        /// 卸载所有已注册的程序集，触发卸载事件，清理所有注册器和生命周期回调
        /// </summary>
        /// <returns>异步任务</returns>
        public static async FTask Dispose()
        {
            foreach (var (_, assemblyManifest) in Manifests)
            {
                await AssemblyLifecycle.OnUnLoad(assemblyManifest);
                assemblyManifest.Clear();
            }
            
            Manifests.Clear();
            AssemblyLifecycle.Dispose();
        }

        #endregion
    }
}