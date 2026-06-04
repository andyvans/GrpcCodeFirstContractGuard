using Codify.GrpcCodeFirstContractGuard.TestServer.Models;
using ProtoBuf.Grpc;

namespace Codify.GrpcCodeFirstContractGuard.TestServer.Services;

public class GreeterCodeFirstService(ILogger<GreeterCodeFirstService> logger) : IGreeterCodeFirst
{
    public HelloCodeFirstResponse SayHello(HelloCodeFirstRequest request, CallContext callContext)
    {
        logger.LogInformation("Received request: {@Request}", request);

        return new HelloCodeFirstResponse
        {
            Name = request.Name
        };
    }

    public async IAsyncEnumerable<HelloCodeFirstResponse> SayHelloServerStream(HelloCodeFirstRequest request, CallContext callContext)
    {
        logger.LogInformation("Server streaming request: {@Request}", request);

        for (var i = 0; i < 5; i++)
        {
            yield return new HelloCodeFirstResponse
            {
                Name = $"{request.Name} #{i}"
            };

            await Task.Delay(50, callContext.CancellationToken);
        }
    }

    public async Task<HelloCodeFirstResponse> SayHelloClientStream(IAsyncEnumerable<HelloCodeFirstRequest> requests, CallContext callContext)
    {
        var names = new List<string>();

        await foreach (var request in requests.WithCancellation(callContext.CancellationToken))
        {
            logger.LogInformation("Client streaming received: {@Request}", request);
            names.Add(request.Name);
        }

        return new HelloCodeFirstResponse
        {
            Name = string.Join(", ", names)
        };
    }

    public async IAsyncEnumerable<HelloCodeFirstResponse> SayHelloDuplexStream(IAsyncEnumerable<HelloCodeFirstRequest> requests, CallContext callContext)
    {
        await foreach (var request in requests.WithCancellation(callContext.CancellationToken))
        {
            logger.LogInformation("Duplex streaming received: {@Request}", request);

            yield return new HelloCodeFirstResponse
            {
                Name = $"Hello {request.Name}"
            };
        }
    }
}