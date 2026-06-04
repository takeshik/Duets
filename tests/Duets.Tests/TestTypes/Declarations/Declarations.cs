namespace Duets.Tests.TestTypes.Declarations;

public interface IDeclarationExtensionTarget
{
    public int Value { get; }
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

    public Dictionary<string, int> Scores { get; set; } = [];

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
    public string Name { get; }

    public int Count(int seed);
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

public class ConstructorSample(string name, int count) { }

public class TypeMappingSample
{
    public bool Flag { get; set; }
    public Dictionary<Guid, string> WeirdMap { get; set; } = [];

    public static void StaticOp(bool enabled) { }

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
