namespace HVTradingBot.Application.Notifications;

/// <summary>An email with a plain-text body and, optionally, an HTML version (sent as multipart/alternative).</summary>
public sealed record EmailMessage(string Subject, string Body, string? HtmlBody = null);
