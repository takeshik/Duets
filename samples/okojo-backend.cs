// Okojo runtime backend
//
// Duets.Okojo provides an alternative JavaScript runtime backed by Okojo.
// Unlike Jint's blanket AllowClr(), Okojo's AllowClrAccess takes an explicit
// list of assemblies that scripts may access, limiting the CLR surface exposed
// to scripts. Duets.Okojo requires .NET 10 or later.
#:project ../src/Duets.Okojo/Duets.Okojo.csproj

using Duets;
using Duets.Okojo;
using Okojo.Reflection;
using Okojo.Runtime;

using var session = await DuetsSession.CreateAsync(
    async decls => await TypeScriptService.CreateAsync(decls, injectStdLib: true),
    config => config.UseOkojo(builder => builder.AllowClrAccess(
        typeof(object).Assembly)));

// typings is registered automatically; the same script API works across backends.
var result = session.Evaluate("""
    typings.usingNamespace("System.IO");
    Directory.GetCurrentDirectory()
    """);
Console.WriteLine(result);
