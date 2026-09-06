using System.Runtime.CompilerServices;

namespace Duets;

/// <summary>Engine-neutral wrapper around a JavaScript value.</summary>
public abstract class ScriptValue : IEquatable<ScriptValue>
{
    /// <summary>Gets the engine-neutral JavaScript <c>undefined</c> value.</summary>
    public static ScriptValue Undefined { get; } = new UndefinedValue();

    /// <summary>Gets the engine-neutral JavaScript <c>null</c> value.</summary>
    public static ScriptValue Null { get; } = new NullValue();

    /// <summary>Determines whether two script values are equal.</summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the non-null values come from different backends.
    /// </exception>
    public static bool operator ==(ScriptValue? left, ScriptValue? right)
    {
        return left is null ? right is null : left.Equals(right);
    }

    /// <summary>Determines whether two script values are not equal.</summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the non-null values come from different backends.
    /// </exception>
    public static bool operator !=(ScriptValue? left, ScriptValue? right)
    {
        return !(left == right);
    }

    /// <inheritdoc />
    public abstract override string ToString();

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="obj"/> is a script value from a different backend.
    /// </exception>
    public sealed override bool Equals(object? obj)
    {
        return this.Equals(obj as ScriptValue);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return this is UndefinedValue ? 0 : this.GetHashCodeCore();
    }

    /// <summary>Converts this value to its closest CLR representation.</summary>
    /// <returns>The converted CLR value.</returns>
    public abstract object? ToObject();

    /// <summary>Compares this value with another value from the same backend.</summary>
    /// <param name="other">The value to compare.</param>
    /// <returns><see langword="true"/> when the values are equal.</returns>
    protected virtual bool EqualsCore(ScriptValue other)
    {
        throw new InvalidOperationException("Cannot compare ScriptValues from different backends.");
    }

    /// <summary>Returns the backend-specific hash code for this value.</summary>
    /// <returns>The hash code.</returns>
    protected virtual int GetHashCodeCore()
    {
        return RuntimeHelpers.GetHashCode(this);
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="other"/> is a non-null value from a different backend.
    /// </exception>
    public bool Equals(ScriptValue? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (this is UndefinedValue)
        {
            return other.EqualsCore(this);
        }

        if (this is NullValue)
        {
            return other.EqualsCore(this);
        }

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
