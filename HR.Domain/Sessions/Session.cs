using HR.Domain.Common;

namespace HR.Domain.Sessions;

public sealed class ChatSession
{
    public SessionId Id { get; set; } = SessionId.New();
    public UserId OwnerUserId { get; set; } = new(string.Empty);
    public string Title { get; set; } = "New conversation";
    public string PreferredLanguage { get; set; } = "auto"; // en | ar | auto
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<SessionMessage> Messages { get; set; } = new();
}

public sealed class SessionMessage
{
    public long Id { get; set; }
    public SessionId SessionId { get; set; }
    public string Role { get; set; } = string.Empty; // user | assistant | tool
    public string Content { get; set; } = string.Empty;
    public string? CitationsJson { get; set; }
    public RunId? RunId { get; set; }
    public bool Refused { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
