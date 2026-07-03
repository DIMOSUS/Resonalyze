using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;

#if TRACY_ENABLE
using bottlenoselabs.C2CS.Runtime;
using Tracy;
#endif

namespace Resonalyze;

internal static class AppProfiler
{
#if TRACY_ENABLE
    private static readonly ConcurrentDictionary<SourceLocationKey, SourceLocation> SourceLocations = new();
    private static readonly ConcurrentDictionary<string, CString> StableStrings = new();
#endif

    public static ProfileZone Zone(
        string name,
        [CallerMemberName] string memberName = "",
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0)
    {
#if TRACY_ENABLE
        SourceLocation sourceLocation = SourceLocations.GetOrAdd(
            new SourceLocationKey(name, memberName, filePath, lineNumber),
            CreateSourceLocation);
        return new ProfileZone(PInvoke.TracyEmitZoneBeginAlloc(sourceLocation.Allocate(), 1));
#else
        _ = name;
        _ = memberName;
        _ = filePath;
        _ = lineNumber;
        return default;
#endif
    }

    public static void FrameMark(string name)
    {
#if TRACY_ENABLE
        PInvoke.TracyEmitFrameMark(GetStableString(name));
#else
        _ = name;
#endif
    }

    public static void SetThreadName(string name)
    {
#if TRACY_ENABLE
        PInvoke.TracySetThreadName(GetStableString(name));
#else
        _ = name;
#endif
    }

#if TRACY_ENABLE
    private static SourceLocation CreateSourceLocation(SourceLocationKey key) =>
        new(key);

    private static CString GetStableString(string value) =>
        StableStrings.GetOrAdd(value, static text => (CString)text);

    private readonly record struct SourceLocationKey(
        string Name,
        string MemberName,
        string FilePath,
        int LineNumber);

    private sealed class SourceLocation
    {
        private readonly CString file;
        private readonly CString function;
        private readonly CString name;

        public SourceLocation(SourceLocationKey key)
        {
            file = (CString)key.FilePath;
            function = (CString)key.MemberName;
            name = (CString)key.Name;
            LineNumber = (uint)key.LineNumber;
            FileLength = ByteCount(key.FilePath);
            FunctionLength = ByteCount(key.MemberName);
            NameLength = ByteCount(key.Name);
        }

        private uint LineNumber { get; }

        private ulong FileLength { get; }

        private ulong FunctionLength { get; }

        private ulong NameLength { get; }

        public ulong Allocate() =>
            PInvoke.TracyAllocSrclocName(
                LineNumber,
                file,
                FileLength,
                function,
                FunctionLength,
                name,
                NameLength,
                0);
    }

    private static ulong ByteCount(string value) =>
        (ulong)Encoding.UTF8.GetByteCount(value);

#endif

    internal readonly struct ProfileZone : IDisposable
    {
#if TRACY_ENABLE
        private readonly PInvoke.TracyCZoneCtx context;

        internal ProfileZone(PInvoke.TracyCZoneCtx context)
        {
            this.context = context;
        }
#endif

        public void Dispose()
        {
#if TRACY_ENABLE
            PInvoke.TracyEmitZoneEnd(context);
#endif
        }
    }
}
