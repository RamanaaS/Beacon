using System.Runtime.InteropServices;

namespace Beacon.Infrastructure.Alerts;

public sealed record ParsedLogLine(string Severity, string Source, string AlertName, string Instance, string Message);

/// <summary>
/// P/Invoke wrapper around native/log-parser-shim (C, MinGW-built) — sits on the raw-alert-
/// ingestion path so a high-volume raw webhook body can be parsed without .NET's Regex/string-
/// split allocation overhead, the same reason real log-shipping tools like Fluent Bit and Vector
/// keep their hot parsing path native. See native/log-parser-shim/log_parser.h for the wire
/// format and native/log-parser-shim/test_log_parser.c for the throughput evidence
/// (~22M lines/sec single-threaded on this dev machine).
///
/// Windows-only for now: log_parser_shim.dll is built via MinGW gcc targeting win-x64, matching
/// this project's existing Windows-only native dependency (Phase 6's WindowsFontResolver) —
/// cross-compiling a Linux build is real, deferred work for Phase 15/17's containerization, not
/// silently assumed away.
/// </summary>
public static class LogParserShim
{
    private const string LibraryName = "log_parser_shim";

    public static ParsedLogLine? Parse(string rawLine)
    {
        var native = new ParsedLogLineNative();
        parse_log_line(rawLine, ref native);

        return native.Success == 1
            ? new ParsedLogLine(native.Severity, native.Source, native.AlertName, native.Instance, native.Message)
            : null;
    }

    // charset/layout has to match native/log-parser-shim/log_parser.h's ParsedLogLine exactly -
    // five 256-byte ANSI char buffers in this order, then a 4-byte int. Marshalling this as a
    // fixed-layout struct (rather than returning pointers into native-owned memory) avoids a
    // cross-boundary free(): the .NET side owns this memory the whole time, nothing native has to
    // be freed after the call returns.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct ParsedLogLineNative
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Severity;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Source;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string AlertName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Instance;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Message;

        public int Success;
    }

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern void parse_log_line(string line, ref ParsedLogLineNative result);
}
