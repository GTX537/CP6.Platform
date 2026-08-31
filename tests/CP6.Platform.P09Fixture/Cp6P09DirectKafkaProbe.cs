using System.Net;
using System.Net.Sockets;

internal static class Cp6P09DirectKafkaProbe
{
    internal static async Task<bool> CanConnectAnyAsync(
        IReadOnlyList<IPAddress> addresses,
        Func<IPAddress, CancellationToken, Task> connectAsync,
        CancellationToken callerCancellation,
        TimeSpan attemptTimeout)
    {
        ArgumentNullException.ThrowIfNull(addresses);
        ArgumentNullException.ThrowIfNull(connectAsync);
        if (attemptTimeout <= TimeSpan.Zero || attemptTimeout > TimeSpan.FromSeconds(5))
        {
            throw new ArgumentOutOfRangeException(nameof(attemptTimeout));
        }

        callerCancellation.ThrowIfCancellationRequested();
        foreach (var address in addresses)
        {
            callerCancellation.ThrowIfCancellationRequested();
            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(callerCancellation);
            attempt.CancelAfter(attemptTimeout);
            try
            {
                await connectAsync(address, attempt.Token);
                callerCancellation.ThrowIfCancellationRequested();
                return true;
            }
            catch (SocketException)
            {
                // A single failed address does not prove that every resolved address is unreachable.
            }
            catch (OperationCanceledException) when (
                !callerCancellation.IsCancellationRequested && attempt.IsCancellationRequested)
            {
                // A bounded attempt timeout is an unreachable address, not caller cancellation.
            }

            callerCancellation.ThrowIfCancellationRequested();
        }

        return false;
    }
}
