using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using DotNetPlugin.NativeBindings.Win32;

namespace DotNetPlugin.NativeBindings.SDK
{
    // https://github.com/x64dbg/x64dbg/blob/development/src/dbg/_plugins.h
    public static class Plugins
    {
        public const int PLUG_SDKVERSION = 1;

        public delegate void CBPLUGIN(CBTYPE cbType, IntPtr callbackInfo);

        public delegate bool CBPLUGINCOMMAND(string[] args);

        public delegate nuint CBPLUGINEXPRFUNCTION(nuint[] args, object userdata);
        public delegate nuint CBPLUGINEXPRFUNCTION_RAWARGS(int argc, IntPtr argv, object userdata);

#if AMD64
        private const string dll = "x64dbg.dll";
#else
        private const string dll = "x32dbg.dll";
#endif
        private const CallingConvention cdecl = CallingConvention.Cdecl;

        #region Logging

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void _plugin_logprint([MarshalAs(UnmanagedType.LPUTF8Str)] string text);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void _plugin_logputs([MarshalAs(UnmanagedType.LPUTF8Str)] string text);

        #endregion

        #region Callbacks

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void CBPLUGIN_NATIVE(CBTYPE cbType, IntPtr callbackInfo);

        private static CBPLUGIN_NATIVE[] _pluginCallbacks = new CBPLUGIN_NATIVE[(int)CBTYPE.CB_LAST];

        [DllImport(dll, CallingConvention = cdecl, EntryPoint = nameof(_plugin_registercallback), ExactSpelling = true)]
        private static extern void _plugin_registercallback_native(int pluginHandle, CBTYPE cbType, CBPLUGIN_NATIVE cbPlugin);

        public static void _plugin_registercallback(int pluginHandle, CBTYPE cbType, CBPLUGIN cbPlugin)
        {
            if (cbPlugin == null)
                throw new ArgumentNullException(nameof(cbPlugin));

            if (cbType < 0 || _pluginCallbacks.Length <= (int)cbType)
                throw new ArgumentOutOfRangeException(nameof(cbType));

            CBPLUGIN_NATIVE callback = (cbType, callbackInfo) =>
            {
                try
                {
                    cbPlugin(cbType, callbackInfo);
                }
                catch (Exception ex)
                {
                    PluginMain.LogUnhandledException(ex);
                }
            };

            lock (_pluginCallbacks)
            {
                // The CLR protects the delegate from being GC'd only for the duration of the call, so we need to keep a reference to it until unregistration.
                _pluginCallbacks[(int)cbType] = callback;

                _plugin_registercallback_native(pluginHandle, cbType, callback);
            }
        }

        [DllImport(dll, CallingConvention = cdecl, EntryPoint = nameof(_plugin_unregistercallback), ExactSpelling = true)]
        private static extern bool _plugin_unregistercallback_native(int pluginHandle, CBTYPE cbType);

        public static bool _plugin_unregistercallback(int pluginHandle, CBTYPE cbType)
        {
            if (cbType < 0 || _pluginCallbacks.Length <= (int)cbType)
                throw new ArgumentOutOfRangeException(nameof(cbType));

            lock (_pluginCallbacks)
            {
                var success = _plugin_unregistercallback_native(pluginHandle, cbType);

                if (success)
                    _pluginCallbacks[(int)cbType] = null;

                return success;
            }
        }

        #endregion

        #region Commands

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate bool CBPLUGINCOMMAND_NATIVE(int argc, IntPtr argv);

        private static Dictionary<string, CBPLUGINCOMMAND_NATIVE> _commandCallbacks = new Dictionary<string, CBPLUGINCOMMAND_NATIVE>();

        [DllImport(dll, CallingConvention = cdecl, EntryPoint = nameof(_plugin_registercommand), ExactSpelling = true)]
        private static extern bool _plugin_registercommand_native(int pluginHandle, [MarshalAs(UnmanagedType.LPUTF8Str)] string command, CBPLUGINCOMMAND_NATIVE cbCommand, bool debugonly);

