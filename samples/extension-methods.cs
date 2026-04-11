// Extension methods and CLR-array conversion
//
// Demonstrates:
// - registering CLR extension methods at script runtime via typings.addExtensionMethods(...)
// - calling LINQ extension methods with instance syntax on CLR arrays
// - converting a CLR array back to a native JavaScript array via util.toJsArray(...)
#:project ../src/Duets/Duets.csproj

using Duets;
using Jint;

using var session = await DuetsSession.CreateAsync(opts => opts.AllowClr());
session.RegisterTypeBuiltins();
session.ConsoleLogged += entry => Console.WriteLine(entry.Text);
session.SetValue("numbers", new[] { 1, 2, 3 });

session.Execute("""
    typings.addExtensionMethods("System.Linq.Enumerable, System.Linq");

    // CLR array -> LINQ extension methods via instance syntax
    var doubledClrArray = numbers.Select(x => x * 2).ToArray();
    console.log("CLR array length:", doubledClrArray.Length);

    // Explicit escape hatch when native JS-array behavior is wanted
    var doubledJsArray = util.toJsArray(doubledClrArray);
    console.log("Is JS array:", Array.isArray(doubledJsArray));
    console.log("Doubled:", util.inspect(doubledJsArray, { compact: true }));
    """);
