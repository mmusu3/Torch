﻿using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using NLog;
using Sandbox.Engine.Multiplayer;
using Torch.Managers.PatchManager;
using Torch.Utils;
using VRage.Utils;

namespace Torch.Patches
{
    [PatchShim]
    internal static class KeenLogPatch
    {
        private static readonly Logger _log = LogManager.GetLogger("Keen");

#pragma warning disable 649
        [ReflectedMethodInfo(typeof(MyLog), nameof(MyLog.Log), Parameters = new[] { typeof(MyLogSeverity), typeof(StringBuilder) })]
        private static MethodInfo _logStringBuilder;

        [ReflectedMethodInfo(typeof(MyLog), nameof(MyLog.Log), Parameters = new[] { typeof(MyLogSeverity), typeof(string), typeof(object[]) })]
        private static MethodInfo _logFormatted;

        [ReflectedMethodInfo(typeof(MyLog), nameof(MyLog.WriteLine), Parameters = new[] { typeof(string) })]
        private static MethodInfo _logWriteLine;

        [ReflectedMethodInfo(typeof(MyLog), nameof(MyLog.AppendToClosedLog), Parameters = new[] { typeof(string) })]
        private static MethodInfo _logAppendToClosedLog;

        [ReflectedMethodInfo(typeof(MyLog), nameof(MyLog.WriteLine), Parameters = new[] { typeof(string), typeof(LoggingOptions) })]
        private static MethodInfo _logWriteLineOptions;

        [ReflectedMethodInfo(typeof(MyLog), nameof(MyLog.WriteLine), Parameters = new[] { typeof(Exception) })]
        private static MethodInfo _logWriteLineException;

        [ReflectedMethodInfo(typeof(MyLog), nameof(MyLog.AppendToClosedLog), Parameters = new[] { typeof(Exception) })]
        private static MethodInfo _logAppendToClosedLogException;

        [ReflectedMethodInfo(typeof(MyLog), nameof(MyLog.WriteLineAndConsole), Parameters = new[] { typeof(string) })]
        private static MethodInfo _logWriteLineAndConsole;

        [ReflectedMethodInfo(typeof(MyMultiplayerServerBase), nameof(MyMultiplayerServerBase.ValidationFailed), Parameters = new[] { typeof(ulong), typeof(bool), typeof(string), typeof(bool) })]
        private static MethodInfo _logSuppressValidationFailed;

#if NET8_0_OR_GREATER
        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "GetIdentByThread")]
        private static extern int _getIndentByThread(MyLog log, int threadId);
#else
        [ReflectedMethod(Name = "GetIdentByThread")]
        private static Func<MyLog, int, int> _getIndentByThread;
#endif
#pragma warning restore 649

        public static void Patch(PatchContext context)
        {
            context.GetPattern(_logStringBuilder).Prefixes.Add(Method(nameof(PrefixLogStringBuilder)));
            context.GetPattern(_logFormatted).Prefixes.Add(Method(nameof(PrefixLogFormatted)));

            context.GetPattern(_logWriteLine).Prefixes.Add(Method(nameof(PrefixWriteLine)));
            context.GetPattern(_logAppendToClosedLog).Prefixes.Add(Method(nameof(PrefixAppendToClosedLog)));
            context.GetPattern(_logWriteLineAndConsole).Prefixes.Add(Method(nameof(PrefixWriteLineConsole)));

            context.GetPattern(_logWriteLineException).Prefixes.Add(Method(nameof(PrefixWriteLineException)));
            context.GetPattern(_logAppendToClosedLogException).Prefixes.Add(Method(nameof(PrefixAppendToClosedLogException)));

            context.GetPattern(_logWriteLineOptions).Prefixes.Add(Method(nameof(PrefixWriteLineOptions)));
        }

        private static MethodInfo Method(string name)
        {
            return typeof(KeenLogPatch).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        }

        [ThreadStatic]
        private static StringBuilder _tmpStringBuilder;

        private static StringBuilder PrepareLog(MyLog log)
        {
            _tmpStringBuilder ??= new StringBuilder();
            _tmpStringBuilder.Clear();

            int threadId = Environment.CurrentManagedThreadId;
            int indent = _getIndentByThread(log, threadId);

            _tmpStringBuilder.Append(' ', indent * 3);

            return _tmpStringBuilder;
        }

        private static bool PrefixWriteLine(MyLog __instance, string msg)
        {
            if (__instance.LogEnabled)
                _log.Debug(PrepareLog(__instance).Append(msg));

            return false;
        }

        private static bool PrefixWriteLineConsole(MyLog __instance, string msg)
        {
            if (__instance.LogEnabled)
                _log.Info(PrepareLog(__instance).Append(msg));

            return false;
        }

        private static bool PrefixAppendToClosedLog(MyLog __instance, string text)
        {
            if (__instance.LogEnabled)
                _log.Info(PrepareLog(__instance).Append(text));
            else
                _log.Info(text);

            return false;
        }

        private static bool PrefixWriteLineOptions(MyLog __instance, string message, LoggingOptions option)
        {
            var logFlagMethod = typeof(MyLog).GetMethod("LogFlag", BindingFlags.Instance | BindingFlags.NonPublic);

            if (logFlagMethod == null)
                throw new Exception("Failed to find LogFlag method");

            var logFlag = (bool)logFlagMethod.Invoke(__instance, new object[] { option })!;

            if (logFlag)
                _log.Info(PrepareLog(__instance).Append(message));

            return false;
        }

        private static bool PrefixAppendToClosedLogException(Exception e)
        {
            _log.Error(e);
            return false;
        }

        private static bool PrefixWriteLineException(Exception ex)
        {
            _log.Error(ex);
            return false;
        }

        private static bool PrefixLogFormatted(MyLog __instance, MyLogSeverity severity, string format, object[] args)
        {
            if (!__instance.LogEnabled)
                return false;

            // Sometimes this is called with a pre-formatted string and no args
            // and causes a crash when the format string contains braces
            var sb = PrepareLog(__instance);

            if (args != null && args.Length > 0)
                sb.AppendFormat(format, args);
            else
                sb.Append(format);

            _log.Log(LogLevelFor(severity), sb);

            return false;
        }

        private static bool PrefixLogStringBuilder(MyLog __instance, MyLogSeverity severity, StringBuilder builder)
        {
            if (__instance.LogEnabled)
                _log.Log(LogLevelFor(severity), PrepareLog(__instance).Append(builder));

            return false;
        }

        private static LogLevel LogLevelFor(MyLogSeverity severity)
        {
            return severity switch {
                MyLogSeverity.Debug => LogLevel.Debug,
                MyLogSeverity.Info => LogLevel.Info,
                MyLogSeverity.Warning => LogLevel.Warn,
                MyLogSeverity.Error => LogLevel.Error,
                MyLogSeverity.Critical => LogLevel.Fatal,
                _ => LogLevel.Info,
            };
        }
    }
}