        public static bool _plugin_registercommand(int pluginHandle, string command, CBPLUGINCOMMAND cbCommand, bool debugonly)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));

            if (cbCommand == null)
                throw new ArgumentNullException(nameof(cbCommand));

            CBPLUGINCOMMAND_NATIVE callback = (argc, argv) =>
            {
                try
                {
                    string[] argvArray;
                    if (argc > 0)
                    {
                        argvArray = new string[argc];
                        for (int i = 0, ofs = 0; i < argvArray.Length; i++, ofs += IntPtr.Size)
                            argvArray[i] = Marshal.ReadIntPtr(argv, ofs).MarshalToStringUTF8();
                    }
                    else
                        argvArray = Array.Empty<string>();

                    return cbCommand(argvArray);
                }
                catch (Exception ex)
                {
                    PluginMain.LogUnhandledException(ex);
                    return false;
                }
            };

            lock (_commandCallbacks)
            {
                // The CLR protects the delegate from being GC'd only for the duration of the call, so we need to keep a reference to it until unregistration.
                var success = _plugin_registercommand_native(pluginHandle, command, callback, debugonly);

                if (success)
                    _commandCallbacks[command] = callback;

                return success;
            }
        }

        [DllImport(dll, CallingConvention = cdecl, EntryPoint = nameof(_plugin_unregistercommand), ExactSpelling = true)]
        private static extern bool _plugin_unregistercommand_native(int pluginHandle, [MarshalAs(UnmanagedType.LPUTF8Str)] string command);

        public static bool _plugin_unregistercommand(int pluginHandle, string command)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));

            lock (_commandCallbacks)
            {
                var success = _plugin_unregistercommand_native(pluginHandle, command);

                if (success)
                    _commandCallbacks.Remove(command);

                return success;
            }
        }

        #endregion

        #region Expression Functions

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate nuint CBPLUGINEXPRFUNCTION_NATIVE(int argc, IntPtr argv, IntPtr userdata);

        private static Dictionary<string, (CBPLUGINEXPRFUNCTION_NATIVE, GCHandle)> _expressionFunctionCallbacks = new Dictionary<string, (CBPLUGINEXPRFUNCTION_NATIVE, GCHandle)>();

        [DllImport(dll, CallingConvention = cdecl, EntryPoint = nameof(_plugin_registerexprfunction), ExactSpelling = true)]
        private static extern bool _plugin_registerexprfunction_native(int pluginHandle, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int argc,
            CBPLUGINEXPRFUNCTION_NATIVE cbCommand, IntPtr userdata);

        private static bool _plugin_registerexprfunction_core(int pluginHandle, string name, int argc, CBPLUGINEXPRFUNCTION_NATIVE callback, object userdata)
        {
            var userdataHandle = userdata != null ? GCHandle.Alloc(userdata) : default;
            try
            {
                lock (_expressionFunctionCallbacks)
                {
                    // The CLR protects the delegate from being GC'd only for the duration of the call, so we need to keep a reference to it until unregistration.
                    var success = _plugin_registerexprfunction_native(pluginHandle, name, argc, callback, GCHandle.ToIntPtr(userdataHandle));

                    if (success)
                        _expressionFunctionCallbacks[name] = (callback, userdataHandle);

                    return success;
                }
            }
            catch
            {
                if (userdataHandle.IsAllocated)
                    userdataHandle.Free();
                throw;
            }
        }

        public static bool _plugin_registerexprfunction(int pluginHandle, string name, int argc, CBPLUGINEXPRFUNCTION cbFunction, object userdata)
        {
            if (name == null)
                throw new ArgumentNullException(nameof(name));

            if (cbFunction == null)
                throw new ArgumentNullException(nameof(cbFunction));

            CBPLUGINEXPRFUNCTION_NATIVE callback = (argc, argv, userdata) =>
            {
                try
                {
                    nuint[] argvArray;
                    if (argc > 0)
                    {
                        argvArray = new nuint[argc];
                        for (int i = 0, ofs = 0; i < argvArray.Length; i++, ofs += IntPtr.Size)
                            argvArray[i] = (nuint)(nint)Marshal.ReadIntPtr(argv, ofs);
                    }
                    else
                        argvArray = Array.Empty<nuint>();

                    object userdataObj = userdata != IntPtr.Zero ? GCHandle.FromIntPtr(userdata) : null;
                    return cbFunction(argvArray, userdataObj);
                }
                catch (Exception ex)
                {
                    PluginMain.LogUnhandledException(ex);
                    return default;
                }
            };

            return _plugin_registerexprfunction_core(pluginHandle, name, argc, callback, userdata);
        }

        public static bool _plugin_registerexprfunction(int pluginHandle, string name, int argc, CBPLUGINEXPRFUNCTION_RAWARGS cbFunction, object userdata)
        {
            if (name == null)
                throw new ArgumentNullException(nameof(name));

            if (cbFunction == null)
                throw new ArgumentNullException(nameof(cbFunction));

            CBPLUGINEXPRFUNCTION_NATIVE callback = (argc, argv, userdata) =>
            {
                try
                {
                    object userdataObj = userdata != IntPtr.Zero ? GCHandle.FromIntPtr(userdata) : null;
                    return cbFunction(argc, argv, userdataObj);
                }
                catch (Exception ex)
                {
                    PluginMain.LogUnhandledException(ex);
                    return default;
                }
            };

            return _plugin_registerexprfunction_core(pluginHandle, name, argc, callback, userdata);
        }

        [DllImport(dll, CallingConvention = cdecl, EntryPoint = nameof(_plugin_unregisterexprfunction), ExactSpelling = true)]
        private static extern bool _plugin_unregisterexprfunction_native(int pluginHandle, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

        public static bool _plugin_unregisterexprfunction(int pluginHandle, string name)
        {
            if (name == null)
                throw new ArgumentNullException(nameof(name));

            lock (_expressionFunctionCallbacks)
            {
                var success = _plugin_unregisterexprfunction_native(pluginHandle, name);

                if (success)
                {
                    if (_expressionFunctionCallbacks.TryGetValue(name, out var callbackInfo))
                    {
                        if (callbackInfo.Item2.IsAllocated)
                            callbackInfo.Item2.Free();

                        _expressionFunctionCallbacks.Remove(name);
                    }
                }

                return success;
            }
        }

        #endregion

        #region Menu

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern bool _plugin_menuaddentry(int hMenu, int hEntry, [MarshalAs(UnmanagedType.LPUTF8Str)] string title);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern int _plugin_menuadd(int hMenu, [MarshalAs(UnmanagedType.LPUTF8Str)] string title);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern bool _plugin_menuremove(int hMenu);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern bool _plugin_menuentryremove(int pluginHandle, int hEntry);

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern bool _plugin_menuclear(int hMenu);

        #endregion

        [DllImport(dll, CallingConvention = cdecl, ExactSpelling = true)]
        public static extern void _plugin_debugskipexceptions(bool skip);

#pragma warning disable 0649

        [Serializable]
        public unsafe struct PLUG_INITSTRUCT
        {
            public int pluginHandle;
            public int sdkVersion;
            public int pluginVersion;

            private const int pluginNameSize = 256;
            public fixed byte pluginNameBytes[pluginNameSize];
            public string pluginName
            {
                get
                {
                    fixed (byte* ptr = pluginNameBytes)
                        return new IntPtr(ptr).MarshalToStringUTF8(pluginNameSize);
                }
                set
                {
                    fixed (byte* ptr = pluginNameBytes)
                        value.MarshalToPtrUTF8(new IntPtr(ptr), pluginNameSize);
                }
            }
        }

        [Serializable]
        public struct PLUG_SETUPSTRUCT
        {
            public IntPtr hwndDlg;
            public int hMenu;
            public int hMenuDisasm;
            public int hMenuDump;
            public int hMenuStack;
        }

        public enum CBTYPE
        {
            CB_INITDEBUG, //PLUG_CB_INITDEBUG
            CB_STOPDEBUG, //PLUG_CB_STOPDEBUG
            CB_CREATEPROCESS, //PLUG_CB_CREATEPROCESS
            CB_EXITPROCESS, //PLUG_CB_EXITPROCESS
            CB_CREATETHREAD, //PLUG_CB_CREATETHREAD
            CB_EXITTHREAD, //PLUG_CB_EXITTHREAD
            CB_SYSTEMBREAKPOINT, //PLUG_CB_SYSTEMBREAKPOINT
            CB_LOADDLL, //PLUG_CB_LOADDLL
            CB_UNLOADDLL, //PLUG_CB_UNLOADDLL
            CB_OUTPUTDEBUGSTRING, //PLUG_CB_OUTPUTDEBUGSTRING
            CB_EXCEPTION, //PLUG_CB_EXCEPTION
            CB_BREAKPOINT, //PLUG_CB_BREAKPOINT
            CB_PAUSEDEBUG, //PLUG_CB_PAUSEDEBUG
            CB_RESUMEDEBUG, //PLUG_CB_RESUMEDEBUG
            CB_STEPPED, //PLUG_CB_STEPPED
            CB_ATTACH, //PLUG_CB_ATTACHED (before attaching, after CB_INITDEBUG)
            CB_DETACH, //PLUG_CB_DETACH (before detaching, before CB_STOPDEBUG)
            CB_DEBUGEVENT, //PLUG_CB_DEBUGEVENT (called on any debug event)
            CB_MENUENTRY, //PLUG_CB_MENUENTRY
            CB_WINEVENT, //PLUG_CB_WINEVENT
            CB_WINEVENTGLOBAL, //PLUG_CB_WINEVENTGLOBAL
            CB_LOADDB, //PLUG_CB_LOADSAVEDB
            CB_SAVEDB, //PLUG_CB_LOADSAVEDB
            CB_FILTERSYMBOL, //PLUG_CB_FILTERSYMBOL
            CB_TRACEEXECUTE, //PLUG_CB_TRACEEXECUTE
            CB_SELCHANGED, //PLUG_CB_SELCHANGED
            CB_ANALYZE, //PLUG_CB_ANALYZE
            CB_ADDRINFO, //PLUG_CB_ADDRINFO
            CB_VALFROMSTRING, //PLUG_CB_VALFROMSTRING
            CB_VALTOSTRING, //PLUG_CB_VALTOSTRING
            CB_MENUPREPARE, //PLUG_CB_MENUPREPARE
            CB_STOPPINGDEBUG, //PLUG_CB_STOPDEBUG
            CB_LAST
        }

        [Serializable]
        public struct PLUG_CB_INITDEBUG
        {
            private IntPtr szFileNamePtr; // string
            public string szFileName => szFileNamePtr.MarshalToStringUTF8();
        }

        [Serializable]
        public struct PLUG_CB_STOPDEBUG
        {
            public IntPtr reserved;
        }

        [Serializable]
        public struct PLUG_CB_CREATEPROCESS
        {
            private IntPtr CreateProcessInfoPtr; //WAPI.CREATE_PROCESS_DEBUG_INFO
            public CREATE_PROCESS_DEBUG_INFO? CreateProcessInfo => CreateProcessInfoPtr.ToStruct<CREATE_PROCESS_DEBUG_INFO>();

            private IntPtr modInfoPtr; //WAPI.IMAGEHLP_MODULE64
            public IMAGEHLP_MODULE64? modInfo => modInfoPtr.ToStruct<IMAGEHLP_MODULE64>();

            private IntPtr DebugFileNamePtr; //string
            public string DebugFileName => DebugFileNamePtr.MarshalToStringUTF8();

            private IntPtr fdProcessInfoPtr; //WAPI.PROCESS_INFORMATION
            public PROCESS_INFORMATION? fdProcessInfo => fdProcessInfoPtr.ToStruct<PROCESS_INFORMATION>();
        }

        [Serializable]
        public struct PLUG_CB_EXITPROCESS
        {
            private IntPtr ExitProcessPtr;
            public EXIT_PROCESS_DEBUG_INFO? fdProcessInfo => ExitProcessPtr.ToStruct<EXIT_PROCESS_DEBUG_INFO>();
        }

        [Serializable]
        public struct PLUG_CB_LOADDLL
        {
            private IntPtr LoadDllPtr; //WAPI.LOAD_DLL_DEBUG_INFO
            public LOAD_DLL_DEBUG_INFO? LoadDll => LoadDllPtr.ToStruct<LOAD_DLL_DEBUG_INFO>();

            private IntPtr modInfoPtr; //WAPI.IMAGEHLP_MODULE64
            public IMAGEHLP_MODULE64? modInfo => modInfoPtr.ToStruct<IMAGEHLP_MODULE64>();

            private IntPtr modnamePtr; //string
            public string modname => modnamePtr.MarshalToStringUTF8();
        }

        [Serializable]
        public struct PLUG_CB_EXCEPTION
        {
            private IntPtr ExceptionPtr;
            public EXCEPTION_DEBUG_INFO? Exception => ExceptionPtr.ToStruct<EXCEPTION_DEBUG_INFO>();
        }

        [Serializable]
        public struct PLUG_CB_DEBUGEVENT
        {
            private IntPtr DebugEventPtr;
            public DEBUG_EVENT? DebugEvent => DebugEventPtr.ToStruct<DEBUG_EVENT>();
        }

        [Serializable]
        public struct PLUG_CB_MENUENTRY
        {
            public int hEntry;
        }

        [Serializable]
        public struct PLUG_CB_TRACEEXECUTE
        {
            public nuint cip;
            public bool stop;
        }
    }
}
