#if FANTASY_NET
using Fantasy.Async;
using Fantasy.InnerMessage;
using Fantasy.Network;
using Fantasy.Network.Interface;
// ReSharper disable InconsistentNaming
// ReSharper disable CheckNamespace

namespace Fantasy.Sphere;

/// <summary>
/// 处理领域事件取消订阅请求的路由消息处理器。
/// 用于远程节点取消订阅本地场景的领域事件。
/// </summary>
internal sealed class I_UnsubscribeSphereEventRequestHandler : RouteRPC<Scene, I_UnsubscribeSphereEventRequest, I_UnsubscribeSphereEventResponse>
{
    protected override async FTask Run(Scene scene, I_UnsubscribeSphereEventRequest request, I_UnsubscribeSphereEventResponse response, Action reply)
    {
        // 验证 RouteId 是否有效
        // RouteId 用于标识订阅者,不能为 0
        if (request.RouteId == 0)
        {
            response.ErrorCode = InnerErrorCode.ErrUnsubscribeSphereEventInvalidRouteId;
            return;
        }

        // 验证 TypeHashCode 是否有效
        // TypeHashCode 是事件类型的哈希值,不能为 0
        if (request.TypeHashCode == 0)
        {
            response.ErrorCode = InnerErrorCode.ErrUnsubscribeSphereEventInvalidTypeHashCode;
            return;
        }

        // 从领域事件组件中移除远程订阅者
        // 移除后,本地场景触发对应类型的领域事件时,不再推送到该订阅者
        scene.SphereEventComponent.UnregisterRemoteSubscriber(request.RouteId, request.TypeHashCode);
        await FTask.CompletedTask;
    }
}
#endif