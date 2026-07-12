// util.inspect
//
// util.inspect formats any value as a readable string (like Node.js util.inspect).
#:project ../../src/Duets.Jint/Duets.Jint.csproj

using Duets;

using var session = await DuetsSession.CreateAsync();
session.ConsoleLogged += entry => Console.WriteLine(entry.Text);

session.Execute(
    """
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

    // To log an intermediate value in an expression chain, define a local tap helper:
    const tap = (v) => { console.log(util.inspect(v)); return v; };
    const doubled = [1, 2, 3]
        .map(x => tap(x) * 2);   // logs 1, 2, 3 as each element is processed
    console.log('doubled:', doubled);
    """
);
