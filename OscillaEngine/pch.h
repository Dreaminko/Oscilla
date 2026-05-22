// pch.h: 这是预编译标头文件。
// 下方列出的文件仅编译一次，提高了将来生成的生成性能。
// 这还将影响 IntelliSense 性能，包括代码完成和许多代码浏览功能。
// 但是，如果此处列出的文件中的任何一个在生成之间有更新，它们全部都将被重新编译。
// 请勿在此处添加要频繁更新的文件，这将使得性能优势无效。

#ifndef PCH_H
#define PCH_H

// 1. 这一行必须是老大哥，因为它负责把 windows.h 领进来
#include "framework.h" 

// 2. 后面再写你的业务代码
extern "C" __declspec(dllexport) int GetEngineVersion() {
    return 2026;
}

#endif // PCH_H