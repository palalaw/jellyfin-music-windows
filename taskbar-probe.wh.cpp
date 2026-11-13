// ==WindhawkMod==
// @id taskbar-jellyfin-feasibility-probe
// @name Taskbar feasibility probe (mock only)
// @description Experimental in-taskbar title and three buttons. No audio/network.
// @version 0.1.0
// @author Local development
// @include explorer.exe
// @architecture x86-64
// @compilerOptions -lole32 -loleaut32 -lruntimeobject -luuid -luser32 -lwindowsapp
// ==/WindhawkMod==
// ==WindhawkModSettings==
/*
- enabled: false
  $name: Show experimental taskbar UI
*/
// ==/WindhawkModSettings==
// Prototype only. Adapted helpers from Salyts Taskbar Fluent Media Player,
// copyright (c) 2026 Salyts, MIT. See THIRD-PARTY-NOTICES.txt.
#include <windows.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Foundation.Collections.h>
#include <winrt/Windows.UI.Xaml.h>
#include <winrt/Windows.UI.Xaml.Controls.h>
#include <winrt/Windows.UI.Xaml.Media.h>
#include <winrt/Windows.UI.Xaml.Automation.h>
#include <windhawk_utils.h>
#include <string>
#include <atomic>
using namespace winrt::Windows::UI::Xaml;
using namespace winrt::Windows::UI::Xaml::Controls;
using namespace winrt::Windows::UI::Xaml::Media;
static HWND g_window = nullptr;
static std::atomic<bool> g_unloading{false};
[[clang::no_destroy]] static Grid g_parent{nullptr};
[[clang::no_destroy]] static StackPanel g_panel{nullptr};
[[clang::no_destroy]] static ColumnDefinition g_column{nullptr};
[[clang::no_destroy]] static TextBlock g_title{nullptr};
[[clang::no_destroy]] static Button g_play{nullptr};
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
        [](int code, WPARAM w, LPARAM l) CALLBACK -> LRESULT {
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
static void UpdateMock() {
    if (!g_title || !g_play) return;
    g_title.Text(L"検証用の曲 " + std::to_wstring(g_track));
    g_play.Content(winrt::box_value(g_playing ? L"Ⅱ" : L"▶"));
    Automation::AutomationProperties::SetName(g_play, g_playing ? L"一時停止" : L"再生");
    Wh_Log(L"MOCK track=%d playing=%d", g_track, g_playing);
}
static void RemoveProbe() {
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
                int col = Grid::GetColumn(child);
                if (col > static_cast<int>(index)) Grid::SetColumn(child, col - 1);
            }
        }
    }
    if (g_panel) g_panel.Children().Clear();
    g_play = nullptr; g_title = nullptr; g_panel = nullptr;
    g_column = nullptr; g_parent = nullptr;
    Wh_Log(L"UI removed; explorer pid=%lu", GetCurrentProcessId());
}
static void ApplyProbe() {
    try {
        RemoveProbe();
        if (g_unloading || !Wh_GetIntSetting(L"enabled")) return;
        auto xaml = GetTaskbarXamlRoot(g_window);
        if (!xaml) { Wh_Log(L"FAIL: XAML root unavailable"); return; }
        auto root = xaml.Content().try_as<FrameworkElement>();
        auto target = FindChildByName(root, L"SystemTrayFrameGrid").try_as<Grid>();
        if (!target) { Wh_Log(L"FAIL: tray grid unavailable"); return; }
        // The test appends an actual Auto column, never an overlay window.
        g_parent = target;
        g_column = ColumnDefinition();
        g_column.Width({1, GridUnitType::Auto});
        int col = static_cast<int>(target.ColumnDefinitions().Size());
        target.ColumnDefinitions().Append(g_column);
        g_panel = StackPanel();
        g_panel.Name(L"JellyfinFeasibilityProbe");
        g_panel.Orientation(Orientation::Horizontal);
        g_panel.VerticalAlignment(VerticalAlignment::Center);
        g_title = TextBlock();
        g_title.Width(145);
        g_title.VerticalAlignment(VerticalAlignment::Center);
        g_title.TextTrimming(TextTrimming::CharacterEllipsis);
        g_panel.Children().Append(g_title);
        for (int command = 0; command < 3; ++command) {
            Button button;
            button.Width(34); button.Height(32); button.Padding({0,0,0,0});
            button.Content(winrt::box_value(command == 0 ? L"|◀" : command == 1 ? L"▶" : L"▶|"));
            Automation::AutomationProperties::SetName(button, command == 0 ? L"前の曲" : command == 1 ? L"再生" : L"次の曲");
            button.Click([command](auto const&, auto const&) {
                if (g_unloading) return;
                try {
                    if (command == 0) g_track = g_track == 1 ? 3 : g_track - 1;
                    if (command == 1) g_playing = !g_playing;
                    if (command == 2) g_track = g_track == 3 ? 1 : g_track + 1;
                    UpdateMock();
                } catch (...) { Wh_Log(L"FAIL: mock click"); }
            });
            if (command == 1) g_play = button;
            g_panel.Children().Append(button);
        }
        UpdateMock();
        Grid::SetColumn(g_panel, col);
        target.Children().Append(g_panel);
        Wh_Log(L"UI inserted; explorer pid=%lu column=%d", GetCurrentProcessId(), col);
    } catch (...) {
        Wh_Log(L"FAIL: apply; attempting cleanup");
        try { RemoveProbe(); } catch (...) { Wh_Log(L"FAIL: cleanup"); }
    }
}
static HWND FindPrimaryTaskbar() {
    HWND result = nullptr;
    EnumWindows([](HWND window, LPARAM param) CALLBACK -> BOOL {
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
BOOL Wh_ModInit() {
    g_unloading = false;
    return HookTaskbarDllSymbols();
}
void Wh_ModAfterInit() {
    g_window = FindPrimaryTaskbar();
    if (!g_window) { Wh_Log(L"FAIL: taskbar not ready; restart recovery NOT proven"); return; }
    RunFromWindowThread(g_window, [](void*) { ApplyProbe(); }, nullptr);
}
void Wh_ModSettingsChanged() {
    g_window = FindPrimaryTaskbar();
    if (g_window) RunFromWindowThread(g_window, [](void*) { ApplyProbe(); }, nullptr);
}
void Wh_ModUninit() {
    g_unloading = true;
    if (g_window && IsWindow(g_window)) RunFromWindowThread(g_window, [](void*) {
        try { RemoveProbe(); } catch (...) { Wh_Log(L"FAIL: unload cleanup"); }
    }, nullptr);
}

