using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using MimeKit.Text;
using TaskYojitsu.Application.Abstractions;

namespace TaskYojitsu.Infrastructure.Email;

/// <summary>Smtp 節。パスワードは秘密情報（smtp_password）から読む。</summary>
public sealed class SmtpOptions
{
    public const string Section = "Smtp";

    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 587;
    public bool UseStartTls { get; set; } = true;
    public string From { get; set; } = "no-reply@localhost";
    public string? UserName { get; set; }
    public string? Password { get; set; }
}

/// <summary>社内メールサーバーへ送る（STARTTLS）。本文はテキストだけで、HTML のメールは送らない。</summary>
public sealed class SmtpEmailSender(IOptions<SmtpOptions> options) : IEmailSender
{
    public async Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken = default)
    {
        var o = options.Value;
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(o.From));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;
        message.Body = new TextPart(TextFormat.Plain) { Text = body };

        using var client = new SmtpClient();
        await client.ConnectAsync(o.Host, o.Port, o.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None, cancellationToken);
        if (!string.IsNullOrEmpty(o.UserName) && !string.IsNullOrEmpty(o.Password))
        {
            await client.AuthenticateAsync(o.UserName, o.Password, cancellationToken);
        }

        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }
}
