using DoSelect.Application.Notifications;
using DoSelect.Infrastructure.Email;
using Microsoft.Extensions.Logging;

namespace DoSelect.Infrastructure.Tests;

public sealed class EmailDispatchBackgroundServiceTests
{
    [Fact]
    public async Task ExecuteAsync_WhenSendFails_LogsAMaskedAddressNeverTheFullEmail()
    {
        const string fullAddress = "someone.specific@example.test";
        var queue = new EmailDispatchChannel();
        var sender = new FailingEmailSender();
        var logger = new CapturingLogger<EmailDispatchBackgroundService>();
        var service = new EmailDispatchBackgroundService(queue, sender, logger);

        await service.StartAsync(CancellationToken.None);
        try
        {
            queue.Enqueue(new EmailMessage(fullAddress, "Subject", "Body"));

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (logger.Messages.Count == 0 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        var logged = Assert.Single(logger.Messages);
        Assert.DoesNotContain(fullAddress, logged, StringComparison.Ordinal);
        Assert.Contains(DoSelect.Application.Members.EmailMasking.Mask(fullAddress), logged, StringComparison.Ordinal);
    }

    private sealed class FailingEmailSender : IEmailSender
    {
        public Task<EmailDeliveryResult> SendAsync(
            EmailMessage message,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new EmailDeliveryResult(
                EmailDeliveryStatus.TransientFailure,
                ErrorCode: EmailDeliveryErrorCodes.TransportUnavailable));
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (Messages)
            {
                Messages.Add(formatter(state, exception));
            }
        }
    }
}
