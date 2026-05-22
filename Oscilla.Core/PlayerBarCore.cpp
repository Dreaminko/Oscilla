#include "pch.h" // 必须是第一行
#include <string.h>

extern "C" {
    // 播放控制接口
    __declspec(dllexport) void Core_TogglePlayback(bool start) {
        // 后端音频流控制
    }

    // 循环模式接口
    __declspec(dllexport) void Core_SetLoopMode(int mode) {
        // 0: All, 1: Uni
    }

    // 获取实时参数
    __declspec(dllexport) void Core_GetAudioSpecs(int* br, int* sr) {
        if (br) *br = 1411;
        if (sr) *sr = 44100;
    }
}