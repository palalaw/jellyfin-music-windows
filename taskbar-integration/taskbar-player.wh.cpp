// ==WindhawkMod==
// @id taskbar-jellyfin-player
// @name Jellyfin Music taskbar bridge (experimental)
// @description Experimental in-taskbar UI controlled by the local Jellyfin Music app.
// @version 0.3.1
// @author Local development
// @include explorer.exe
// @architecture x86-64
// @compilerOptions -lole32 -loleaut32 -lruntimeobject -luuid -luser32 -lwindowsapp -lcomctl32
// ==/WindhawkMod==
// ==WindhawkModSettings==
/*
- enabled: false
  $name: Show experimental taskbar UI
*/
// ==/WindhawkModSettings==
// Prototype only. Adapted helpers from Salyts Taskbar Fluent Media Player,
// copyright (c) 2026 Salyts, MIT. See THIRD-PARTY-NOTICES.txt.
#ifndef WH_MOD_ID
#define WH_MOD_ID L"taskbar-jellyfin-player"
#endif
#include <windows.h>
#undef GetCurrentTime
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Foundation.Collections.h>
#include <winrt/Windows.UI.Xaml.h>
#include <winrt/Windows.UI.Xaml.Controls.h>
#include <winrt/Windows.UI.Xaml.Media.h>
#include <winrt/Windows.UI.Xaml.Automation.h>
#include <winrt/Windows.UI.Xaml.Controls.Primitives.h>
#include <windhawk_utils.h>
#include <string>
#include <atomic>
#include <vector>
using namespace winrt::Windows::UI::Xaml;
using namespace winrt::Windows::UI::Xaml::Controls;
using namespace winrt::Windows::UI::Xaml::Media;
static HWND g_window = nullptr;
static std::atomic<bool> g_unloading{false};
static std::atomic<bool> g_uiReady{false};
static bool g_inTaskbarRoot=false;
[[clang::no_destroy]] static Grid g_parent{nullptr};
[[clang::no_destroy]] static StackPanel g_panel{nullptr};
[[clang::no_destroy]] static ColumnDefinition g_column{nullptr};
[[clang::no_destroy]] static ColumnDefinition g_baseColumn{nullptr};
[[clang::no_destroy]] static FrameworkElement g_background{nullptr};
static int g_backgroundSpan=1;
[[clang::no_destroy]] static TextBlock g_title{nullptr};
[[clang::no_destroy]] static Button g_play{nullptr};
// XAML can retain detached controls for animation/focus. Explicitly revoke
// every delegate while our DLL is still loaded, before removing the controls.
[[clang::no_destroy]] static std::vector<std::pair<Button,winrt::event_token>> g_clickHandlers;
static int g_track = 1;
static bool g_playing = false;

