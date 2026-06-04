using System.ServiceModel;
using ProtoBuf.Grpc;

namespace Codify.GrpcCodeFirstContractGuard.TestServer.Models;

[ServiceContract]
public interface IGreeterCodeFirst
{
    [OperationContract]
    HelloCodeFirstResponse SayHello(HelloCodeFirstRequest request, CallContext callContext);

    [OperationContract]
    IAsyncEnumerable<HelloCodeFirstResponse> SayHelloServerStream(HelloCodeFirstRequest request, CallContext callContext);

    [OperationContract]
    Task<HelloCodeFirstResponse> SayHelloClientStream(IAsyncEnumerable<HelloCodeFirstRequest> requests, CallContext callContext);

    [OperationContract]
    IAsyncEnumerable<HelloCodeFirstResponse> SayHelloDuplexStream(IAsyncEnumerable<HelloCodeFirstRequest> requests, CallContext callContext);
}