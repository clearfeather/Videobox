#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using LibVLCSharp.Shared;

namespace Screenbox.Core.Services
{
    public static class LogService
    {
        public static void Log(object? message, [CallerMemberName] string? source = default)
        {
            Debug.WriteLine($"[{DateTime.Now.ToString(CultureInfo.CurrentCulture)} - {source}]: {message}");
            if (message is Exception e) TrackError(e);
        }

        [Conditional("DEBUG")]
        public static void RegisterLibVlcLogging(LibVLC libVlc)
        {
            libVlc.Log -= LibVLC_Log;
            libVlc.Log += LibVLC_Log;
        }

        private static void LibVLC_Log(object sender, LogEventArgs e)
        {
            Log(e.FormattedLog, "LibVLC");
        }

        [Conditional("DEBUG")]
        private static void TrackError(Exception e)
        {
            Debug.WriteLine(e);
        }
    }
}
