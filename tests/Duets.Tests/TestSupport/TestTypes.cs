// Types without a namespace — used to verify top-level declare keywords

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