using CTaskBand_GetTaskbarHost_t  = void*(WINAPI*)(void*, void*);
using CSecondaryTaskBand_GetTaskbarHost_t = void*(WINAPI*)(void*, void*);
using TaskbarHost_FrameHeight_t   = int  (WINAPI*)(void*);
using Std_Ref_Decref_t            = void (WINAPI*)(void*);
static CTaskBand_GetTaskbarHost_t  CTaskBand_GetTaskbarHost_Original  = nullptr;
static CSecondaryTaskBand_GetTaskbarHost_t CSecondaryTaskBand_GetTaskbarHost_Original = nullptr;
static TaskbarHost_FrameHeight_t   TaskbarHost_FrameHeight_Original   = nullptr;
static Std_Ref_Decref_t            Std_Ref_Decref_Original            = nullptr;
static void* CTaskBand_ITaskListWndSite_vftable = nullptr;
static void* CSecondaryTaskBand_ITaskListWndSite_vftable = nullptr;
using WindowThreadProc = void(*)(void*);
static bool RunFromWindowThread(HWND hWnd, WindowThreadProc proc, void* param) {
    static const UINT kMsg = RegisterWindowMessage(L"Windhawk_RunFromWindowThread_" WH_MOD_ID);
    struct Payload { WindowThreadProc proc; void* param; };
    DWORD tid = GetWindowThreadProcessId(hWnd, nullptr);
    if (!tid) return false;
    if (tid == GetCurrentThreadId()) {
        proc(param);
        return true;
    }
    HHOOK hook = SetWindowsHookExW(WH_CALLWNDPROC,
        [](int code, WPARAM w, LPARAM l) -> LRESULT {
            if (code == HC_ACTION) {
                auto* cwp = reinterpret_cast<const CWPSTRUCT*>(l);
                static const UINT kM = RegisterWindowMessage(L"Windhawk_RunFromWindowThread_" WH_MOD_ID);
                if (cwp->message == kM) {
                    auto* p = reinterpret_cast<Payload*>(cwp->lParam);
                    p->proc(p->param);
                }
            }
            return CallNextHookEx(nullptr, code, w, l);
        }, nullptr, tid);
    if (!hook) return false;
    Payload pay{proc, param};
    SendMessageW(hWnd, kMsg, 0, reinterpret_cast<LPARAM>(&pay));
    UnhookWindowsHookEx(hook);
    return true;
}
static FrameworkElement FindChildByName(FrameworkElement const& root, std::wstring_view name, int depth = 32) {
    if (!root || depth == 0) return nullptr;
    int n = VisualTreeHelper::GetChildrenCount(root);
    for (int i = 0; i < n; ++i) {
        auto child = VisualTreeHelper::GetChild(root, i).try_as<FrameworkElement>();
        if (!child) continue;
        if (child.Name() == name) return child;
        if (auto found = FindChildByName(child, name, depth - 1)) return found;
    }
    return nullptr;
}
// Bounded structural diagnostics only: never log text content or song metadata.
static void LogStructure(DependencyObject const& node, int depth, int& budget) {
    if (!node || depth > 9 || budget-- <= 0) return;
    auto element = node.try_as<FrameworkElement>();
    auto name = element ? element.Name() : winrt::hstring{};
    auto type = winrt::get_class_name(node);
    Wh_Log(L"TREE depth=%d type=%s name=%s", depth, type.c_str(), name.c_str());
    int count = VisualTreeHelper::GetChildrenCount(node);
    for (int i=0; i<count && budget>0; ++i)
        LogStructure(VisualTreeHelper::GetChild(node,i),depth+1,budget);
}
static bool IsReadableMemoryRange(const void* address, size_t size) {
    if (!address || size == 0) return false;
    MEMORY_BASIC_INFORMATION memory{};
    if (!VirtualQuery(address, &memory, sizeof(memory)) ||
        memory.State != MEM_COMMIT ||
        (memory.Protect & (PAGE_GUARD | PAGE_NOACCESS))) {
        return false;
    }
    const auto start = reinterpret_cast<uintptr_t>(address);
    const auto regionStart = reinterpret_cast<uintptr_t>(memory.BaseAddress);
    const auto regionEnd = regionStart + memory.RegionSize;
    return start >= regionStart && start <= regionEnd &&
           size <= regionEnd - start;
}
static XamlRoot GetTaskbarXamlRoot(HWND hTaskbarWnd) {
    wchar_t clsBuf[64] = {};
    GetClassNameW(hTaskbarWnd, clsBuf, ARRAYSIZE(clsBuf));
    bool isSecondary = _wcsicmp(clsBuf, L"Shell_SecondaryTrayWnd") == 0;
    HWND hTaskSwWnd = isSecondary
        ? FindWindowExW(hTaskbarWnd, nullptr, L"WorkerW", nullptr)
        : (HWND)GetProp(hTaskbarWnd, L"TaskbandHWND");
    if (!hTaskSwWnd) {
        Wh_Log(L"GetTaskbarXamlRoot: could not find taskband host window, aborting");
        return nullptr;
    }
    wchar_t swCls[64] = {};
    GetClassNameW(hTaskSwWnd, swCls, ARRAYSIZE(swCls));
    void* taskBand = (void*)GetWindowLongPtrW(hTaskSwWnd, 0);
    if (!taskBand) {
        Wh_Log(L"GetTaskbarXamlRoot: taskBand pointer is null, aborting");
        return nullptr;
    }
    void* expectedVftable = isSecondary
        ? CSecondaryTaskBand_ITaskListWndSite_vftable
        : CTaskBand_ITaskListWndSite_vftable;
    auto getTaskbarHost = isSecondary
        ? CSecondaryTaskBand_GetTaskbarHost_Original
        : CTaskBand_GetTaskbarHost_Original;
    if (!expectedVftable || !getTaskbarHost) {
        Wh_Log(L"GetTaskbarXamlRoot: %s symbols not resolved, aborting",
            isSecondary ? L"CSecondaryTaskBand" : L"CTaskBand");
        return nullptr;
    }
    void* taskBandForTaskListWndSite = taskBand;
    int i = 0;
    constexpr int kMaxSlotsToScan = 20;
    for (;; i++) {
        if (!IsReadableMemoryRange(taskBandForTaskListWndSite, sizeof(void*))) {
            Wh_Log(L"GetTaskbarXamlRoot: hit unreadable memory at slot %d, aborting", i);
            return nullptr;
        }
        if (*(void**)taskBandForTaskListWndSite == expectedVftable) break;
        if (i == kMaxSlotsToScan) {
            Wh_Log(L"GetTaskbarXamlRoot: ITaskListWndSite vftable not found within %d slots, aborting",
                kMaxSlotsToScan);
            return nullptr;
        }
        taskBandForTaskListWndSite = (void**)taskBandForTaskListWndSite + 1;
    }
    void* taskbarHostSharedPtr[2]{};
    getTaskbarHost(taskBandForTaskListWndSite, taskbarHostSharedPtr);
    if (!taskbarHostSharedPtr[0]) {
        Wh_Log(L"GetTaskbarXamlRoot: TaskbarHost shared_ptr is empty, aborting");
        if (taskbarHostSharedPtr[1] && Std_Ref_Decref_Original)
            Std_Ref_Decref_Original(taskbarHostSharedPtr[1]);
        return nullptr;
    }
    size_t taskbarElementIUnknownOffset = 0;
    bool frameHeightPatternRecognized = false;
#if defined(_M_X64) || defined(__x86_64__)
    {
        const BYTE* b = (const BYTE*)TaskbarHost_FrameHeight_Original;
        if (IsReadableMemoryRange(b, 8) &&
            b[0] == 0x48 && b[1] == 0x83 && b[2] == 0xEC && b[4] == 0x48 &&
            b[5] == 0x83 && b[6] == 0xC1 && b[7] <= 0x7F) {
            taskbarElementIUnknownOffset = b[7];
            frameHeightPatternRecognized = true;
        } else {
            Wh_Log(L"Unsupported TaskbarHost::FrameHeight pattern (x64)");
        }
    }
#elif defined(_M_ARM64) || defined(__aarch64__)
    {
        const DWORD* p = (const DWORD*)TaskbarHost_FrameHeight_Original;
        if (IsReadableMemoryRange(p, sizeof(DWORD) * 4) &&
            p[0] == 0xD503237F && (p[1] & 0xFFC07FFF) == 0xA9807BFD &&
            p[2] == 0x910003FD && (p[3] & 0xFFF00FE0) == 0xF8400C00) {
            taskbarElementIUnknownOffset = (p[3] >> 12) & 0xFF;
            frameHeightPatternRecognized = true;
        } else {
            Wh_Log(L"Unsupported TaskbarHost::FrameHeight pattern (arm64)");
        }
    }
#else
    Wh_Log(L"GetTaskbarXamlRoot: Unknown architecture, using default offset 0x10");
    taskbarElementIUnknownOffset = 0x10;
    frameHeightPatternRecognized = true;
#endif
    if (!frameHeightPatternRecognized ||
        !IsReadableMemoryRange(
            static_cast<BYTE*>(taskbarHostSharedPtr[0]) +
                taskbarElementIUnknownOffset,
            sizeof(IUnknown*))) {
        Wh_Log(L"GetTaskbarXamlRoot: FrameHeight pattern not recognized or offset unreadable, aborting");
        if (taskbarHostSharedPtr[1] && Std_Ref_Decref_Original)
            Std_Ref_Decref_Original(taskbarHostSharedPtr[1]);
        return nullptr;
    }
    auto* taskbarElementIUnknown =
        *(IUnknown**)((BYTE*)taskbarHostSharedPtr[0] +
                    taskbarElementIUnknownOffset);
    if (!taskbarElementIUnknown) {
        Wh_Log(L"GetTaskbarXamlRoot: taskbarElementIUnknown is null, aborting");
        if (taskbarHostSharedPtr[1] && Std_Ref_Decref_Original)
            Std_Ref_Decref_Original(taskbarHostSharedPtr[1]);
        return nullptr;
    }
    FrameworkElement taskbarElement{nullptr};
    HRESULT hr = taskbarElementIUnknown->QueryInterface(
        winrt::guid_of<FrameworkElement>(), winrt::put_abi(taskbarElement));
    auto result = taskbarElement ? taskbarElement.XamlRoot() : nullptr;
    if (taskbarHostSharedPtr[1] && Std_Ref_Decref_Original)
        Std_Ref_Decref_Original(taskbarHostSharedPtr[1]);
    return SUCCEEDED(hr) ? result : nullptr;
}
static bool HookTaskbarDllSymbols() {
    static const wchar_t* const kCandidates[] = {
        L"taskbar.dll",
    };
    HMODULE h = nullptr;
    for (auto* name : kCandidates) {
        h = LoadLibraryExW(name, nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
        if (h) break;
    }
    if (!h) { return FALSE; }
    WindhawkUtils::SYMBOL_HOOK taskbarDllHooks[] = {
        {{LR"(const CTaskBand::`vftable'{for `ITaskListWndSite'})"},
        &CTaskBand_ITaskListWndSite_vftable},
        {{LR"(const CSecondaryTaskBand::`vftable'{for `ITaskListWndSite'})"},
        &CSecondaryTaskBand_ITaskListWndSite_vftable},
        {{LR"(public: virtual class std::shared_ptr<class TaskbarHost> __cdecl CTaskBand::GetTaskbarHost(void)const )"},
        &CTaskBand_GetTaskbarHost_Original},
        {{LR"(public: virtual class std::shared_ptr<class TaskbarHost> __cdecl CSecondaryTaskBand::GetTaskbarHost(void)const )"},
        &CSecondaryTaskBand_GetTaskbarHost_Original},
        {{LR"(public: int __cdecl TaskbarHost::FrameHeight(void)const )"},
        &TaskbarHost_FrameHeight_Original},
        {{LR"(public: void __cdecl std::_Ref_count_base::_Decref(void))"},
        &Std_Ref_Decref_Original},

    };
    if (!WindhawkUtils::HookSymbols(h, taskbarDllHooks, ARRAYSIZE(taskbarDllHooks))) {
        return FALSE;
    }
    return TRUE;
}
#include <thread>
#include <mutex>
#include <deque>
struct PlayerPacket {
    uint32_t magic=0, version=0, visible=0, playing=0, canControl=0, reserved=0;
    wchar_t title[256]{}, artist[256]{};
};
static_assert(sizeof(PlayerPacket)==1048);
static std::mutex g_ipcMutex;
static PlayerPacket g_snapshot;
static std::deque<uint32_t> g_commands;
static std::thread g_worker;
static HANDLE g_stop=nullptr;
static UINT_PTR g_timer=0;
static ULONGLONG g_retryAfter=0;
static Button g_previous{nullptr}, g_next{nullptr};
static bool Transfer(HANDLE pipe,bool write,void* buffer,DWORD size) {
    OVERLAPPED ov{}; ov.hEvent=CreateEventW(nullptr,TRUE,FALSE,nullptr);
    if(!ov.hEvent) return false;
    DWORD transferred=0;
    BOOL done=write?WriteFile(pipe,buffer,size,&transferred,&ov):ReadFile(pipe,buffer,size,&transferred,&ov);
    if(!done && GetLastError()==ERROR_IO_PENDING) {
        HANDLE events[]={g_stop,ov.hEvent};
        if(WaitForMultipleObjects(2,events,FALSE,700)!=WAIT_OBJECT_0+1) {
            CancelIoEx(pipe,&ov);
            GetOverlappedResult(pipe,&ov,&transferred,TRUE);
            CloseHandle(ov.hEvent); return false;
        }
        done=GetOverlappedResult(pipe,&ov,&transferred,FALSE);
    }
    CloseHandle(ov.hEvent); return done && transferred==size;
}
static void QueueCommand(uint32_t command) {
    std::lock_guard lock(g_ipcMutex);
    if(g_snapshot.visible && g_commands.size()<16) g_commands.push_back(command);
}
static void PollPlayer() {
    DWORD session=0; ProcessIdToSessionId(GetCurrentProcessId(),&session);
    auto name=L"\\\\.\\pipe\\JellyfinMusic.Taskbar.v1."+std::to_wstring(session);
    while(WaitForSingleObject(g_stop,0)==WAIT_TIMEOUT) {
        uint32_t command=0;
        { std::lock_guard lock(g_ipcMutex); if(!g_commands.empty()) { command=g_commands.front(); g_commands.pop_front(); } }
        HANDLE pipe=CreateFileW(name.c_str(),GENERIC_READ|GENERIC_WRITE,0,nullptr,OPEN_EXISTING,FILE_FLAG_OVERLAPPED|SECURITY_SQOS_PRESENT|SECURITY_IDENTIFICATION,nullptr);
        PlayerPacket next{};
        if(pipe!=INVALID_HANDLE_VALUE) {
            uint32_t request[]={0x314D464A,command|(g_uiReady?0x100u:0u)};
            if(!Transfer(pipe,true,request,sizeof(request)) || !Transfer(pipe,false,&next,sizeof(next)) || next.magic!=0x314D464A || next.version!=1) next={};
            CloseHandle(pipe);
        }
        next.title[255]=0; next.artist[255]=0;
        { std::lock_guard lock(g_ipcMutex); g_snapshot=next; if(!next.visible) g_commands.clear(); }
        if(WaitForSingleObject(g_stop,250)!=WAIT_TIMEOUT) break;
    }
}
static void UpdateMock() {
    if(!g_title||!g_play) return;
    PlayerPacket state;
    { std::lock_guard lock(g_ipcMutex); state=g_snapshot; }
    g_title.Text(std::wstring(state.title)+L"\n"+state.artist);
    ToolTipService::SetToolTip(g_panel,winrt::box_value(std::wstring(state.title)+L"\n"+state.artist));
    g_play.Content(winrt::box_value(state.playing?L"Ⅱ":L"▶"));
    g_previous.IsEnabled(state.canControl!=0); g_play.IsEnabled(state.canControl!=0); g_next.IsEnabled(state.canControl!=0);
    Automation::AutomationProperties::SetName(g_play,state.playing?L"一時停止":L"再生");
}

static void RemoveProbe() {
    g_uiReady=false;
    for(auto const& handler:g_clickHandlers) handler.first.Click(handler.second);
    g_clickHandlers.clear();
    if(g_panel) ToolTipService::SetToolTip(g_panel,nullptr);
    // Only remove objects owned by this probe. Do not clear the system grid.
    if (g_parent && g_panel) {
        uint32_t index;
        if (g_parent.Children().IndexOf(g_panel, index)) g_parent.Children().RemoveAt(index);
    }
    if (g_parent && g_column) {
        uint32_t index;
        if (g_parent.ColumnDefinitions().IndexOf(g_column, index)) {
            g_parent.ColumnDefinitions().RemoveAt(index);
            for (auto child : g_parent.Children()) {
                auto element = child.try_as<FrameworkElement>(); if (!element) continue;
                int col = Grid::GetColumn(element);
                if (col > static_cast<int>(index)) Grid::SetColumn(element, col - 1);
            }
        }
    }
    if(g_background) Grid::SetColumnSpan(g_background,g_backgroundSpan);
    g_background=nullptr;
    if(g_parent && g_baseColumn) {
        uint32_t index;
        if(g_parent.ColumnDefinitions().IndexOf(g_baseColumn,index)) g_parent.ColumnDefinitions().RemoveAt(index);
    }
    g_baseColumn=nullptr;
    if (g_panel) g_panel.Children().Clear();
    g_previous=nullptr; g_next=nullptr; g_play = nullptr; g_title = nullptr; g_panel = nullptr;
    g_column = nullptr; g_parent = nullptr;
    g_inTaskbarRoot=false;
    Wh_Log(L"UI removed; explorer pid=%lu", GetCurrentProcessId());
}
static void ApplyProbe() {
    try {
        if (g_unloading || !Wh_GetIntSetting(L"enabled")) return;
        auto xaml = GetTaskbarXamlRoot(g_window);
        if (!xaml) { Wh_Log(L"FAIL: XAML root unavailable"); return; }
        auto root = xaml.Content().try_as<FrameworkElement>();
        auto target = FindChildByName(root, L"SystemTrayFrameGrid").try_as<Grid>();
        bool taskbarRoot=false;
        if(!target) {
            auto frame=FindChildByName(root,L"TaskbarFrame");
            target=FindChildByName(frame,L"RootGrid").try_as<Grid>();
            taskbarRoot=target!=nullptr;
            // Accept only the observed layout: implicit single column and
            // a direct taskbar repeater. Unknown structures remain unsupported.
            if(target && (target.ColumnDefinitions().Size()!=0 ||
                !FindChildByName(target,L"TaskbarFrameRepeater"))) target=nullptr;
        }
        if (!target) {
            static bool logged=false;
            if(!logged) { logged=true; int budget=120; LogStructure(root,0,budget); }
            Wh_Log(L"FAIL: tray grid unavailable"); return;
        }
        // The test appends an actual Auto column, never an overlay window.
        g_parent = target;
        g_inTaskbarRoot=taskbarRoot;
        if(taskbarRoot) {
            g_baseColumn=ColumnDefinition();
            g_baseColumn.Width({1,GridUnitType::Star});
            target.ColumnDefinitions().Append(g_baseColumn);
            g_background=FindChildByName(target,L"BackgroundControl");
            if(g_background) {g_backgroundSpan=Grid::GetColumnSpan(g_background);Grid::SetColumnSpan(g_background,2);}
        }
        g_column = ColumnDefinition();
        g_column.Width({1, GridUnitType::Auto});
        int col = static_cast<int>(target.ColumnDefinitions().Size());
        target.ColumnDefinitions().Append(g_column);
        g_panel = StackPanel();
        g_panel.Name(L"JellyfinFeasibilityProbe");
        g_panel.Orientation(Orientation::Horizontal);
        g_panel.VerticalAlignment(VerticalAlignment::Center);
        g_title = TextBlock();
        g_title.Width(165); g_title.FontSize(11); g_title.MaxLines(2);
        g_title.VerticalAlignment(VerticalAlignment::Center);
        g_title.TextTrimming(TextTrimming::CharacterEllipsis);
        Button titleButton; titleButton.Padding({4,0,4,0}); titleButton.Content(g_title);
        g_clickHandlers.emplace_back(titleButton,titleButton.Click([](auto const&,auto const&){ if(!g_unloading) QueueCommand(4); }));
        g_panel.Children().Append(titleButton);
        for (int command = 0; command < 3; ++command) {
            Button button;
            button.Width(34); button.Height(32); button.Padding({0,0,0,0});
            button.Content(winrt::box_value(command == 0 ? L"|◀" : command == 1 ? L"▶" : L"▶|"));
            Automation::AutomationProperties::SetName(button, command == 0 ? L"前の曲" : command == 1 ? L"再生" : L"次の曲");
            auto clickToken=button.Click([command](auto const&, auto const&) {
                if (g_unloading) return;
                try {
                    QueueCommand(command+1);
                } catch (...) { Wh_Log(L"FAIL: mock click"); }
            });
            g_clickHandlers.emplace_back(button,clickToken);
            if(command==0) g_previous=button; if(command==1) g_play=button; if(command==2) g_next=button;
            g_panel.Children().Append(button);
        }
        UpdateMock();
        Grid::SetColumn(g_panel, col);
        target.Children().Append(g_panel);
        Wh_Log(L"UI inserted; explorer pid=%lu column=%d parent=%s width=%.1f", GetCurrentProcessId(), col,target.Name().c_str(),target.ActualWidth());
    } catch (...) {
        Wh_Log(L"FAIL: apply; attempting cleanup");
        try { RemoveProbe(); } catch (...) { Wh_Log(L"FAIL: cleanup"); }
    }
}
static HWND FindPrimaryTaskbar() {
    HWND result = nullptr;
    EnumWindows([](HWND window, LPARAM param) -> BOOL {
        DWORD pid = 0; wchar_t cls[64]{};
        GetWindowThreadProcessId(window, &pid);
        if (pid == GetCurrentProcessId() && GetClassNameW(window, cls, 64) &&
            wcscmp(cls, L"Shell_TrayWnd") == 0) {
            *reinterpret_cast<HWND*>(param) = window;
            return FALSE;
        }
        return TRUE;
    }, reinterpret_cast<LPARAM>(&result));
    return result;
}
static void UiTick() {
    try {
        bool show;
        { std::lock_guard lock(g_ipcMutex); show=g_snapshot.visible!=0; }
        show=show&&!g_unloading&&Wh_GetIntSetting(L"enabled");
        if(!show) { if(g_panel) RemoveProbe(); g_retryAfter=0; return; }
        if(!g_panel) {
            if(GetTickCount64()>=g_retryAfter) { g_retryAfter=GetTickCount64()+5000; ApplyProbe(); }
        } else {
            if(g_inTaskbarRoot) {
                // The notification area can occupy a separate native child
                // window. Reserve its width inside our Auto column as well.
                HWND tray=FindWindowExW(g_window,nullptr,L"TrayNotifyWnd",nullptr);
                RECT trayRect{},barRect{};
                double reserve=0;
                if(tray && IsWindowVisible(tray) && GetWindowRect(tray,&trayRect) && GetWindowRect(g_window,&barRect))
                    reserve=(barRect.right-trayRect.left)/g_panel.XamlRoot().RasterizationScale();
                auto margin=g_panel.Margin();
                if(margin.Right!=reserve) g_panel.Margin({0,0,reserve,0});
            }
            UpdateMock();
            g_uiReady=g_panel.IsLoaded()&&g_panel.ActualWidth()>0&&g_panel.ActualHeight()>0;
        }
    } catch(...) { Wh_Log(L"Taskbar update failed"); }
}
// A queued WM_TIMER must never hold a callback address in an unloaded DLL.
// Remove the subclass before unloading; remaining messages go to Explorer.
static LRESULT CALLBACK TaskbarSubclass(HWND hwnd,UINT msg,WPARAM wp,LPARAM lp,DWORD_PTR) {
    if(msg==WM_TIMER && g_timer && wp==g_timer) { if(!g_unloading) UiTick(); return 0; }
    return DefSubclassProc(hwnd,msg,wp,lp);
}
BOOL Wh_ModInit() {
    g_unloading=false;
    if(!HookTaskbarDllSymbols()) return FALSE;
    g_stop=CreateEventW(nullptr,TRUE,FALSE,nullptr);
    return g_stop!=nullptr;
}
void Wh_ModAfterInit() {
    g_window=FindPrimaryTaskbar();
    if(!g_window) { Wh_Log(L"Taskbar not ready; disable/re-enable on this build"); return; }
    if(!WindhawkUtils::SetWindowSubclassFromAnyThread(g_window,TaskbarSubclass,0)) {
        Wh_Log(L"FAIL: taskbar subclass unavailable"); return;
    }
    g_worker=std::thread(PollPlayer);
    RunFromWindowThread(g_window,[](void*){
        // Unique id for this module, no TIMERPROC in the message queue.
        g_timer=SetTimer(g_window,reinterpret_cast<UINT_PTR>(&g_timer),300,nullptr);
        Wh_Log(L"Bridge ready; explorer pid=%lu timer=%llu",GetCurrentProcessId(),static_cast<unsigned long long>(g_timer));
    },nullptr);
}
void Wh_ModSettingsChanged() { /* UI timer reads the enabled setting. */ }
void Wh_ModUninit() {
    g_unloading=true;
    if(g_stop) SetEvent(g_stop);
    if(g_worker.joinable()) g_worker.join();
    if(g_window&&IsWindow(g_window)) RunFromWindowThread(g_window,[](void*){
        if(g_timer) KillTimer(g_window,g_timer); g_timer=0;
        try {RemoveProbe();} catch(...) {Wh_Log(L"UI cleanup failed");}
    },nullptr);
    if(g_window&&IsWindow(g_window)) WindhawkUtils::RemoveWindowSubclassFromAnyThread(g_window,TaskbarSubclass);
    if(g_stop) {CloseHandle(g_stop);g_stop=nullptr;}
}
