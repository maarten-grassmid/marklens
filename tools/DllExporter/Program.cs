// Post-build step: turns static methods marked with [DllExport("name")] into
// native PE exports so Notepad++ can load the managed assembly as a plugin.
using dnlib.DotNet;
using dnlib.DotNet.Writer;

if (args.Length != 1)
{
    Console.Error.WriteLine("usage: DllExporter <assembly.dll>");
    return 2;
}

var path = Path.GetFullPath(args[0]);
var module = ModuleDefMD.Load(File.ReadAllBytes(path));

int exported = 0;
foreach (var type in module.GetTypes())
{
    foreach (var method in type.Methods)
    {
        var attr = method.CustomAttributes.FirstOrDefault(a => a.TypeFullName.EndsWith(".DllExportAttribute"));
        if (attr is null)
            continue;
        if (!method.IsStatic)
        {
            Console.Error.WriteLine($"error: {method.FullName} must be static to be exported");
            return 1;
        }

        var name = attr.ConstructorArguments.Count > 0 && attr.ConstructorArguments[0].Value is UTF8String s
            ? s.String
            : method.Name.String;

        method.ExportInfo = new MethodExportInfo(name);
        method.IsUnmanagedExport = true;
        method.CustomAttributes.Remove(attr);
        exported++;
        Console.WriteLine($"  export {name} -> {type.FullName}::{method.Name}");
    }
}

if (exported == 0)
{
    Console.Error.WriteLine("error: no [DllExport] methods found");
    return 1;
}

var options = new ModuleWriterOptions(module);
options.Cor20HeaderOptions.Flags = (options.Cor20HeaderOptions.Flags ?? module.Cor20HeaderFlags) & ~dnlib.DotNet.MD.ComImageFlags.ILOnly;
options.PEHeadersOptions.Machine = dnlib.PE.Machine.AMD64;
options.PEHeadersOptions.Characteristics = dnlib.PE.Characteristics.Dll
    | dnlib.PE.Characteristics.ExecutableImage
    | dnlib.PE.Characteristics.LargeAddressAware;

var tmp = path + ".tmp";
module.Write(tmp, options);
module.Dispose();
File.Copy(tmp, path, overwrite: true);
File.Delete(tmp);

Console.WriteLine($"DllExporter: {exported} export(s) written to {Path.GetFileName(path)}");
return 0;
