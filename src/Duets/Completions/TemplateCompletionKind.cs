namespace Duets.Completions;

/// <summary>Classifies a host-provided tagged-template completion candidate.</summary>
public enum TemplateCompletionKind
{
    /// <summary>A generic value.</summary>
    Value,

    /// <summary>A file-like leaf.</summary>
    File,

    /// <summary>A folder-like container.</summary>
    Folder,

    /// <summary>A member-like child.</summary>
    Member,
}
