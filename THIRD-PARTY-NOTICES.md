# Third-party notices

SpoitableToolKit is released under the MIT License (see LICENSE). That license
covers SpoitableToolKit's own code only. The app also includes the third-party
software listed below, and each piece of it stays under its own license, which
is not changed by the MIT License of the app. When several entries share a
license, its full text is given once under "License texts" at the end, and each
entry gives its own copyright line.

Chromium, which runs inside CEF, includes many more components under their own
licenses. The app shows all of them: Settings > Licenses > Chromium licenses
opens chrome://credits from the Chromium inside the app.


## Included in the app

### CefSharp

- NuGet: CefSharp.Common.NETCore 151.3.170, CefSharp.OffScreen.NETCore 151.3.170, CefSharp.WinForms.NETCore 151.3.170
- License: BSD 3-Clause (below; also licenses/CefSharp/LICENSE)
- Copyright: Copyright © The CefSharp Authors. All rights reserved.
- Source: https://github.com/cefsharp/CefSharp

    // Copyright © The CefSharp Authors. All rights reserved.
    //
    // Redistribution and use in source and binary forms, with or without
    // modification, are permitted provided that the following conditions are
    // met:
    //
    //    * Redistributions of source code must retain the above copyright
    //      notice, this list of conditions and the following disclaimer.
    //
    //    * Redistributions in binary form must reproduce the above
    //      copyright notice, this list of conditions and the following disclaimer
    //      in the documentation and/or other materials provided with the
    //      distribution.
    //
    //    * Neither the name of Google Inc. nor the name Chromium Embedded
    //      Framework nor the name CefSharp nor the names of its contributors
    //      may be used to endorse or promote products derived from this software
    //      without specific prior written permission.
    //
    // THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
    // "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
    // LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
    // A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
    // OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
    // SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
    // LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
    // DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
    // THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
    // (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
    // OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

### Chromium Embedded Framework (CEF)

