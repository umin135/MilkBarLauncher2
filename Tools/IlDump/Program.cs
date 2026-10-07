using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
var pe = new PEReader(File.OpenRead(args[0]));
var md = pe.GetMetadataReader();
var filter = args.Length > 1 ? args[1] : null;
string MemberName(EntityHandle h) {
    try {
        switch (h.Kind) {
            case HandleKind.MethodDefinition: { var m = md.GetMethodDefinition((MethodDefinitionHandle)h); return md.GetString(md.GetTypeDefinition(m.GetDeclaringType()).Name) + "::" + md.GetString(m.Name); }
            case HandleKind.MemberReference: { var m = md.GetMemberReference((MemberReferenceHandle)h); string p = m.Parent.Kind == HandleKind.TypeReference ? md.GetString(md.GetTypeReference((TypeReferenceHandle)m.Parent).Name) : m.Parent.Kind.ToString(); return p + "::" + md.GetString(m.Name); }
            case HandleKind.MethodSpecification: return MemberName(md.GetMethodSpecification((MethodSpecificationHandle)h).Method);
            case HandleKind.FieldDefinition: return "fld " + md.GetString(md.GetFieldDefinition((FieldDefinitionHandle)h).Name);
        }
    } catch {}
    return h.Kind.ToString();
}
foreach (var th in md.TypeDefinitions) {
    var t = md.GetTypeDefinition(th);
    foreach (var mh in t.GetMethods()) {
        var m = md.GetMethodDefinition(mh);
        if (m.RelativeVirtualAddress == 0) continue;
        var body = pe.GetMethodBody(m.RelativeVirtualAddress);
        var il = body.GetILReader();
        var outl = new List<string>();
        while (il.RemainingBytes > 0) {
            int op = il.ReadByte(); if (op == 0xFE) op = 0xFE00 | il.ReadByte();
            switch (op) {
                case 0x72: outl.Add("\"" + md.GetUserString(MetadataTokens.UserStringHandle(il.ReadInt32() & 0xFFFFFF)) + "\""); break;
                case 0x28: case 0x6F: case 0x73: outl.Add("call " + MemberName(MetadataTokens.EntityHandle(il.ReadInt32()))); break;
                case 0x7B: case 0x7D: case 0x7E: case 0x80: outl.Add((op==0x7D||op==0x80?"st ":"ld ") + MemberName(MetadataTokens.EntityHandle(il.ReadInt32()))); break;
                default:
                    // skip operands by opcode size table (subset)
                    if (op is 0x20 or 0x22 or 0x38 or 0x39 or 0x3A or 0x3B or 0x3C or 0x3D or 0x3E or 0x3F or 0x40 or 0x41 or 0x42 or 0x43 or 0x44 or 0x8D or 0x74 or 0x75 or 0xA5 or 0x8C or 0x79 or 0xD0 or 0x7C or 0x7F or 0x29 or 0x27 or 0x70 or 0x71 or 0x81 or 0xA3 or 0xA4 or 0xC2 or 0xC6 or 0x8F or 0xFE06 or 0xFE07 or 0xFE15 or 0xFE16 or 0xFE1C) il.ReadInt32();
                    else if (op is 0x21 or 0x23) il.ReadInt64();
                    else if (op is 0x45) { int n = il.ReadInt32(); for (int i=0;i<n;i++) il.ReadInt32(); }
                    else if (op is >= 0x0E and <= 0x13 or 0x1F or >= 0x2B and <= 0x37 or 0xDE) il.ReadByte();
                    else if (op is 0xDD) il.ReadInt32();
                    else if (op is 0xFE09 or 0xFE0A or 0xFE0B or 0xFE0C or 0xFE0D or 0xFE0E) il.ReadInt16();
                    else if (op is 0xFE12 or 0xFE19) il.ReadByte();
                    break;
            }
        }
        var name = md.GetString(t.Name) + "::" + md.GetString(m.Name);
        var text = string.Join(" | ", outl);
        if (filter == null || text.Contains(filter, StringComparison.OrdinalIgnoreCase) || name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            Console.WriteLine("### " + name + "\n  " + text + "\n");
    }
}
