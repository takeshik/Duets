using Duets.Tests.TestTypes.Declarations;

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
