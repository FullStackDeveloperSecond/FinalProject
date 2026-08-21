using System.Threading.Channels;
using DoSelect.Application.Notifications;

namespace DoSelect.Infrastructure.Email;

/// <summary>
/// In-process, in-memory queue backing <see cref="IEmailDispatchQueue"/>. Messages enqueued here
/// are not persisted — a process restart drops whatever has not yet been read by
/// <see cref="EmailDispatchBackgroundService"/>. That is an accepted tradeoff for now (no Outbox
/// table exists yet); it removes SMTP latency and the exists/doesn't-exist timing leak from the
/// HTTP request path without requiring a schema change.
/// </summary>
public sealed class EmailDispatchChannel : IEmailDispatchQueue
{
    private readonly Channel<EmailMessage> _channel = Channel.CreateUnbounded<EmailMessage>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

    public ChannelReader<EmailMessage> Reader => _channel.Reader;

    public void Enqueue(EmailMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        // Unbounded writer: TryWrite only fails once the channel is completed, which this type
        // never does, so the result is always true.
        _channel.Writer.TryWrite(message);
    }
}
