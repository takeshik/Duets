namespace Duets.Pad;

/// <summary>
/// Identifies the DuetsPad session for which an <see cref="IAttachmentStorage"/> instance is
/// created.
/// </summary>
/// <param name="SessionId">The owning DuetsPad session identifier.</param>
public sealed record AttachmentStorageContext(Guid SessionId);
