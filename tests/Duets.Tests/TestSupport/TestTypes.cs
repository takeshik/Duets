// Types without a namespace — used to verify top-level declare keywords

using Duets.Tests.TestTypes.Declarations;

public class NoNamespaceClass
{
    public string Name { get; set; } = "";
}

public interface INoNamespaceInterface
{
    int Value { get; }
}

public enum NoNamespaceEnum
{
    X = 0,
    Y = 1,
}

namespace Duets.Tests.TestTypes.Declarations
{
    public interface IDeclarationExtensionTarget
    {
        int Value { get; }
    }

    public class DeclarationExtensionTarget : IDeclarationExtensionTarget
    {
        public int Value { get; set; }
    }

    public class DeclarationBase
    {
        public string BaseName { get; set; } = "";
    }

    public class DeclarationSample : DeclarationBase
    {
        public static int GlobalCount;

        public string this[int index] => this.Names[index];

        public int? OptionalCount { get; }

        public List<string> Names { get; set; } = [];

        public Dictionary<string, int> Scores { get; set; } = new();

        public Task<string> LoadAsync(string value, int? optional)
        {
            return Task.FromResult(value);
        }

        public string Convert(string @default, IReadOnlyList<int> values)
        {
            return $"{@default}:{values.Count}";
        }

        public void Mutate(ref int value)
        {
            value++;
        }
    }

    public interface IDeclarationContract
    {
        string Name { get; }

        int Count(int seed);
    }

    public enum DeclarationMode
    {
        Alpha = 1,
        Beta = 3,
    }

    public class GenericBox<T>
    {
        public T Value { get; set; } = default!;
    }

    public class ConstructorSample
    {
        public ConstructorSample(string name, int count)
        {
        }
    }

    public class TypeMappingSample
    {
        public bool Flag { get; set; }
        public Dictionary<Guid, string> WeirdMap { get; set; } = new();

        public static void StaticOp(bool enabled)
        {
        }

        public Task Run()
        {
            return Task.CompletedTask;
        }

        public DeclarationBase GetBase()
        {
            return new DeclarationBase();
        }
    }

    public class OverloadSample
    {
        public string Format(int value)
        {
            return value.ToString();
        }

        public string Format(long value)
        {
            return value.ToString();
        }
    }
}

namespace Duets.Tests.TestTypes.NamespaceTargets
{
    public class NamespaceAlpha
    {
        public string Name { get; set; } = "";
    }

    public class NamespaceBeta
    {
        public int Value { get; set; }
    }
}

namespace Duets.Tests.TestTypes.Extensions
{
    public class Item
    {
        public string Label { get; set; } = "";
        public int Value { get; set; }
    }

    public static class ItemExtensions
    {
        public static string Describe(this Item item)
        {
            return $"{item.Label}={item.Value}";
        }

        public static Item WithValue(this Item item, int value)
        {
            return new Item { Label = item.Label, Value = value };
        }

        public static TResult Map<TResult>(this Item item, Func<Item, TResult> selector)
        {
            return selector(item);
        }
    }

    public static class ArrayExtensions
    {
        public static T HeadOr<T>(this T[] items, T fallback)
        {
            return items.Length > 0 ? items[0] : fallback;
        }
    }

    public class ArrayFactory
    {
        public int[] MakeNumbers()
        {
            return [4, 5, 6];
        }
    }

    public static class DictionaryExtensions
    {
        public static int CountPlus<TKey, TValue>(this IDictionary<TKey, TValue> items, int extra)
        {
            return items.Count + extra;
        }
    }

    public static class DeclarationExtensions
    {
        public static int DoubleValue(this IDeclarationExtensionTarget target)
        {
            return target.Value * 2;
        }
    }

    public static class ByteArrayExtensions
    {
        public static int FirstPlus(this byte[] items, int extra)
        {
            return items[0] + extra;
        }
    }
}
