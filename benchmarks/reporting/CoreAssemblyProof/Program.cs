using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

// Ignores build-specific PDB paths/MVID; checks emitted method bodies and signatures.
var proofs = args.Select(path =>
{
    using var stream = File.OpenRead(path);
    using var pe = new PEReader(stream);
    var metadata = pe.GetMetadataReader();
    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    void Text(string value) => hash.AppendData(Encoding.UTF8.GetBytes(value + "\n"));
    int methods = 0;
    foreach (var handle in metadata.TypeDefinitions)
    {
        var type = metadata.GetTypeDefinition(handle);
        Text(metadata.GetString(type.Namespace) + "." + metadata.GetString(type.Name));
        foreach (var methodHandle in type.GetMethods())
        {
            var method = metadata.GetMethodDefinition(methodHandle);
            Text(metadata.GetString(method.Name));
            hash.AppendData(metadata.GetBlobBytes(method.Signature));
            if (method.RelativeVirtualAddress != 0)
            {
                var body = pe.GetMethodBody(method.RelativeVirtualAddress);
                Text(body.MaxStack + ":" + body.LocalVariablesInitialized);
                hash.AppendData(body.GetILBytes()!);
            }
            methods++;
        }
    }
    return new { Path = Path.GetFullPath(path), Methods = methods, IlSignatureSha256 = Convert.ToHexStringLower(hash.GetHashAndReset()) };
});
Console.WriteLine(JsonSerializer.Serialize(proofs, new JsonSerializerOptions { WriteIndented = true }));
