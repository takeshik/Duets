namespace Duets;

public interface IScriptValueAdapter
{
    bool IsUndefined(object rawValue);

    bool IsNull(object rawValue);

    bool IsObject(object rawValue);

    object? ToObject(object rawValue);

    string ToDisplayString(object rawValue);

    bool AreEqual(object left, object right);

    int GetHashCode(object rawValue);
}
