using System.Runtime.CompilerServices;

namespace Duets;

/// <summary>Engine-neutral wrapper around a JavaScript value.</summary>
public abstract class ScriptValue : IEquatable<ScriptValue>
{
    public static ScriptValue Undefined { get; } = new UndefinedValue();

    public static ScriptValue Null { get; } = new NullValue();

    public static bool operator ==(ScriptValue? left, ScriptValue? right)
    {
        return left is null ? right is null : left.Equals(right);
    }

    public static bool operator !=(ScriptValue? left, ScriptValue? right)
    {
        return !(left == right);
    }

    public abstract override string ToString();

    public sealed override bool Equals(object? obj)
    {
        return this.Equals(obj as ScriptValue);
    }

    public override int GetHashCode()
    {
        return this is UndefinedValue ? 0 : this.GetHashCodeCore();
    }

    public abstract object? ToObject();

    protected virtual bool EqualsCore(ScriptValue other)
    {
        throw new InvalidOperationException("Cannot compare ScriptValues from different backends.");
    }

    protected virtual int GetHashCodeCore()
    {
        return RuntimeHelpers.GetHashCode(this);
    }

    public bool Equals(ScriptValue? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (this is UndefinedValue) return other.EqualsCore(this);
        if (this is NullValue) return other.EqualsCore(this);
        return this.EqualsCore(other);
    }

    private sealed class UndefinedValue : ScriptValue
    {
        public override object? ToObject()
        {
            return null;
        }

        public override string ToString()
        {
            return "undefined";
        }

        protected override bool EqualsCore(ScriptValue other)
        {
            return false;
        }

        protected override int GetHashCodeCore()
        {
            return 0;
        }
    }

    private sealed class NullValue : ScriptValue
    {
        public override object? ToObject()
        {
            return null;
        }

        public override string ToString()
        {
            return "null";
        }

        protected override bool EqualsCore(ScriptValue other)
        {
            return false;
        }

        protected override int GetHashCodeCore()
        {
            return 1;
        }
    }
}
