// Types without a namespace — used to verify top-level declare keywords

using Duets.Tests.TestTypes.Declarations;

public class NoNamespaceClass
{
    public string Name { get; set; } = "";
}

public interface INoNamespaceInterface
{
    public int Value { get; }
}

public enum NoNamespaceEnum
{
    X = 0,
    Y = 1,
}
