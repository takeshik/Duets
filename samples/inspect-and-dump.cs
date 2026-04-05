// util.inspect and dump
//
// util.inspect formats any value as a readable string (like Node.js util.inspect).
// dump(value) prints the value to the console and returns it unchanged, so it can
// be inserted anywhere in an expression chain without breaking it.
#:project ../src/Duets/Duets.csproj

using Duets;

var declarations = new TypeDeclarations();
using var ts = new TypeScriptService(declarations);
await ts.ResetAsync();

using var engine = new ScriptEngine(null, ts);
engine.ConsoleLogged += entry => Console.WriteLine(entry.Text);

engine.Execute("""
    // util.inspect — returns a formatted string; does not print anything.
    const formatted = util.inspect({ x: 1, y: [2, 3] });
    console.log(formatted);
    // {
    //   "x": 1,
    //   "y": [
    //     2,
    //     3
    //   ]
    // }

    // opts.compact collapses output to a single line.
    console.log(util.inspect({ x: 1, y: [2, 3] }, { compact: true }));
    // {"x":1,"y":[2,3]}

    // opts.depth controls how deep nested objects are expanded (default: 2).
    // Values deeper than depth are replaced with "[Object]" or "[Array]".
    console.log(util.inspect({ a: { b: { c: { d: 4 } } } }, { depth: 1 }));
    // {"a":"[Object]"}

    // dump(value) — prints via console.log and returns value unchanged.
    // Useful for inspecting intermediate results in an expression chain.
    const doubled = [1, 2, 3]
        .map(x => dump(x) * 2);   // prints 1, 2, 3 as each element is processed
    console.log('doubled:', doubled);

    // Because dump returns its argument, the type is preserved.
    // dump(obj).someProperty works as expected.
    const name = dump({ name: 'Alice' }).name;
    console.log('name:', name);   // name: Alice
    """);