- NuGet: chromiumembeddedframework.runtime 151.3.17, chromiumembeddedframework.runtime.win-x64 151.3.17
- Version: CEF 151.3.17+gf059e67+chromium-151.0.7922.138 (commit f059e67fa6aad5e8cce8bebea5df706ffddfb174 on branch 7922; https://github.com/chromiumembedded/cef/tree/f059e67fa6aad5e8cce8bebea5df706ffddfb174)
- In the app: libcef.dll and the files next to it (see Chromium below)
- License: BSD 3-Clause (below; also licenses/CEF/LICENSE.txt)
- Copyright: Copyright (c) 2008-2020 Marshall A. Greenblatt. Portions Copyright (c) 2006-2009 Google Inc. All rights reserved.
- Source: https://github.com/chromiumembedded/cef

    // Copyright (c) 2008-2020 Marshall A. Greenblatt. Portions Copyright (c)
    // 2006-2009 Google Inc. All rights reserved.
    //
    // Redistribution and use in source and binary forms, with or without
    // modification, are permitted provided that the following conditions are
    // met:
    //
    //    * Redistributions of source code must retain the above copyright
    // notice, this list of conditions and the following disclaimer.
    //    * Redistributions in binary form must reproduce the above
    // copyright notice, this list of conditions and the following disclaimer
    // in the documentation and/or other materials provided with the
    // distribution.
    //    * Neither the name of Google Inc. nor the name Chromium Embedded
    // Framework nor the names of its contributors may be used to endorse
    // or promote products derived from this software without specific prior
    // written permission.
    //
    // THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
    // "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
    // LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
    // A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
    // OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
    // SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
    // LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
    // DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
    // THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
    // (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
    // OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

### Chromium

- Version: 151.0.7922.138 (tag 151.0.7922.138, commit 41fa82442390a4d4456c78f2d69a832d5720cb27; https://chromium.googlesource.com/chromium/src/+/refs/tags/151.0.7922.138)
- In the app: libcef.dll, chrome_elf.dll, libEGL.dll and libGLESv2.dll (ANGLE), vk_swiftshader.dll and vk_swiftshader_icd.json (SwiftShader), vulkan-1.dll (Vulkan Loader), dxcompiler.dll (DirectX Shader Compiler), icudtl.dat, v8_context_snapshot.bin, the .pak files and locales/
- License: Chromium and the components it includes come under the licenses listed in chrome://credits, which the app opens from its license screen (ANGLE, SwiftShader, Vulkan Loader Components and DirectX-Shader-Compiler are among them). Parts of it are under the GNU Lesser General Public License (below).
- Source: https://source.chromium.org/chromium

    libcef.dll contains code under the GNU Lesser General Public License:
    parts of Blink (which comes from WebKit) and FFmpeg. Their licenses are in
    chrome://credits ("WebKit" and "ffmpeg"). The source of this libcef.dll is
    the CEF and Chromium source at these versions:

      CEF 151.3.17+gf059e67+chromium-151.0.7922.138
        https://github.com/chromiumembedded/cef/tree/f059e67fa6aad5e8cce8bebea5df706ffddfb174
        (commit f059e67fa6aad5e8cce8bebea5df706ffddfb174, on branch 7922)
      Chromium 151.0.7922.138
        https://chromium.googlesource.com/chromium/src/+/refs/tags/151.0.7922.138
        (commit 41fa82442390a4d4456c78f2d69a832d5720cb27)

    You may replace libcef.dll with one built from that source, changed or not.

    This libcef.dll is built without proprietary codecs: it does not play
    H.264, HEVC or AAC. In that build, FFmpeg is configured as "LGPL version 2.1
    or later", with CONFIG_GPL 0, CONFIG_GPLV3 0 and CONFIG_NONFREE 0
    (chromium/config/Chromium/win/x64/config.h of Chromium's FFmpeg at revision
    ad41607c61898cf7150e0fb20fe4bbabd44922a3, the revision in the DEPS of
    Chromium 151.0.7922.138).

### OpenVR

- NuGet: VolcanicArts.Libs.OpenVR 2.5.1
- In the app: openvr_api.dll and OpenVR.dll (the C# interface of OpenVR)
- License: BSD 3-Clause (below). The package does not carry a license file; its files are Valve's OpenVR SDK 2.5.1, whose license is this one.
- Copyright: Copyright (c) 2015, Valve Corporation
- Source: https://github.com/ValveSoftware/openvr

    Copyright (c) 2015, Valve Corporation
    All rights reserved.

    Redistribution and use in source and binary forms, with or without modification,
    are permitted provided that the following conditions are met:

    1. Redistributions of source code must retain the above copyright notice, this
    list of conditions and the following disclaimer.

    2. Redistributions in binary form must reproduce the above copyright notice,
    this list of conditions and the following disclaimer in the documentation and/or
    other materials provided with the distribution.

    3. Neither the name of the copyright holder nor the names of its contributors
    may be used to endorse or promote products derived from this software without
    specific prior written permission.

    THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
    ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
    WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
    DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR
    ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
    (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
    LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON
    ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
    (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
    SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

### Vortice.Windows

- NuGet: Vortice.Direct3D11 3.8.3, Vortice.DirectX 3.8.3, Vortice.DXGI 3.8.3
- License: MIT (see "MIT License" under License texts)
- Copyright: Copyright (c) Amer Koleci and Contributors
- Source: https://github.com/amerkoleci/Vortice.Windows

### Vortice.Mathematics

- NuGet: Vortice.Mathematics 2.1.0
- License: MIT (see "MIT License" under License texts)
- Copyright: Copyright (c) Amer Koleci and contributors
- Source: https://github.com/amerkoleci/Vortice.Mathematics

### SharpGen.Runtime

- NuGet: SharpGen.Runtime 2.4.2-beta, SharpGen.Runtime.COM 2.4.2-beta
- License: MIT (see "MIT License" under License texts)
- Copyright: Copyright (c) 2010-2017 Alexandre Mutel, 2017 Jeremy Koritzinsky; (c) 2010-2017 Alexandre Mutel, 2017-2023 Jeremy Koritzinsky, 2023-2024 Amer Koleci
- Source: https://github.com/SharpGenTools/SharpGenTools

### Microsoft.Data.Sqlite

- NuGet: Microsoft.Data.Sqlite 10.0.15, Microsoft.Data.Sqlite.Core 10.0.15
- License: MIT (see "MIT License" under License texts)
- Copyright: Copyright (c) .NET Foundation and Contributors; © Microsoft Corporation. All rights reserved.
- Source: https://github.com/dotnet/efcore

### SQLitePCLRaw

- NuGet: SQLitePCLRaw.bundle_e_sqlite3 2.1.13, SQLitePCLRaw.core 2.1.13, SQLitePCLRaw.lib.e_sqlite3 2.1.13, SQLitePCLRaw.provider.e_sqlite3 2.1.13
- License: Apache License 2.0 (see "Apache License 2.0" under License texts)
- Copyright: Copyright 2014-2024 SourceGear, LLC
- Source: https://github.com/ericsink/SQLitePCL.raw

### SQLite

- In the app: e_sqlite3.dll (from SQLitePCLRaw.lib.e_sqlite3)
- License: public domain
- Source: https://www.sqlite.org/

    All of the code and documentation in SQLite has been dedicated to the public
    domain by the authors. Anyone is free to copy, modify, publish, use, compile,
    sell, or distribute the original SQLite code, either in source code form or
    as a compiled binary, for any purpose, commercial or non-commercial, and by
    any means. (https://www.sqlite.org/copyright.html)

### Microsoft.Extensions

- NuGet: Microsoft.Extensions.Configuration 10.0.15, Microsoft.Extensions.Configuration.Abstractions 10.0.15, Microsoft.Extensions.Configuration.Binder 10.0.15, Microsoft.Extensions.DependencyInjection 10.0.15, Microsoft.Extensions.DependencyInjection.Abstractions 10.0.15, Microsoft.Extensions.Logging 10.0.15, Microsoft.Extensions.Logging.Abstractions 10.0.15, Microsoft.Extensions.Logging.Configuration 10.0.15, Microsoft.Extensions.Logging.Console 10.0.15, Microsoft.Extensions.Options 10.0.15, Microsoft.Extensions.Options.ConfigurationExtensions 10.0.15, Microsoft.Extensions.Primitives 10.0.15
- License: MIT (see "MIT License" under License texts). The notices of the third-party code in these libraries are in licenses/dotnet/THIRD-PARTY-NOTICES.TXT.
- Copyright: Copyright (c) .NET Foundation and Contributors; © Microsoft Corporation. All rights reserved.
- Source: https://github.com/dotnet/runtime

### Windows SDK

- NuGet: Microsoft.Windows.SDK.NET.Ref 10.0.19041.57
- In the app: modules/hrs/Microsoft.Windows.SDK.NET.dll and modules/hrs/WinRT.Runtime.dll (both from the Microsoft.Windows.SDK.NET.Ref package, unmodified), and d3dcompiler_47.dll and dxil.dll (Microsoft's Direct3D files that come with CEF)
- License: Microsoft Software License Terms for the Microsoft Windows Software Development Kit for Windows 10 (https://aka.ms/WinSDKLicenseURL). The files are Distributable Code on its REDIST.TXT lists (https://learn.microsoft.com/en-us/legal/windows-sdk/redist): the files of the Microsoft.Windows.SDK.NET.Ref package may be distributed unmodified as part of a program so that it can call WinRT APIs, and d3dcompiler_47.dll and dxil.dll are on the list of Direct3D files that may be distributed with Classic Windows applications.
- Copyright: Copyright (c) Microsoft Corporation. All rights reserved.
- Source: the source code of C#/WinRT, which WinRT.Runtime.dll is built from, is under the MIT License (https://github.com/microsoft/CsWinRT); the WinRT.Runtime.dll the app distributes is the one in the package, under the package's terms.

    These files are not under the MIT License. They are distributed unmodified
    under the Microsoft Software License Terms for the Windows SDK, and those
    terms apply to any use of these files.

dxcompiler.dll, which also comes with CEF and whose name is on the same
Direct3D list, is not the Windows SDK's file: it is a build of the open source
DirectX Shader Compiler made for Chromium (its file description says "Google
Dawn Custom Build"), and the licenses of its source are in chrome://credits
("DirectX-Shader-Compiler").

### .NET

- In the app: SpoitableToolKit.Host.exe (the .NET app host), Ijwhost.dll (part of .NET, carried by CefSharp) and, in the release build, the .NET runtime (Microsoft.NETCore.App and Microsoft.WindowsDesktop.App)
- License: MIT (see "MIT License" under License texts), except Microsoft.DiaSymReader.Native.amd64.dll (in the release build), which comes under the .NET Library License (https://dotnet.microsoft.com/dotnet_library_license.htm; https://github.com/dotnet/core/blob/main/license-information-windows.md). The notices of the third-party code in .NET are in licenses/dotnet/THIRD-PARTY-NOTICES.TXT.
- Copyright: Copyright (c) .NET Foundation and Contributors
- Source: https://github.com/dotnet/dotnet

### Velopack

- NuGet: Velopack 1.2.161
- In the app: Velopack.dll (the app's side of updating), Update.exe (updates the app) and Setup.exe (the installer)
- License: MIT (see "MIT License" under License texts)
- Copyright: Copyright © 2021 Caelan Sayler; Copyright © 2024 Velopack Ltd.
- Source: https://github.com/velopack/velopack

### Velopack: Rust crates in Update.exe and Setup.exe

- In the app: Update.exe and Setup.exe are built from Velopack's Rust code (https://github.com/velopack/velopack at commit 92d6a1c91716729d449034df5c50307dcce39493, the commit that the Velopack 1.2.161 package names), and contain the Rust crates below and the Rust standard library (MIT OR Apache-2.0; https://github.com/rust-lang/rust). Velopack does not ship a list of these crates, so it is made here from Velopack's Cargo.lock (tools/licenses/velopack-crates.ps1).
- Crates: 195, the normal dependencies of the velopack_bins package (which builds Update.exe and Setup.exe) in Cargo.lock, as Velopack builds it for Windows (features "windows", target x86_64-win7-windows-msvc). Build-only, dev-only and procedural-macro crates are left out. The source of each is at https://crates.io/crates/<name>/<version>.
- License: each crate's own. Where a crate offers a choice (for example "MIT OR Apache-2.0"), the app uses the one it is listed under below.

#### MIT License (the text is under "MIT License" in License texts) (153 crates)

- adler2 2.0.1 (offered as 0BSD OR MIT OR Apache-2.0): authors: Jonas Schievink, oyvindln
- aho-corasick 1.1.5 (offered as Unlicense OR MIT): Copyright (c) 2015 Andrew Gallant
- aligned 0.4.3 (offered as MIT OR Apache-2.0): Copyright (c) 2017 Jorge Aparicio
- anstream 1.0.0 (offered as MIT OR Apache-2.0): Copyright (c) Individual contributors
- anstyle 1.0.14 (offered as MIT OR Apache-2.0): Copyright (c) Individual contributors
- anstyle-parse 1.0.0 (offered as MIT OR Apache-2.0): Copyright (c) Individual contributors
- anstyle-query 1.1.5 (offered as MIT OR Apache-2.0): Copyright (c) Individual contributors
- anstyle-wincon 3.0.11 (offered as MIT OR Apache-2.0): Copyright (c) Individual contributors
- anyhow 1.0.104 (offered as MIT OR Apache-2.0): authors: David Tolnay
- as-slice 0.2.1 (offered as MIT OR Apache-2.0): Copyright (c) 2018 Jorge Aparicio
- base64 0.23.1 (offered as MIT OR Apache-2.0): Copyright (c) 2025 Alice Maz, Marshall Pierce
- bitflags 2.13.2 (offered as MIT OR Apache-2.0): Copyright (c) 2014 The Rust Project Developers
- block-buffer 0.12.1 (offered as MIT OR Apache-2.0): Copyright (c) 2018-2025 The RustCrypto Project Developers
- bumpalo 3.20.3 (offered as MIT OR Apache-2.0): Copyright (c) 2019 Nick Fitzgerald
- bytemuck 1.25.2 (offered as Zlib OR Apache-2.0 OR MIT): Copyright (c) 2019 Daniel "Lokathor" Gee.
- byteorder-lite 0.1.0 (offered as Unlicense OR MIT): Copyright (c) 2015 Andrew Gallant
- bytes 1.12.1: Copyright (c) 2018 Carl Lerche
- cfg-if 1.0.5 (offered as MIT OR Apache-2.0): Copyright (c) 2014 Alex Crichton
- chacha20 0.10.2 (offered as MIT OR Apache-2.0): Copyright (c) 2019-2026 The RustCrypto Project Developers
- chrono 0.4.45 (offered as MIT OR Apache-2.0): Copyright (c) 2014, Kang Seonghoon.
- clap 4.6.7 (offered as MIT OR Apache-2.0): Copyright (c) Individual contributors
- clap_builder 4.6.7 (offered as MIT OR Apache-2.0): Copyright (c) Individual contributors
- clap_lex 1.1.1 (offered as MIT OR Apache-2.0): Copyright (c) Individual contributors
- color_quant 1.1.0: Copyright (c) 2016 PistonDevelopers
- colorchoice 1.0.5 (offered as MIT OR Apache-2.0): Copyright (c) Individual contributors
- const-oid 0.10.2 (offered as Apache-2.0 OR MIT): Copyright (c) 2020-2026 The RustCrypto Project Developers
- cpufeatures 0.3.1 (offered as MIT OR Apache-2.0): Copyright (c) 2020-2026 The RustCrypto Project Developers
- crc32fast 1.5.2 (offered as MIT OR Apache-2.0): Copyright (c) 2018 Sam Rijs, Alex Crichton and contributors
- crossbeam-deque 0.8.8 (offered as MIT OR Apache-2.0): Copyright (c) 2019 The Crossbeam Project Developers
- crossbeam-epoch 0.9.21 (offered as MIT OR Apache-2.0): Copyright (c) 2019 The Crossbeam Project Developers
- crossbeam-utils 0.8.23 (offered as MIT OR Apache-2.0): Copyright (c) 2019 The Crossbeam Project Developers
- crypto-common 0.2.2 (offered as MIT OR Apache-2.0): Copyright (c) 2021-2026 RustCrypto Developers
- deranged 0.5.8 (offered as MIT OR Apache-2.0): Copyright (c) 2024 Jacob Pratt et al.
- digest 0.11.3 (offered as MIT OR Apache-2.0): Copyright (c) 2017-2025 RustCrypto Developers; Copyright (c) 2017 Artyom Pavlov
- either 1.18.0 (offered as MIT OR Apache-2.0): Copyright (c) 2015
- equivalent 1.0.2 (offered as Apache-2.0 OR MIT): Copyright (c) 2016--2023
- fast_image_resize 6.1.0 (offered as MIT OR Apache-2.0): Copyright (c) 2021 Kirill Kuzminykh
- fdeflate 0.3.7 (offered as MIT OR Apache-2.0): authors: The image-rs Developers
- flate2 1.1.10 (offered as MIT OR Apache-2.0): Copyright (c) 2014-2026 Alex Crichton
- fluent 0.17.0 (offered as Apache-2.0 OR MIT): Copyright 2017 Mozilla
- fluent-bundle 0.16.0 (offered as Apache-2.0 OR MIT): Copyright 2017 Mozilla
- fluent-langneg 0.13.1 (offered as Apache-2.0 OR MIT): authors: Zibi Braniecki
- fluent-langneg 0.14.2 (offered as Apache-2.0 OR MIT): authors: Zibi Braniecki
- fluent-syntax 0.12.0 (offered as Apache-2.0 OR MIT): Copyright 2017 Mozilla
- form_urlencoded 1.2.2 (offered as MIT OR Apache-2.0): Copyright (c) 2013-2016 The rust-url developers
- fs_extra 1.3.0: Copyright (c) 2017 Denis Kurilenko
- getrandom 0.2.17 (offered as MIT OR Apache-2.0): Copyright (c) 2018-2024 The rust-random Project Developers; Copyright (c) 2014 The Rust Project Developers
- getrandom 0.4.3 (offered as MIT OR Apache-2.0): Copyright (c) 2018-2026 The rust-random Project Developers; Copyright (c) 2014 The Rust Project Developers
- gif 0.14.2 (offered as MIT OR Apache-2.0): Copyright (c) 2015 nwin
- glob 0.3.4 (offered as MIT OR Apache-2.0): Copyright (c) 2014 The Rust Project Developers
- hashbrown 0.17.1 (offered as MIT OR Apache-2.0): Copyright (c) 2016 Amanieu d'Antras
- http 1.5.0 (offered as MIT OR Apache-2.0): Copyright (c) 2017 http-rs authors
- httparse 1.10.1 (offered as MIT OR Apache-2.0): Copyright (c) 2015-2025 Sean McArthur
- hybrid-array 0.4.15 (offered as MIT OR Apache-2.0): Copyright (c) 2022-2026 The RustCrypto Project Developers
- idna 1.1.0 (offered as MIT OR Apache-2.0): Copyright (c) 2013-2025 The rust-url developers
- idna_adapter 1.2.2 (offered as Apache-2.0 OR MIT): Copyright (c) The rust-url developers
- image 0.25.10 (offered as MIT OR Apache-2.0): authors: The image-rs Developers
- indexmap 2.14.2 (offered as Apache-2.0 OR MIT): Copyright (c) 2016--2017
- intl_pluralrules 7.0.2 (offered as Apache-2.0/MIT): authors: Kekoa Riggin, Zibi Braniecki
- intl-memoizer 0.5.3 (offered as Apache-2.0 OR MIT): Copyright 2017 Mozilla
- is_terminal_polyfill 1.70.2 (offered as MIT OR Apache-2.0): Copyright (c) Individual contributors
- itoa 1.0.18 (offered as MIT OR Apache-2.0): authors: David Tolnay
- lazy_static 1.5.0 (offered as MIT OR Apache-2.0): Copyright (c) 2010 The Rust Project Developers
- libc 0.2.189 (offered as MIT OR Apache-2.0): Copyright (c) The Rust Project Developers
- log 0.4.34 (offered as MIT OR Apache-2.0): Copyright (c) 2014 The Rust Project Developers
- log-panics 2.1.0 (offered as MIT/Apache-2.0): Copyright (c) 2016 The rust-log-panics Developers
- memchr 2.8.3 (offered as Unlicense OR MIT): Copyright (c) 2015 Andrew Gallant
- memmap2 0.9.11 (offered as MIT OR Apache-2.0): Copyright (c) 2020 Yevhenii Reizner; Copyright (c) 2015 Dan Burkert
- miniz_oxide 0.8.9 (offered as MIT OR Zlib OR Apache-2.0): Copyright 2013-2014 RAD Game Tools and Valve Software; Copyright 2010-2014 Rich Geldreich and Tenacious Software LLC; Copyright (c) 2017 Frommi; Copyright (c) 2017-2024 oyvindln
- miniz_oxide 0.9.1 (offered as MIT OR Zlib OR Apache-2.0): Copyright 2013-2014 RAD Game Tools and Valve Software; Copyright 2010-2014 Rich Geldreich and Tenacious Software LLC; Copyright (c) 2017 Frommi; Copyright (c) 2017-2024 oyvindln
- normpath 1.5.2 (offered as MIT OR Apache-2.0): Copyright (c) 2020 dylni (https://github.com/dylni)
- num-conv 0.2.2 (offered as MIT OR Apache-2.0): Copyright (c) Jacob Pratt
- num-traits 0.2.19 (offered as MIT OR Apache-2.0): Copyright (c) 2014 The Rust Project Developers
- once_cell 1.21.4 (offered as MIT OR Apache-2.0): authors: Aleksey Kladov
- once_cell_polyfill 1.70.2 (offered as MIT OR Apache-2.0): Copyright (c) Individual contributors
- oneshot 0.2.1 (offered as MIT OR Apache-2.0): authors: Linus Färnstrand
- os_info 3.15.0: Copyright (c) 2017 Stanislav Tkach
- percent-encoding 2.3.2 (offered as MIT OR Apache-2.0): Copyright (c) 2013-2025 The rust-url developers
- png 0.18.1 (offered as MIT OR Apache-2.0): Copyright (c) 2015 nwin
- powerfmt 0.2.0 (offered as MIT OR Apache-2.0): Copyright (c) 2023 Jacob Pratt et al.
- progress-streams 1.1.0: Copyright (c) 2018 System76
- rand 0.10.3 (offered as MIT OR Apache-2.0): Copyright 2018 Developers of the Rand project; Copyright (c) 2014 The Rust Project Developers
- rand_core 0.10.1 (offered as MIT OR Apache-2.0): Copyright (c) 2018-2026 The Rand Project Developers
- rayon 1.12.0 (offered as MIT OR Apache-2.0): Copyright (c) 2010 The Rust Project Developers
- rayon-core 1.13.0 (offered as MIT OR Apache-2.0): Copyright (c) 2010 The Rust Project Developers
- regex 1.13.1 (offered as MIT OR Apache-2.0): Copyright (c) 2014 The Rust Project Developers
- regex-automata 0.4.18 (offered as MIT OR Apache-2.0): Copyright (c) 2014 The Rust Project Developers
- regex-syntax 0.8.11 (offered as MIT OR Apache-2.0): Copyright (c) 2014 The Rust Project Developers
- remove_dir_all 1.0.0 (offered as MIT OR Apache-2.0): Copyright (c) 2017 Aaron Power
- rustc-hash 2.1.3 (offered as Apache-2.0 OR MIT): authors: The Rust Project Developers
- rustls 0.23.45 (offered as Apache-2.0 OR ISC OR MIT): Copyright (c) 2016 Joseph Birr-Pixton <jpixton@gmail.com>
- rustls-pki-types 1.15.1 (offered as MIT OR Apache-2.0): Copyright (c) 2023 Dirkjan Ochtman <dirkjan@ochtman.nl>
- same-file 1.0.6 (offered as Unlicense/MIT): Copyright (c) 2017 Andrew Gallant
- semver 1.0.28 (offered as MIT OR Apache-2.0): authors: David Tolnay
- serde 1.0.229 (offered as MIT OR Apache-2.0): authors: Erick Tryzelaar, David Tolnay
- serde_core 1.0.229 (offered as MIT OR Apache-2.0): authors: Erick Tryzelaar, David Tolnay
- serde_json 1.0.151 (offered as MIT OR Apache-2.0): authors: Erick Tryzelaar, David Tolnay
- sha1 0.11.0 (offered as MIT OR Apache-2.0): Copyright (c) 2016-2026 The RustCrypto Project Developers; Copyright (c) 2016 Artyom Pavlov; Copyright (c) 2009-2013 Mozilla Foundation; Copyright (c) 2006-2009 Graydon Hoare
- sha2 0.11.0 (offered as MIT OR Apache-2.0): Copyright (c) 2016-2026 The RustCrypto Project Developers; Copyright (c) 2016 Artyom Pavlov; Copyright (c) 2009-2013 Mozilla Foundation; Copyright (c) 2006-2009 Graydon Hoare
- simd-adler32 0.3.10: Copyright (c) [2021] [Marvin Countryman]
- simplelog 0.12.2 (offered as MIT OR Apache-2.0): Copyright (c) 2015 Victor Brekenfeld
- simple-stopwatch 0.1.4: Copyright (c) 2018 Huw Bowles
- smallvec 1.16.1 (offered as MIT OR Apache-2.0): Copyright (c) 2018 The Servo Project Developers
- stable_deref_trait 1.2.1 (offered as MIT OR Apache-2.0): Copyright (c) 2017 Robert Grosse
- strsim 0.11.1: Copyright (c) 2015 Danny Guo; Copyright (c) 2016 Titus Wormer <tituswormer@gmail.com>; Copyright (c) 2018 Akash Kurdekar
- strum 0.28.0: Copyright (c) 2019 Peter Glotfelty
- sys-locale 0.3.2 (offered as MIT OR Apache-2.0): Copyright (c) 2021 1Password
- termcolor 1.4.1 (offered as Unlicense OR MIT): Copyright (c) 2015 Andrew Gallant
- thiserror 2.0.20 (offered as MIT OR Apache-2.0): authors: David Tolnay
- time 0.1.45 (offered as MIT/Apache-2.0): Copyright (c) 2014 The Rust Project Developers
- time 0.3.55 (offered as MIT OR Apache-2.0): Copyright (c) Jacob Pratt et al.
- time-core 0.1.9 (offered as MIT OR Apache-2.0): Copyright (c) Jacob Pratt et al.
- typed-path 0.12.3 (offered as MIT OR Apache-2.0): authors: Chip Senkbeil
- type-map 0.5.1 (offered as MIT/Apache-2.0): Copyright (c) 2022 Jacob Brown; Copyright (c) 2017-NOW Actix Team
- typenum 1.20.1 (offered as MIT OR Apache-2.0): Copyright (c) 2014 Paho Lurie-Gregg
- unic-langid 0.9.6 (offered as MIT OR Apache-2.0): authors: Zibi Braniecki
- unic-langid-impl 0.9.6 (offered as MIT OR Apache-2.0): authors: Zibi Braniecki
- ureq 3.4.2 (offered as MIT OR Apache-2.0): Copyright (c) 2019 Martin Algesten
- ureq-proto 0.6.4 (offered as MIT OR Apache-2.0): Copyright 2022 Martin Algesten
- url 2.5.8 (offered as MIT OR Apache-2.0): Copyright (c) 2013-2025 The rust-url developers
- utf8_iter 1.0.4 (offered as Apache-2.0 OR MIT): Copyright Mozilla Foundation
- utf8parse 0.2.2 (offered as Apache-2.0 OR MIT): Copyright (c) 2016 Joe Wilm
- utf8-zero 0.8.1 (offered as MIT OR Apache-2.0): authors: Simon Sapin, Martin Algesten
- uuid 1.26.1 (offered as Apache-2.0 OR MIT): Copyright (c) 2014 The Rust Project Developers; Copyright (c) 2018 Ashley Mannix, Christopher Armstrong, Dylan DPC, Hunar Roop Kahlon
- wait-timeout 0.2.1 (offered as MIT/Apache-2.0): Copyright (c) 2014 Alex Crichton
- walkdir 2.5.0 (offered as Unlicense/MIT): Copyright (c) 2015 Andrew Gallant
- webview2-com-sys 0.39.1: the crate gives no copyright line or author
- weezl 0.1.12 (offered as MIT OR Apache-2.0): Copyright (c) HeroicKatora 2020
- widestring 1.2.1 (offered as MIT OR Apache-2.0): the crate gives no copyright line or author
- winapi 0.3.9 (offered as MIT/Apache-2.0): Copyright (c) 2015-2018 The winapi-rs Developers
- winapi-util 0.1.11 (offered as Unlicense OR MIT): Copyright (c) 2017 Andrew Gallant
- windows 0.62.2 (offered as MIT OR Apache-2.0): Copyright (c) Microsoft Corporation.
- windows_x86_64_msvc 0.52.6 (offered as MIT OR Apache-2.0): Copyright (c) Microsoft Corporation.
- windows-collections 0.3.2 (offered as MIT OR Apache-2.0): Copyright (c) Microsoft Corporation.
- windows-core 0.62.2 (offered as MIT OR Apache-2.0): Copyright (c) Microsoft Corporation.
- windows-future 0.3.2 (offered as MIT OR Apache-2.0): Copyright (c) Microsoft Corporation.
- windows-link 0.2.1 (offered as MIT OR Apache-2.0): Copyright (c) Microsoft Corporation.
- windows-numerics 0.3.1 (offered as MIT OR Apache-2.0): Copyright (c) Microsoft Corporation.
- windows-result 0.4.1 (offered as MIT OR Apache-2.0): Copyright (c) Microsoft Corporation.
- windows-strings 0.5.1 (offered as MIT OR Apache-2.0): Copyright (c) Microsoft Corporation.
- windows-sys 0.52.0 (offered as MIT OR Apache-2.0): Copyright (c) Microsoft Corporation.
- windows-sys 0.59.0 (offered as MIT OR Apache-2.0): Copyright (c) Microsoft Corporation.
- windows-sys 0.61.2 (offered as MIT OR Apache-2.0): Copyright (c) Microsoft Corporation.
- windows-targets 0.52.6 (offered as MIT OR Apache-2.0): Copyright (c) Microsoft Corporation.
- windows-threading 0.2.1 (offered as MIT OR Apache-2.0): Copyright (c) Microsoft Corporation.
- winreg 0.56.0: Copyright (c) 2015 Igor Shaula
- xdialog 3.1.9: Copyright © 2024 Caelan Sayler, Velopack Ltd.
- xml 1.4.0: Copyright (c) 2014 Vladimir Matveev
- zeroize 1.9.0 (offered as Apache-2.0 OR MIT): Copyright (c) 2018-2026 The RustCrypto Project Developers
- zip 8.6.0: Copyright (c) 2014 Mathijs van de Nes
- zmij 1.0.23: authors: David Tolnay
- zune-core 0.5.3 (offered as MIT OR Apache-2.0 OR Zlib): Copyright (c) zune-image developers
- zune-jpeg 0.5.15 (offered as MIT OR Apache-2.0 OR Zlib): Copyright (c) zune-image developers

#### Apache License 2.0 (the text is under "Apache License 2.0" in License texts) (7 crates)

- cvt 0.1.2: authors: Marcin Mielniczuk
- fs_at 0.2.1: authors: Robert Collins
- moxcms 0.8.1 (offered as BSD-3-Clause OR Apache-2.0): Copyright 2024 Radzivon Bartoshyk
- pretty-bytes-rust 0.3.0: authors: Patrick Desjardins
- pxfm 0.1.30 (offered as BSD-3-Clause OR Apache-2.0): Copyright 2024 Radzivon Bartoshyk
- self_cell 1.3.0 (offered as Apache-2.0 OR GPL-2.0-only): authors: Lukas Bergdoll
- zopfli 0.8.3: Copyright 2011 Google Inc.

#### Unicode License v3 (24 crates)

- icu_collections 2.3.0: Copyright © 2020-2024 Unicode, Inc.
- icu_locale_core 2.3.0: Copyright © 2020-2024 Unicode, Inc.
- icu_locid 1.5.0: Copyright © 2020-2024 Unicode, Inc.
- icu_locid_transform 1.5.0: Copyright © 2020-2024 Unicode, Inc.
- icu_locid_transform_data 1.5.1: Copyright © 2020-2024 Unicode, Inc.
- icu_normalizer 2.3.0: Copyright © 2020-2024 Unicode, Inc.
- icu_normalizer_data 2.3.0: Copyright © 2020-2024 Unicode, Inc.
- icu_properties 2.3.0: Copyright © 2020-2024 Unicode, Inc.
- icu_properties_data 2.3.0: Copyright © 2020-2024 Unicode, Inc.
- icu_provider 1.5.0: Copyright © 2020-2024 Unicode, Inc.
- icu_provider 2.3.1: Copyright © 2020-2024 Unicode, Inc.
- litemap 0.7.5: Copyright © 2020-2024 Unicode, Inc.
- litemap 0.8.3: Copyright © 2020-2024 Unicode, Inc.
- potential_utf 0.1.6: Copyright © 2020-2024 Unicode, Inc.
- tinystr 0.7.6: Copyright © 2020-2024 Unicode, Inc.
- tinystr 0.8.4: Copyright © 2020-2024 Unicode, Inc.
- writeable 0.5.5: Copyright © 2020-2024 Unicode, Inc.
- writeable 0.6.4: Copyright © 2020-2024 Unicode, Inc.
- yoke 0.7.5: Copyright © 2020-2024 Unicode, Inc.
- yoke 0.8.3: Copyright © 2020-2024 Unicode, Inc.
- zerofrom 0.1.8: Copyright © 2020-2024 Unicode, Inc.
- zerotrie 0.2.5: Copyright © 2020-2024 Unicode, Inc.
- zerovec 0.10.4: Copyright © 2020-2024 Unicode, Inc.
- zerovec 0.11.8: Copyright © 2020-2024 Unicode, Inc.

    UNICODE LICENSE V3

    COPYRIGHT AND PERMISSION NOTICE

    Copyright © 2020-2024 Unicode, Inc.

    NOTICE TO USER: Carefully read the following legal agreement. BY
    DOWNLOADING, INSTALLING, COPYING OR OTHERWISE USING DATA FILES, AND/OR
    SOFTWARE, YOU UNEQUIVOCALLY ACCEPT, AND AGREE TO BE BOUND BY, ALL OF THE
    TERMS AND CONDITIONS OF THIS AGREEMENT. IF YOU DO NOT AGREE, DO NOT
    DOWNLOAD, INSTALL, COPY, DISTRIBUTE OR USE THE DATA FILES OR SOFTWARE.

    Permission is hereby granted, free of charge, to any person obtaining a
    copy of data files and any associated documentation (the "Data Files") or
    software and any associated documentation (the "Software") to deal in the
    Data Files or Software without restriction, including without limitation
    the rights to use, copy, modify, merge, publish, distribute, and/or sell
    copies of the Data Files or Software, and to permit persons to whom the
    Data Files or Software are furnished to do so, provided that either (a)
    this copyright and permission notice appear with all copies of the Data
    Files or Software, or (b) this copyright and permission notice appear in
    associated Documentation.

    THE DATA FILES AND SOFTWARE ARE PROVIDED "AS IS", WITHOUT WARRANTY OF ANY
    KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
    MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT OF
    THIRD PARTY RIGHTS.

    IN NO EVENT SHALL THE COPYRIGHT HOLDER OR HOLDERS INCLUDED IN THIS NOTICE
    BE LIABLE FOR ANY CLAIM, OR ANY SPECIAL INDIRECT OR CONSEQUENTIAL DAMAGES,
    OR ANY DAMAGES WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS,
    WHETHER IN AN ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION,
    ARISING OUT OF OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THE DATA
    FILES OR SOFTWARE.

    Except as contained in this notice, the name of a copyright holder shall
    not be used in advertising or otherwise to promote the sale, use or other
    dealings in these Data Files or Software without prior written
    authorization of the copyright holder.

    SPDX-License-Identifier: Unicode-3.0

    —

    Portions of ICU4X may have been adapted from ICU4C and/or ICU4J.
    ICU 1.8.1 to ICU 57.1 © 1995-2016 International Business Machines Corporation and others.

#### BSD 3-Clause License (5 crates)

- sha1_smol 1.0.1: Copyright (c) 2018, the respective contributors, as shown by the AUTHORS file.; Copyright (c) 2006-2009 Graydon Hoare; Copyright (c) 2009-2013 Mozilla Foundation

    [LICENSE]
    BSD 3-Clause License

    Copyright (c) 2018, the respective contributors, as shown by the AUTHORS file.
    All rights reserved.

    Redistribution and use in source and binary forms, with or without
    modification, are permitted provided that the following conditions are met:

    * Redistributions of source code must retain the above copyright notice, this
      list of conditions and the following disclaimer.

    * Redistributions in binary form must reproduce the above copyright notice,
      this list of conditions and the following disclaimer in the documentation
      and/or other materials provided with the distribution.

    * Neither the name of the copyright holder nor the names of its
      contributors may be used to endorse or promote products derived from
      this software without specific prior written permission.

    THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
    AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
    IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
    DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
    FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
    DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
    SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
    CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
    OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
    OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

    -----

    src/simd.rs is licensed under the MIT license:

    Copyright (c) 2006-2009 Graydon Hoare
    Copyright (c) 2009-2013 Mozilla Foundation

    Permission is hereby granted, free of charge, to any
    person obtaining a copy of this software and associated
    documentation files (the "Software"), to deal in the
    Software without restriction, including without
    limitation the rights to use, copy, modify, merge,
    publish, distribute, sublicense, and/or sell copies of
    the Software, and to permit persons to whom the Software
    is furnished to do so, subject to the following
    conditions:

    The above copyright notice and this permission notice
    shall be included in all copies or substantial portions
    of the Software.

    THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF
    ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED
    TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A
    PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT
    SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY
    CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
    OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR
    IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
    DEALINGS IN THE SOFTWARE.

- subtle 2.6.1: Copyright (c) 2016-2017 Isis Agora Lovecruft, Henry de Valence. All rights reserved.; Copyright (c) 2016-2024 Isis Agora Lovecruft. All rights reserved.

    [LICENSE]
    Copyright (c) 2016-2017 Isis Agora Lovecruft, Henry de Valence. All rights reserved.
    Copyright (c) 2016-2024 Isis Agora Lovecruft. All rights reserved.

    Redistribution and use in source and binary forms, with or without
    modification, are permitted provided that the following conditions are
    met:

    1. Redistributions of source code must retain the above copyright
    notice, this list of conditions and the following disclaimer.

    2. Redistributions in binary form must reproduce the above copyright
    notice, this list of conditions and the following disclaimer in the
    documentation and/or other materials provided with the distribution.

    3. Neither the name of the copyright holder nor the names of its
    contributors may be used to endorse or promote products derived from
    this software without specific prior written permission.

    THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS
    IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED
    TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A
    PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
    HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
    SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED
    TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR
    PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF
    LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING
    NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
    SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

- zstd 0.14.0: Copyright (c) 2026, Alexandre Bury

    [LICENSE]
    BSD 3-Clause License

    Copyright (c) 2026, Alexandre Bury

    Redistribution and use in source and binary forms, with or without
    modification, are permitted provided that the following conditions are met:

    1. Redistributions of source code must retain the above copyright notice, this
       list of conditions and the following disclaimer.

    2. Redistributions in binary form must reproduce the above copyright notice,
       this list of conditions and the following disclaimer in the documentation
       and/or other materials provided with the distribution.

    3. Neither the name of the copyright holder nor the names of its
       contributors may be used to endorse or promote products derived from
       this software without specific prior written permission.

    THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
    AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
    IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
    DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
    FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
    DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
    SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
    CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
    OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
    OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

- zstd-safe 8.0.0: Copyright (c) 2026, Alexandre Bury

    [LICENSE]
    BSD 3-Clause License

    Copyright (c) 2026, Alexandre Bury

    Redistribution and use in source and binary forms, with or without
    modification, are permitted provided that the following conditions are met:

    1. Redistributions of source code must retain the above copyright notice, this
       list of conditions and the following disclaimer.

    2. Redistributions in binary form must reproduce the above copyright notice,
       this list of conditions and the following disclaimer in the documentation
       and/or other materials provided with the distribution.

    3. Neither the name of the copyright holder nor the names of its
       contributors may be used to endorse or promote products derived from
       this software without specific prior written permission.

    THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
    AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
    IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
    DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
    FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
    DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
    SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
    CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
    OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
    OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

- zstd-sys 2.1.0+zstd.1.5.7: Copyright (c) 2026, Alexandre Bury; Copyright (c) Meta Platforms, Inc. and affiliates. All rights reserved.

    [LICENSE]
    BSD 3-Clause License

    Copyright (c) 2026, Alexandre Bury

    Redistribution and use in source and binary forms, with or without
    modification, are permitted provided that the following conditions are met:

    1. Redistributions of source code must retain the above copyright notice, this
       list of conditions and the following disclaimer.

    2. Redistributions in binary form must reproduce the above copyright notice,
       this list of conditions and the following disclaimer in the documentation
       and/or other materials provided with the distribution.

    3. Neither the name of the copyright holder nor the names of its
       contributors may be used to endorse or promote products derived from
       this software without specific prior written permission.

    THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
    AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
    IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
    DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
    FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
    DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
    SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
    CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
    OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
    OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

    [LICENSE.BSD-3-Clause]
    The auto-generated bindings are under the 3-clause BSD license:

    BSD License

    For Zstandard software

    Copyright (c) Meta Platforms, Inc. and affiliates. All rights reserved.

    Redistribution and use in source and binary forms, with or without modification,
    are permitted provided that the following conditions are met:

     * Redistributions of source code must retain the above copyright notice, this
       list of conditions and the following disclaimer.

     * Redistributions in binary form must reproduce the above copyright notice,
       this list of conditions and the following disclaimer in the documentation
       and/or other materials provided with the distribution.

     * Neither the name Facebook, nor Meta, nor the names of its contributors may
       be used to endorse or promote products derived from this software without
       specific prior written permission.

    THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
    ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
    WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
    DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR
    ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
    (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
    LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON
    ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
    (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
    SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.


#### ISC License (3 crates)

- libloading 0.9.0: Copyright © 2015, Simonas Kazlauskas

    [LICENSE]
    Copyright © 2015, Simonas Kazlauskas

    Permission to use, copy, modify, and/or distribute this software for any purpose with or without
    fee is hereby granted, provided that the above copyright notice and this permission notice appear
    in all copies.

    THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES WITH REGARD TO THIS
    SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE
    AUTHOR BE LIABLE FOR ANY SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES
    WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN ACTION OF CONTRACT,
    NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF OR IN CONNECTION WITH THE USE OR PERFORMANCE OF
    THIS SOFTWARE.

- rustls-webpki 0.103.15: Copyright 2015 Brian Smith.

    [LICENSE]
    Except as otherwise noted, this project is licensed under the following
    (ISC-style) terms:

    Copyright 2015 Brian Smith.

    Permission to use, copy, modify, and/or distribute this software for any
    purpose with or without fee is hereby granted, provided that the above
    copyright notice and this permission notice appear in all copies.

    THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHORS DISCLAIM ALL WARRANTIES
    WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF
    MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHORS BE LIABLE FOR
    ANY SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES
    WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN
    ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF
    OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.

    The files under third-party/chromium are licensed as described in
    third-party/chromium/LICENSE.

- untrusted 0.9.0: Copyright 2015-2016 Brian Smith.

    [LICENSE.txt]
    // Copyright 2015-2016 Brian Smith.
    //
    // Permission to use, copy, modify, and/or distribute this software for any
    // purpose with or without fee is hereby granted, provided that the above
    // copyright notice and this permission notice appear in all copies.
    //
    // THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHORS DISCLAIM ALL WARRANTIES
    // WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF
    // MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHORS BE LIABLE FOR
    // ANY SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES
    // WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN
    // ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF
    // OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.


#### zlib License (1 crate)

- zlib-rs 0.6.8: (C) 2024 Trifecta Tech Foundation

    [LICENSE]
    (C) 2024 Trifecta Tech Foundation

    This software is provided 'as-is', without any express or implied
    warranty. In no event will the authors be held liable for any damages
    arising from the use of this software.

    Permission is granted to anyone to use this software for any purpose,
    including commercial applications, and to alter it and redistribute it
    freely, subject to the following restrictions:

    1. The origin of this software must not be misrepresented; you must not
       claim that you wrote the original software. If you use this software
       in a product, an acknowledgment in the product documentation would be
       appreciated but is not required.

    2. Altered source versions must be plainly marked as such, and must not be
       misrepresented as being the original software.

    3. This notice may not be removed or altered from any source distribution.


#### Community Data License Agreement - Permissive - Version 2.0 (1 crate)

- webpki-roots 1.0.9: the crate gives no copyright line or author

    [LICENSE]
    # Community Data License Agreement - Permissive - Version 2.0

    This is the Community Data License Agreement - Permissive, Version
    2.0 (the "agreement"). Data Provider(s) and Data Recipient(s) agree
    as follows:

    ## 1. Provision of the Data

    1.1. A Data Recipient may use, modify, and share the Data made
    available by Data Provider(s) under this agreement if that Data
    Recipient follows the terms of this agreement.

    1.2. This agreement does not impose any restriction on a Data
    Recipient's use, modification, or sharing of any portions of the
    Data that are in the public domain or that may be used, modified,
    or shared under any other legal exception or limitation.

    ## 2. Conditions for Sharing Data

    2.1. A Data Recipient may share Data, with or without modifications, so
    long as the Data Recipient makes available the text of this agreement
    with the shared Data.

    ## 3. No Restrictions on Results

    3.1. This agreement does not impose any restriction or obligations
    with respect to the use, modification, or sharing of Results.

    ## 4. No Warranty; Limitation of Liability

    4.1. All Data Recipients receive the Data subject to the following
    terms:

    THE DATA IS PROVIDED ON AN "AS IS" BASIS, WITHOUT REPRESENTATIONS,
    WARRANTIES OR CONDITIONS OF ANY KIND, EITHER EXPRESS OR IMPLIED
    INCLUDING, WITHOUT LIMITATION, ANY WARRANTIES OR CONDITIONS OF TITLE,
    NON-INFRINGEMENT, MERCHANTABILITY OR FITNESS FOR A PARTICULAR PURPOSE.

    NO DATA PROVIDER SHALL HAVE ANY LIABILITY FOR ANY DIRECT, INDIRECT,
    INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING
    WITHOUT LIMITATION LOST PROFITS), HOWEVER CAUSED AND ON ANY THEORY OF
    LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING
    NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE DATA OR RESULTS,
    EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGES.

    ## 5. Definitions

    5.1. "Data" means the material received by a Data Recipient under
    this agreement.

    5.2. "Data Provider" means any person who is the source of Data
    provided under this agreement and in reliance on a Data Recipient's
    agreement to its terms.

    5.3. "Data Recipient" means any person who receives Data directly
    or indirectly from a Data Provider and agrees to the terms of this
    agreement.

    5.4. "Results" means any outcome obtained by computational analysis
    of Data, including for example machine learning models and models'
    insights.


#### Apache-2.0 AND ISC (1 crate)

- ring 0.17.14: Copyright (c) 2009 The Go Authors. All rights reserved.; Copyright 2015 The Chromium Authors. All rights reserved.; Copyright 2015-2025 Brian Smith.

    [LICENSE]
    *ring* uses an "ISC" license, like BoringSSL used to use, for new code
    files. See LICENSE-other-bits for the text of that license.

    See LICENSE-BoringSSL for code that was sourced from BoringSSL under the
    Apache 2.0 license. Some code that was sourced from BoringSSL under the ISC
    license. In each case, the license info is at the top of the file.

    See src/polyfill/once_cell/LICENSE-APACHE and src/polyfill/once_cell/LICENSE-MIT
    for the license to code that was sourced from the once_cell project.

    [LICENSE-BoringSSL]

                                     Apache License
                               Version 2.0, January 2004
                            http://www.apache.org/licenses/

       TERMS AND CONDITIONS FOR USE, REPRODUCTION, AND DISTRIBUTION

       1. Definitions.

          "License" shall mean the terms and conditions for use, reproduction,
          and distribution as defined by Sections 1 through 9 of this document.

          "Licensor" shall mean the copyright owner or entity authorized by
          the copyright owner that is granting the License.

          "Legal Entity" shall mean the union of the acting entity and all
          other entities that control, are controlled by, or are under common
          control with that entity. For the purposes of this definition,
          "control" means (i) the power, direct or indirect, to cause the
          direction or management of such entity, whether by contract or
          otherwise, or (ii) ownership of fifty percent (50%) or more of the
          outstanding shares, or (iii) beneficial ownership of such entity.

          "You" (or "Your") shall mean an individual or Legal Entity
          exercising permissions granted by this License.

          "Source" form shall mean the preferred form for making modifications,
          including but not limited to software source code, documentation
          source, and configuration files.

          "Object" form shall mean any form resulting from mechanical
          transformation or translation of a Source form, including but
          not limited to compiled object code, generated documentation,
          and conversions to other media types.

          "Work" shall mean the work of authorship, whether in Source or
          Object form, made available under the License, as indicated by a
          copyright notice that is included in or attached to the work
          (an example is provided in the Appendix below).

          "Derivative Works" shall mean any work, whether in Source or Object
          form, that is based on (or derived from) the Work and for which the
          editorial revisions, annotations, elaborations, or other modifications
          represent, as a whole, an original work of authorship. For the purposes
          of this License, Derivative Works shall not include works that remain
          separable from, or merely link (or bind by name) to the interfaces of,
          the Work and Derivative Works thereof.

          "Contribution" shall mean any work of authorship, including
          the original version of the Work and any modifications or additions
          to that Work or Derivative Works thereof, that is intentionally
          submitted to Licensor for inclusion in the Work by the copyright owner
          or by an individual or Legal Entity authorized to submit on behalf of
          the copyright owner. For the purposes of this definition, "submitted"
          means any form of electronic, verbal, or written communication sent
          to the Licensor or its representatives, including but not limited to
          communication on electronic mailing lists, source code control systems,
          and issue tracking systems that are managed by, or on behalf of, the
          Licensor for the purpose of discussing and improving the Work, but
          excluding communication that is conspicuously marked or otherwise
          designated in writing by the copyright owner as "Not a Contribution."

          "Contributor" shall mean Licensor and any individual or Legal Entity
          on behalf of whom a Contribution has been received by Licensor and
          subsequently incorporated within the Work.

       2. Grant of Copyright License. Subject to the terms and conditions of
          this License, each Contributor hereby grants to You a perpetual,
          worldwide, non-exclusive, no-charge, royalty-free, irrevocable
          copyright license to reproduce, prepare Derivative Works of,
          publicly display, publicly perform, sublicense, and distribute the
          Work and such Derivative Works in Source or Object form.

       3. Grant of Patent License. Subject to the terms and conditions of
          this License, each Contributor hereby grants to You a perpetual,
          worldwide, non-exclusive, no-charge, royalty-free, irrevocable
          (except as stated in this section) patent license to make, have made,
          use, offer to sell, sell, import, and otherwise transfer the Work,
          where such license applies only to those patent claims licensable
          by such Contributor that are necessarily infringed by their
          Contribution(s) alone or by combination of their Contribution(s)
          with the Work to which such Contribution(s) was submitted. If You
          institute patent litigation against any entity (including a
          cross-claim or counterclaim in a lawsuit) alleging that the Work
          or a Contribution incorporated within the Work constitutes direct
          or contributory patent infringement, then any patent licenses
          granted to You under this License for that Work shall terminate
          as of the date such litigation is filed.

       4. Redistribution. You may reproduce and distribute copies of the
          Work or Derivative Works thereof in any medium, with or without
          modifications, and in Source or Object form, provided that You
          meet the following conditions:

          (a) You must give any other recipients of the Work or
              Derivative Works a copy of this License; and

          (b) You must cause any modified files to carry prominent notices
              stating that You changed the files; and

          (c) You must retain, in the Source form of any Derivative Works
              that You distribute, all copyright, patent, trademark, and
              attribution notices from the Source form of the Work,
              excluding those notices that do not pertain to any part of
              the Derivative Works; and

          (d) If the Work includes a "NOTICE" text file as part of its
              distribution, then any Derivative Works that You distribute must
              include a readable copy of the attribution notices contained
              within such NOTICE file, excluding those notices that do not
              pertain to any part of the Derivative Works, in at least one
              of the following places: within a NOTICE text file distributed
              as part of the Derivative Works; within the Source form or
              documentation, if provided along with the Derivative Works; or,
              within a display generated by the Derivative Works, if and
              wherever such third-party notices normally appear. The contents
              of the NOTICE file are for informational purposes only and
              do not modify the License. You may add Your own attribution
              notices within Derivative Works that You distribute, alongside
              or as an addendum to the NOTICE text from the Work, provided
              that such additional attribution notices cannot be construed
              as modifying the License.

          You may add Your own copyright statement to Your modifications and
          may provide additional or different license terms and conditions
          for use, reproduction, or distribution of Your modifications, or
          for any such Derivative Works as a whole, provided Your use,
          reproduction, and distribution of the Work otherwise complies with
          the conditions stated in this License.

       5. Submission of Contributions. Unless You explicitly state otherwise,
          any Contribution intentionally submitted for inclusion in the Work
          by You to the Licensor shall be under the terms and conditions of
          this License, without any additional terms or conditions.
          Notwithstanding the above, nothing herein shall supersede or modify
          the terms of any separate license agreement you may have executed
          with Licensor regarding such Contributions.

       6. Trademarks. This License does not grant permission to use the trade
          names, trademarks, service marks, or product names of the Licensor,
          except as required for reasonable and customary use in describing the
          origin of the Work and reproducing the content of the NOTICE file.

       7. Disclaimer of Warranty. Unless required by applicable law or
          agreed to in writing, Licensor provides the Work (and each
          Contributor provides its Contributions) on an "AS IS" BASIS,
          WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or
          implied, including, without limitation, any warranties or conditions
          of TITLE, NON-INFRINGEMENT, MERCHANTABILITY, or FITNESS FOR A
          PARTICULAR PURPOSE. You are solely responsible for determining the
          appropriateness of using or redistributing the Work and assume any
          risks associated with Your exercise of permissions under this License.

       8. Limitation of Liability. In no event and under no legal theory,
          whether in tort (including negligence), contract, or otherwise,
          unless required by applicable law (such as deliberate and grossly
          negligent acts) or agreed to in writing, shall any Contributor be
          liable to You for damages, including any direct, indirect, special,
          incidental, or consequential damages of any character arising as a
          result of this License or out of the use or inability to use the
          Work (including but not limited to damages for loss of goodwill,
          work stoppage, computer failure or malfunction, or any and all
          other commercial damages or losses), even if such Contributor
          has been advised of the possibility of such damages.

       9. Accepting Warranty or Additional Liability. While redistributing
          the Work or Derivative Works thereof, You may choose to offer,
          and charge a fee for, acceptance of support, warranty, indemnity,
          or other liability obligations and/or rights consistent with this
          License. However, in accepting such obligations, You may act only
          on Your own behalf and on Your sole responsibility, not on behalf
          of any other Contributor, and only if You agree to indemnify,
          defend, and hold each Contributor harmless for any liability
          incurred by, or claims asserted against, such Contributor by reason
          of your accepting any such warranty or additional liability.

       END OF TERMS AND CONDITIONS

       APPENDIX: How to apply the Apache License to your work.

          To apply the Apache License to your work, attach the following
          boilerplate notice, with the fields enclosed by brackets "[]"
          replaced with your own identifying information. (Don't include
          the brackets!)  The text should be enclosed in the appropriate
          comment syntax for the file format. We also recommend that a
          file or class name and description of purpose be included on the
          same "printed page" as the copyright notice for easier
          identification within third-party archives.

       Copyright [yyyy] [name of copyright owner]

       Licensed under the Apache License, Version 2.0 (the "License");
       you may not use this file except in compliance with the License.
       You may obtain a copy of the License at

           http://www.apache.org/licenses/LICENSE-2.0

       Unless required by applicable law or agreed to in writing, software
       distributed under the License is distributed on an "AS IS" BASIS,
       WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
       See the License for the specific language governing permissions and
       limitations under the License.


    Licenses for support code
    -------------------------

    Parts of the TLS test suite are under the Go license. This code is not included
    in BoringSSL (i.e. libcrypto and libssl) when compiled, however, so
    distributing code linked against BoringSSL does not trigger this license:

    Copyright (c) 2009 The Go Authors. All rights reserved.

    Redistribution and use in source and binary forms, with or without
    modification, are permitted provided that the following conditions are
    met:

       * Redistributions of source code must retain the above copyright
    notice, this list of conditions and the following disclaimer.
       * Redistributions in binary form must reproduce the above
    copyright notice, this list of conditions and the following disclaimer
    in the documentation and/or other materials provided with the
    distribution.
       * Neither the name of Google Inc. nor the names of its
    contributors may be used to endorse or promote products derived from
    this software without specific prior written permission.

    THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
    "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
    LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
    A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
    OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
    SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
    LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
    DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
    THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
    (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
    OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.


    BoringSSL uses the Chromium test infrastructure to run a continuous build,
    trybots etc. The scripts which manage this, and the script for generating build
    metadata, are under the Chromium license. Distributing code linked against
    BoringSSL does not trigger this license.

    Copyright 2015 The Chromium Authors. All rights reserved.

    Redistribution and use in source and binary forms, with or without
    modification, are permitted provided that the following conditions are
    met:

       * Redistributions of source code must retain the above copyright
    notice, this list of conditions and the following disclaimer.
       * Redistributions in binary form must reproduce the above
    copyright notice, this list of conditions and the following disclaimer
    in the documentation and/or other materials provided with the
    distribution.
       * Neither the name of Google Inc. nor the names of its
    contributors may be used to endorse or promote products derived from
    this software without specific prior written permission.

    THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
    "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
    LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
    A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
    OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
    SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
    LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
    DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
    THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
    (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
    OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

    [LICENSE-other-bits]
    Copyright 2015-2025 Brian Smith.

    Permission to use, copy, modify, and/or distribute this software for any
    purpose with or without fee is hereby granted, provided that the above
    copyright notice and this permission notice appear in all copies.

    THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES
    WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF
    MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR ANY
    SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES
    WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN ACTION
    OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF OR IN
    CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.

    [src/polyfill/once_cell/LICENSE-MIT]
    Permission is hereby granted, free of charge, to any
    person obtaining a copy of this software and associated
    documentation files (the "Software"), to deal in the
    Software without restriction, including without
    limitation the rights to use, copy, modify, merge,
    publish, distribute, sublicense, and/or sell copies of
    the Software, and to permit persons to whom the Software
    is furnished to do so, subject to the following
    conditions:

    The above copyright notice and this permission notice
    shall be included in all copies or substantial portions
    of the Software.

    THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF
    ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED
    TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A
    PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT
    SHALL THE AUTHOR OR COPYRIGHT HOLDERS BE LIABLE FOR ANY
    CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
    OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR
    IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
    DEALINGS IN THE SOFTWARE.

### Lucide

- In the app: the line icons on the app's screens (web/index.html and the other pages) are based on Lucide's icons, and the heart in the app's own icon (the exe, its windows and the installer) is based on Lucide's heart
- License: ISC License, with the MIT License for the icons derived from Feather (below, as Lucide gives it)
- Copyright: Copyright (c) 2026 Lucide Icons and Contributors; Copyright (c) 2013-present Cole Bemis
- Source: https://github.com/lucide-icons/lucide

    ISC License

    Copyright (c) 2026 Lucide Icons and Contributors

    Permission to use, copy, modify, and/or distribute this software for any
    purpose with or without fee is hereby granted, provided that the above
    copyright notice and this permission notice appear in all copies.

    THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES
    WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF
    MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR
    ANY SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES
    WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN
    ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF
    OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.

    ---

    The following Lucide icons are derived from the Feather project:

    airplay, alert-circle, alert-octagon, alert-triangle, aperture, arrow-down-circle, arrow-down-left, arrow-down-right, arrow-down, arrow-left-circle, arrow-left, arrow-right-circle, arrow-right, arrow-up-circle, arrow-up-left, arrow-up-right, arrow-up, at-sign, calendar, cast, check, chevron-down, chevron-left, chevron-right, chevron-up, chevrons-down, chevrons-left, chevrons-right, chevrons-up, circle, clipboard, clock, code, columns, command, compass, corner-down-left, corner-down-right, corner-left-down, corner-left-up, corner-right-down, corner-right-up, corner-up-left, corner-up-right, crosshair, database, divide-circle, divide-square, dollar-sign, download, external-link, feather, frown, hash, headphones, help-circle, info, italic, key, layout, life-buoy, link-2, link, loader, lock, log-in, log-out, maximize, meh, minimize, minimize-2, minus-circle, minus-square, minus, monitor, moon, more-horizontal, more-vertical, move, music, navigation-2, navigation, octagon, pause-circle, percent, plus-circle, plus-square, plus, power, radio, rss, search, server, share, shopping-bag, sidebar, smartphone, smile, square, table-2, tablet, target, terminal, trash-2, trash, triangle, tv, type, upload, x-circle, x-octagon, x-square, x, zoom-in, zoom-out

    The MIT License (MIT) (for the icons listed above)

    Copyright (c) 2013-present Cole Bemis

    Permission is hereby granted, free of charge, to any person obtaining a copy
    of this software and associated documentation files (the "Software"), to deal
    in the Software without restriction, including without limitation the rights
    to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
    copies of the Software, and to permit persons to whom the Software is
    furnished to do so, subject to the following conditions:

    The above copyright notice and this permission notice shall be included in all
    copies or substantial portions of the Software.

    THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
    IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
    FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
    AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
    LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
    OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
    SOFTWARE.

### Feather

- In the app: the line icons on the app's screens (web/index.html and the other pages) are based on Feather's icons
- License: MIT (below)
- Copyright: Copyright (c) 2013-2023 Cole Bemis
- Source: https://github.com/feathericons/feather

    The MIT License (MIT)

    Copyright (c) 2013-2023 Cole Bemis

    Permission is hereby granted, free of charge, to any person obtaining a copy
    of this software and associated documentation files (the "Software"), to deal
    in the Software without restriction, including without limitation the rights
    to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
    copies of the Software, and to permit persons to whom the Software is
    furnished to do so, subject to the following conditions:

    The above copyright notice and this permission notice shall be included in all
    copies or substantial portions of the Software.

    THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
    IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
    FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
    AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
    LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
    OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
    SOFTWARE.

### In the module SDK (sdk/SpoitableToolKit.Sdk.*.nupkg)

- In the app: Microsoft.Extensions.Logging.Abstractions.dll, which the SDK package carries for compiling modules
- License: MIT (see "MIT License" under License texts)
- Copyright: Copyright (c) .NET Foundation and Contributors; © Microsoft Corporation. All rights reserved.
- Source: https://github.com/dotnet/runtime


## Downloaded by the app when it needs them

The app does not include these. It downloads them from their publishers when a
feature first needs them, and they come with their own licenses.

### yt-dlp

- When: the Video URL module resolves a video URL (yt-dlp.exe from the project's GitHub releases)
- License: The Unlicense (below)
- Source: https://github.com/yt-dlp/yt-dlp

    This is free and unencumbered software released into the public domain.

    Anyone is free to copy, modify, publish, use, compile, sell, or
    distribute this software, either in source code form or as a compiled
    binary, for any purpose, commercial or non-commercial, and by any
    means.

    In jurisdictions that recognize copyright laws, the author or authors
    of this software dedicate any and all copyright interest in the
    software to the public domain. We make this dedication for the benefit
    of the public at large and to the detriment of our heirs and
    successors. We intend this dedication to be an overt act of
    relinquishment in perpetuity of all present and future rights to this
    software under copyright law.

    THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
    EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
    MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
    IN NO EVENT SHALL THE AUTHORS BE LIABLE FOR ANY CLAIM, DAMAGES OR
    OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE,
    ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
    OTHER DEALINGS IN THE SOFTWARE.

    For more information, please refer to <http://unlicense.org/>

### Deno

- When: yt-dlp needs a JavaScript runtime (deno from the project's GitHub releases)
- License: MIT (see "MIT License" under License texts)
- Copyright: Copyright 2018-2026 the Deno authors
- Source: https://github.com/denoland/deno

### .NET SDK

- When: a module is built from its source and no .NET SDK is installed (from Microsoft's download site)
- License: MIT (see "MIT License" under License texts)
- Copyright: Copyright (c) .NET Foundation and Contributors
- Source: https://github.com/dotnet/sdk

### Packages of a module built from source

- When: a module is built from its source; the build restores the NuGet packages that the module's project lists, each under its own license


## License texts

### MIT License

The MIT License as used by the entries above. Each entry gives its copyright
line, which comes before this text.

    Permission is hereby granted, free of charge, to any person obtaining a copy
    of this software and associated documentation files (the "Software"), to deal
    in the Software without restriction, including without limitation the rights
    to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
    copies of the Software, and to permit persons to whom the Software is
    furnished to do so, subject to the following conditions:

    The above copyright notice and this permission notice shall be included in all
    copies or substantial portions of the Software.

    THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
    IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
    FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
    AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
    LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
    OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
    SOFTWARE.

### Apache License 2.0


                                     Apache License
                               Version 2.0, January 2004
                            http://www.apache.org/licenses/

       TERMS AND CONDITIONS FOR USE, REPRODUCTION, AND DISTRIBUTION

       1. Definitions.

          "License" shall mean the terms and conditions for use, reproduction,
          and distribution as defined by Sections 1 through 9 of this document.

          "Licensor" shall mean the copyright owner or entity authorized by
          the copyright owner that is granting the License.

          "Legal Entity" shall mean the union of the acting entity and all
          other entities that control, are controlled by, or are under common
          control with that entity. For the purposes of this definition,
          "control" means (i) the power, direct or indirect, to cause the
          direction or management of such entity, whether by contract or
          otherwise, or (ii) ownership of fifty percent (50%) or more of the
          outstanding shares, or (iii) beneficial ownership of such entity.

          "You" (or "Your") shall mean an individual or Legal Entity
          exercising permissions granted by this License.

          "Source" form shall mean the preferred form for making modifications,
          including but not limited to software source code, documentation
          source, and configuration files.

          "Object" form shall mean any form resulting from mechanical
          transformation or translation of a Source form, including but
          not limited to compiled object code, generated documentation,
          and conversions to other media types.

          "Work" shall mean the work of authorship, whether in Source or
          Object form, made available under the License, as indicated by a
          copyright notice that is included in or attached to the work
          (an example is provided in the Appendix below).

          "Derivative Works" shall mean any work, whether in Source or Object
          form, that is based on (or derived from) the Work and for which the
          editorial revisions, annotations, elaborations, or other modifications
          represent, as a whole, an original work of authorship. For the purposes
          of this License, Derivative Works shall not include works that remain
          separable from, or merely link (or bind by name) to the interfaces of,
          the Work and Derivative Works thereof.

          "Contribution" shall mean any work of authorship, including
          the original version of the Work and any modifications or additions
          to that Work or Derivative Works thereof, that is intentionally
          submitted to Licensor for inclusion in the Work by the copyright owner
          or by an individual or Legal Entity authorized to submit on behalf of
          the copyright owner. For the purposes of this definition, "submitted"
          means any form of electronic, verbal, or written communication sent
          to the Licensor or its representatives, including but not limited to
          communication on electronic mailing lists, source code control systems,
          and issue tracking systems that are managed by, or on behalf of, the
          Licensor for the purpose of discussing and improving the Work, but
          excluding communication that is conspicuously marked or otherwise
          designated in writing by the copyright owner as "Not a Contribution."

          "Contributor" shall mean Licensor and any individual or Legal Entity
          on behalf of whom a Contribution has been received by Licensor and
          subsequently incorporated within the Work.

       2. Grant of Copyright License. Subject to the terms and conditions of
          this License, each Contributor hereby grants to You a perpetual,
          worldwide, non-exclusive, no-charge, royalty-free, irrevocable
          copyright license to reproduce, prepare Derivative Works of,
          publicly display, publicly perform, sublicense, and distribute the
          Work and such Derivative Works in Source or Object form.

       3. Grant of Patent License. Subject to the terms and conditions of
          this License, each Contributor hereby grants to You a perpetual,
          worldwide, non-exclusive, no-charge, royalty-free, irrevocable
          (except as stated in this section) patent license to make, have made,
          use, offer to sell, sell, import, and otherwise transfer the Work,
          where such license applies only to those patent claims licensable
          by such Contributor that are necessarily infringed by their
          Contribution(s) alone or by combination of their Contribution(s)
          with the Work to which such Contribution(s) was submitted. If You
          institute patent litigation against any entity (including a
          cross-claim or counterclaim in a lawsuit) alleging that the Work
          or a Contribution incorporated within the Work constitutes direct
          or contributory patent infringement, then any patent licenses
          granted to You under this License for that Work shall terminate
          as of the date such litigation is filed.

       4. Redistribution. You may reproduce and distribute copies of the
          Work or Derivative Works thereof in any medium, with or without
          modifications, and in Source or Object form, provided that You
          meet the following conditions:

          (a) You must give any other recipients of the Work or
              Derivative Works a copy of this License; and

          (b) You must cause any modified files to carry prominent notices
              stating that You changed the files; and

          (c) You must retain, in the Source form of any Derivative Works
              that You distribute, all copyright, patent, trademark, and
              attribution notices from the Source form of the Work,
              excluding those notices that do not pertain to any part of
              the Derivative Works; and

          (d) If the Work includes a "NOTICE" text file as part of its
              distribution, then any Derivative Works that You distribute must
              include a readable copy of the attribution notices contained
              within such NOTICE file, excluding those notices that do not
              pertain to any part of the Derivative Works, in at least one
              of the following places: within a NOTICE text file distributed
              as part of the Derivative Works; within the Source form or
              documentation, if provided along with the Derivative Works; or,
              within a display generated by the Derivative Works, if and
              wherever such third-party notices normally appear. The contents
              of the NOTICE file are for informational purposes only and
              do not modify the License. You may add Your own attribution
              notices within Derivative Works that You distribute, alongside
              or as an addendum to the NOTICE text from the Work, provided
              that such additional attribution notices cannot be construed
              as modifying the License.

          You may add Your own copyright statement to Your modifications and
          may provide additional or different license terms and conditions
          for use, reproduction, or distribution of Your modifications, or
          for any such Derivative Works as a whole, provided Your use,
          reproduction, and distribution of the Work otherwise complies with
          the conditions stated in this License.

       5. Submission of Contributions. Unless You explicitly state otherwise,
          any Contribution intentionally submitted for inclusion in the Work
          by You to the Licensor shall be under the terms and conditions of
          this License, without any additional terms or conditions.
          Notwithstanding the above, nothing herein shall supersede or modify
          the terms of any separate license agreement you may have executed
          with Licensor regarding such Contributions.

       6. Trademarks. This License does not grant permission to use the trade
          names, trademarks, service marks, or product names of the Licensor,
          except as required for reasonable and customary use in describing the
          origin of the Work and reproducing the content of the NOTICE file.

       7. Disclaimer of Warranty. Unless required by applicable law or
          agreed to in writing, Licensor provides the Work (and each
          Contributor provides its Contributions) on an "AS IS" BASIS,
          WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or
          implied, including, without limitation, any warranties or conditions
          of TITLE, NON-INFRINGEMENT, MERCHANTABILITY, or FITNESS FOR A
          PARTICULAR PURPOSE. You are solely responsible for determining the
          appropriateness of using or redistributing the Work and assume any
          risks associated with Your exercise of permissions under this License.

       8. Limitation of Liability. In no event and under no legal theory,
          whether in tort (including negligence), contract, or otherwise,
          unless required by applicable law (such as deliberate and grossly
          negligent acts) or agreed to in writing, shall any Contributor be
          liable to You for damages, including any direct, indirect, special,
          incidental, or consequential damages of any character arising as a
          result of this License or out of the use or inability to use the
          Work (including but not limited to damages for loss of goodwill,
          work stoppage, computer failure or malfunction, or any and all
          other commercial damages or losses), even if such Contributor
          has been advised of the possibility of such damages.

       9. Accepting Warranty or Additional Liability. While redistributing
          the Work or Derivative Works thereof, You may choose to offer,
          and charge a fee for, acceptance of support, warranty, indemnity,
          or other liability obligations and/or rights consistent with this
          License. However, in accepting such obligations, You may act only
          on Your own behalf and on Your sole responsibility, not on behalf
          of any other Contributor, and only if You agree to indemnify,
          defend, and hold each Contributor harmless for any liability
          incurred by, or claims asserted against, such Contributor by reason
          of your accepting any such warranty or additional liability.

       END OF TERMS AND CONDITIONS

       APPENDIX: How to apply the Apache License to your work.

          To apply the Apache License to your work, attach the following
          boilerplate notice, with the fields enclosed by brackets "[]"
          replaced with your own identifying information. (Don't include
          the brackets!)  The text should be enclosed in the appropriate
          comment syntax for the file format. We also recommend that a
          file or class name and description of purpose be included on the
          same "printed page" as the copyright notice for easier
          identification within third-party archives.

       Copyright [yyyy] [name of copyright owner]

       Licensed under the Apache License, Version 2.0 (the "License");
       you may not use this file except in compliance with the License.
       You may obtain a copy of the License at

           http://www.apache.org/licenses/LICENSE-2.0

       Unless required by applicable law or agreed to in writing, software
       distributed under the License is distributed on an "AS IS" BASIS,
       WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
       See the License for the specific language governing permissions and
       limitations under the License.
