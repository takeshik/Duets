// Script console output
//
// DuetsSession exposes a ConsoleLogged event that fires whenever script code
// calls console.log/warn/error/info/debug. Subscribe to it to route output
// to your application's logging infrastructure.
#:project ../src/Duets/Duets.csproj

using Duets;

using var session = await DuetsSession.CreateAsync();

// Subscribe before executing any code.
session.ConsoleLogged += entry =>
{
    var prefix = entry.Level switch
    {
        ConsoleLogLevel.Warn  => "[warn]  ",
        ConsoleLogLevel.Error => "[error] ",
        ConsoleLogLevel.Info  => "[info]  ",
        ConsoleLogLevel.Debug => "[debug] ",
        _                     => "[log]   ",
    };
    Console.WriteLine(prefix + entry.Text);
};

session.Execute("""
    console.log('hello from script');
    console.warn('something looks off');
    console.error('something went wrong');
    console.info('for your information');
    console.debug('low-level detail');

    // Multiple arguments are joined with a space, matching Node.js behavior.
    console.log('x =', 42, 'y =', [1, 2, 3]);

    // Non-string values are formatted via util.inspect (JSON-like output).
    console.log({ name: 'Alice', scores: [10, 20, 30] });
    """);
