//--------------------------//
//--------定义 etcd 与 NATS 基础设施端点---------//
//--------Defines etcd and NATS infrastructure endpoints--------//
//-------------------------//
namespace Nas.Infrastructure.ClusterServices;

public sealed class ClusterServicesOptions
{
    public const string SectionName = "ClusterServices";

    public required Uri EtcdHealthUrl { get; init; }
    public required Uri NatsHealthUrl { get; init; }
}
