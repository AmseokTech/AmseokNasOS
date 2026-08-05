//--------------------------//
//--------统一定义全局与节点操作状态---------//
//--------Defines shared global and node operation states--------//
//-------------------------//
namespace Nas.Domain.Operations;

public enum OperationStatus
{
    Queued,
    WaitingForLock,
    Running,
    Cancelling,
    Succeeded,
    Failed,
    Cancelled,
    Interrupted
}
