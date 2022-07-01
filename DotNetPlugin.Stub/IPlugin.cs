﻿using DotNetPlugin.NativeBindings.SDK;

namespace DotNetPlugin
{
    /// <summary>
    /// Defines an API to interact with x64dbg.
    /// </summary>
    internal interface IPlugin
    {
        int PluginVersion { get; }
        string PluginName { get; }
        int PluginHandle { get; }

        bool Init();
        void Setup(in Plugins.PLUG_SETUPSTRUCT setupStruct);
        bool Stop();

        void OnInitDebug(in Plugins.PLUG_CB_INITDEBUG info);
        void OnStopDebug(in Plugins.PLUG_CB_STOPDEBUG info);
        void OnCreateProcess(in Plugins.PLUG_CB_CREATEPROCESS info);
        void OnLoadDll(in Plugins.PLUG_CB_LOADDLL info);
        void OnMenuEntry(in Plugins.PLUG_CB_MENUENTRY info);
    }
}
