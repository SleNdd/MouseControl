using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace MouseControl.Masking;

// Only extracted copies of our native apphost are updated; source applications are read as data.
internal static class ExecutableResources
{
    internal static byte[]? ReadIcon(string executable)
    {
        var module = LoadLibraryEx(executable, 0, 0x22); // DATAFILE | IMAGE_RESOURCE: no code executes.
        if (module == 0) return null;
        try
        {
            byte[]? group = null;
            EnumNames callback = (m, t, n, p) => { group = Read(m, t, n); return false; };
            EnumResourceNames(module, (nint)14, callback, 0);
            if (group is null || group.Length < 6) return null;
            var count = BitConverter.ToUInt16(group, 4);
            if (count == 0 || group.Length < 6 + count * 14) return null;
            var images = new List<byte[]>();
            for (var i = 0; i < count; i++)
            {
                var image = Read(module, (nint)3, (nint)BitConverter.ToUInt16(group, 6 + i * 14 + 12));
                if (image is null) return null;
                images.Add(image);
            }
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write((ushort)0); writer.Write((ushort)1); writer.Write(count);
            var offset = 6 + count * 16;
            for (var i = 0; i < count; i++)
            {
                writer.Write(group, 6 + i * 14, 8);
                writer.Write(images[i].Length); writer.Write(offset);
                offset = checked(offset + images[i].Length);
            }
            foreach (var image in images) writer.Write(image);
            return stream.ToArray();
        }
        finally { FreeLibrary(module); }
    }

    internal static void Apply(string executable, ProcessMasker.MaskResult mask)
    {
        var icon = mask.IconBytes;
        if (icon.Length < 6 || BitConverter.ToUInt16(icon, 0) != 0 || BitConverter.ToUInt16(icon, 2) != 1)
            throw new InvalidDataException("Invalid icon header.");
        var count = BitConverter.ToUInt16(icon, 4);
        if (count == 0 || icon.Length < 6 + count * 16) throw new InvalidDataException("Invalid icon directory.");
        var images = new List<byte[]>();
        using var groupStream = new MemoryStream();
        using var groupWriter = new BinaryWriter(groupStream);
        groupWriter.Write((ushort)0); groupWriter.Write((ushort)1); groupWriter.Write(count);
        for (var i = 0; i < count; i++)
        {
            var position = 6 + i * 16;
            var size = BitConverter.ToUInt32(icon, position + 8);
            var offset = BitConverter.ToUInt32(icon, position + 12);
            if (size == 0 || offset < 6 + count * 16 || (ulong)offset + size > (ulong)icon.Length)
                throw new InvalidDataException("Invalid icon image bounds.");
            images.Add(icon.AsSpan(checked((int)offset), checked((int)size)).ToArray());
            groupWriter.Write(icon, position, 12); groupWriter.Write(checked((ushort)(i + 1)));
        }

        // Preserve the application's manifest and all unrelated resources.
        var oldResources = ListResources(executable, [3, 14, 16]);
        var update = BeginUpdateResource(executable, false);
        if (update == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            foreach (var old in oldResources)
            {
                var name = old.Name is null ? (nint)old.Id : Marshal.StringToHGlobalUni(old.Name);
                try { Check(UpdateResource(update, (nint)old.Type, name, old.Language, null, 0)); }
                finally { if (old.Name is not null) Marshal.FreeHGlobal(name); }
            }
            for (var i = 0; i < images.Count; i++) Put(update, 3, i + 1, images[i]);
            Put(update, 14, 1, groupStream.ToArray());
            Put(update, 16, 1, VersionResource(mask.ProcessName));
            var handle = update;
            update = 0;
            Check(EndUpdateResource(handle, false));
        }
        finally { if (update != 0) EndUpdateResource(update, true); }
    }

