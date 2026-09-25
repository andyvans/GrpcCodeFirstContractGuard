using System.ServiceModel;
using ProtoBuf.Grpc;

namespace Codify.GrpcCodeFirstContractGuard.TestServer.Models;

[ServiceContract]
public interface IGreeterCodeFirstV2
{
    [OperationContract]
    HelloCodeFirstResponse SayHello(HelloCodeFirstRequest request, CallContext callContext);

    [OperationContract]
    IAsyncEnumerable<HelloCodeFirstResponse> SayHelloServerStream(HelloCodeFirstRequest request, CallContext callContext);

    [OperationContract]
    Task<HelloCodeFirstResponse> SayHelloClientStream(IAsyncEnumerable<HelloCodeFirstRequest> requests, CallContext callContext);

    [OperationContract]
    IAsyncEnumerable<HelloCodeFirstResponse> SayHelloDuplexStream(IAsyncEnumerable<HelloCodeFirstRequest> requests, CallContext callContext);

    [OperationContract]
    int GetNameLength(string name, CallContext callContext);

    [OperationContract]
    Task<long> GetNameHash(string name, CallContext callContext);

    [OperationContract]
    Guid CreateId(string name, CallContext callContext);

    [OperationContract]
    DateTime GetServerTime(bool utc, CallContext callContext);

    [OperationContract]
    Task<byte[]> GetNameBytes(string name, CallContext callContext);

    [OperationContract]
    IAsyncEnumerable<string> StreamNames(int count, CallContext callContext);
}