    private sealed record Resource(int Type, ushort Id, string? Name, ushort Language);
    private static List<Resource> ListResources(string executable, int[] types)
    {
        var result = new List<Resource>();
        var module = LoadLibraryEx(executable, 0, 0x22);
        if (module == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            foreach (var type in types)
            {
                EnumNames names = (m, t, n, p) =>
                {
                    var isId = (nuint)n <= ushort.MaxValue;
                    var name = isId ? null : Marshal.PtrToStringUni(n);
                    EnumLanguages languages = (lm, lt, ln, language, lp) =>
                    { result.Add(new Resource(type, isId ? (ushort)n : (ushort)0, name, language)); return true; };
                    EnumResourceLanguages(m, t, n, languages, 0);
                    return true;
                };
                EnumResourceNames(module, (nint)type, names, 0);
            }
        }
        finally { FreeLibrary(module); }
        return result;
    }

    private static byte[]? Read(nint module, nint type, nint name)
    {
        var resource = FindResource(module, name, type);
        if (resource == 0) return null;
        var size = SizeofResource(module, resource);
        var pointer = LockResource(LoadResource(module, resource));
        if (size == 0 || size > 16 * 1024 * 1024 || pointer == 0) return null;
        var data = new byte[size]; Marshal.Copy(pointer, data, 0, data.Length); return data;
    }

    private static void Put(nint update, int type, int id, byte[] data) =>
        Check(UpdateResource(update, (nint)type, (nint)id, 0, data, (uint)data.Length));
    private static void Check(bool success) { if (!success) throw new Win32Exception(Marshal.GetLastWin32Error()); }

    private static byte[] VersionResource(string name)
    {
        var strings = new Dictionary<string, string>
        {
            ["FileDescription"] = name, ["FileVersion"] = "0.5.1.0", ["ProductName"] = "MouseControl",
            ["ProductVersion"] = "0.5.1.0", ["OriginalFilename"] = name + ".exe",
            ["InternalName"] = name, ["CompanyName"] = "SleNdd", ["LegalCopyright"] = "Copyright © SleNdd"
        };
        var table = Node("040904B0", [], 1, 0, strings.Select(pair =>
            Node(pair.Key, Encoding.Unicode.GetBytes(pair.Value + "\0"), 1, (ushort)(pair.Value.Length + 1))).ToArray());
        var stringInfo = Node("StringFileInfo", [], 1, 0, table);
        var translation = Node("Translation", [0x09, 0x04, 0xB0, 0x04], 0, 4);
        var varInfo = Node("VarFileInfo", [], 1, 0, translation);
        using var fixedStream = new MemoryStream();
        using var fixedWriter = new BinaryWriter(fixedStream);
        foreach (uint value in new uint[] { 0xFEEF04BD, 0x10000, 5, 0x10000, 5, 0x10000, 0x3F, 0, 0x40004, 1, 0, 0, 0 })
            fixedWriter.Write(value);
        return Node("VS_VERSION_INFO", fixedStream.ToArray(), 0, 52, stringInfo, varInfo);
    }

    private static byte[] Node(string key, byte[] value, ushort type, ushort valueLength, params byte[][] children)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0); writer.Write(valueLength); writer.Write(type);
        writer.Write(Encoding.Unicode.GetBytes(key + "\0"));
        Align(); writer.Write(value);
        foreach (var child in children) { Align(); writer.Write(child); }
        var length = checked((ushort)stream.Length);
        stream.Position = 0; writer.Write(length);
        return stream.ToArray();
        void Align() { while (stream.Position % 4 != 0) writer.Write((byte)0); }
    }

    private delegate bool EnumNames(nint module, nint type, nint name, nint parameter);
    private delegate bool EnumLanguages(nint module, nint type, nint name, ushort language, nint parameter);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint LoadLibraryEx(string path, nint file, uint flags);
    [DllImport("kernel32.dll")] private static extern bool FreeLibrary(nint module);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumResourceNames(nint module, nint type, EnumNames callback, nint parameter);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumResourceLanguages(nint module, nint type, nint name, EnumLanguages callback, nint parameter);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint FindResource(nint module, nint name, nint type);
    [DllImport("kernel32.dll")] private static extern uint SizeofResource(nint module, nint resource);
    [DllImport("kernel32.dll")] private static extern nint LoadResource(nint module, nint resource);
    [DllImport("kernel32.dll")] private static extern nint LockResource(nint resource);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint BeginUpdateResource(string path, bool deleteExisting);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool UpdateResource(nint update, nint type, nint name, ushort language, byte[]? data, uint size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool EndUpdateResource(nint update, bool discard);
}
